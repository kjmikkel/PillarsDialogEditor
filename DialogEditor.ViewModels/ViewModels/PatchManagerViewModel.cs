using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Logging;
using DialogEditor.Patch;
using DialogEditor.Patch.Install;
using DialogEditor.Patch.Packaging;
using DialogEditor.Patch.Schema;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class PatchManagerViewModel : ObservableObject
{
    private readonly IFolderPicker _folderPicker;
    private readonly IFilePicker   _filePicker;
    private string?                _patchlistPath;   // path of the last loaded/saved .patchlist

    public ObservableCollection<PatchEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _gameFolder = string.Empty;

    [ObservableProperty] private string _statusText   = string.Empty;
    [ObservableProperty] private bool   _hasConflicts;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveAllModsCommand))]
    private bool _isApplying;

    /// True when the patcher has a backup in the chosen game folder, i.e. "Remove all mods"
    /// has something to undo.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveAllModsCommand))]
    private bool _hasInstalledMods;

    [ObservableProperty] private string _backupStatusText = string.Empty;

    /// Set by the host view: shows the files changed outside the patcher and returns true
    /// when the user chooses to treat them as the new originals.
    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmAcceptExternalChanges { get; set; }

    /// Set by the host view: returns true when the user confirms "Remove all mods".
    public Func<Task<bool>>? ConfirmRemoveAllMods { get; set; }

    public bool HasEntries => Entries.Count > 0;

    /// Conflict rows for the summary list. Rows rather than raw PatchConflicts because
    /// the list shows text, and only this layer can reach Loc.
    public IReadOnlyList<PatchConflictRowViewModel> Conflicts { get; private set; } = [];

    public PatchManagerViewModel(IFolderPicker folderPicker, IFilePicker filePicker)
    {
        _folderPicker = folderPicker;
        _filePicker   = filePicker;
        Entries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasEntries));
            ApplyCommand.NotifyCanExecuteChanged();
            Analyse();
        };
    }

    // ── Add / Remove / Reorder ────────────────────────────────────────────

    [RelayCommand]
    private async Task AddEntries()
    {
        var paths = await _filePicker.PickOpenFilesAsync(
            Loc.Get("PatchManager_AddProjects"),
            ".dialogproject",
            Loc.Get("FileType_DialogProjectOrPack"));

        foreach (var path in paths)
        {
            if (Entries.Any(e => string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                Entries.Add(LoadEntry(path));
            }
            catch (UnsupportedSchemaVersionException ex)
            {
                AppLog.Warn($"PatchManager: refused '{path}': {ex.Message}");
                StatusText = SchemaVersionMessages.TooNew(ex, Path.GetFileName(path));
            }
        }
    }

    /// Loads a .dialogproject or a .dialogpack into an entry. Shared by AddEntries and
    /// LoadFromFile: a saved .patchlist records the pack path itself, so a reload must
    /// extract the pack again — JSON-parsing the zip fails and the vo/ folder is lost.
    /// A load failure yields an error entry rather than throwing, so one bad file
    /// doesn't abort the rest of the list — except a newer file format, which is rethrown
    /// (see the catch below).
    private static PatchEntryViewModel LoadEntry(string path)
    {
        string? tempDir = null;
        try
        {
            string projectFilePath = path;
            string? voFolder = null;

            if (DialogPackHelper.IsDialogPack(path))
            {
                // TempDir is kept alive until apply (vo/ is needed) or removal.
                var extracted   = DialogPackHelper.Extract(path);
                projectFilePath = extracted.ProjectFilePath;
                voFolder        = extracted.VoFolderPath;
                tempDir         = extracted.TempDir;
            }

            var project = DialogProjectSerializer.LoadFromFile(projectFilePath);
            return new PatchEntryViewModel(path, project, voFolder, tempDir);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnsupportedSchemaVersionException)
        {
            // Not an error entry: Apply skips unloaded entries, which would half-apply the
            // stack without this mod (GitHub issue 62). The callers refuse it instead.
            DeleteTempDir(tempDir);
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load '{path}'", ex);
            // An error entry owns no temp dir, so nothing would ever delete it.
            DeleteTempDir(tempDir);
            return new PatchEntryViewModel(path, ex.Message);
        }
    }

    private static void DeleteTempDir(string? tempDir)
    {
        if (tempDir is null) return;
        try { Directory.Delete(tempDir, recursive: true); }
        catch (Exception cleanupEx) { AppLog.Warn($"PatchManager: failed to delete temp dir '{tempDir}': {cleanupEx.Message}"); }
    }

    [RelayCommand]
    private void RemoveEntry(PatchEntryViewModel? entry)
    {
        if (entry is null) return;
        Entries.Remove(entry);
        if (entry.TempDir is not null)
        {
            try { Directory.Delete(entry.TempDir, recursive: true); }
            catch (Exception ex) { AppLog.Warn($"PatchManager: failed to delete temp dir '{entry.TempDir}': {ex.Message}"); }
        }
    }

    [RelayCommand]
    private void MoveUp(PatchEntryViewModel? entry)
    {
        if (entry is null) return;
        var i = Entries.IndexOf(entry);
        if (i > 0) Entries.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveDown(PatchEntryViewModel? entry)
    {
        if (entry is null) return;
        var i = Entries.IndexOf(entry);
        if (i >= 0 && i < Entries.Count - 1) Entries.Move(i, i + 1);
    }

    // ── Game folder ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseGameFolder()
    {
        var path = await _folderPicker.PickFolderAsync(Loc.Get("Dialog_SelectFolder"));
        if (path is not null) GameFolder = path;
    }

    // ── Conflict detection ────────────────────────────────────────────────

    private void Analyse()
    {
        // Conflict indices refer to THIS list (loaded entries only), not to Entries: an
        // entry that failed to load has no patches to compare. Map them back through it,
        // or a broken entry above a clashing pair shifts the badges onto the wrong rows.
        var loaded = Entries.Where(e => e.IsLoaded).ToList();
        var projects = loaded
            .Select(e => (e.ProjectName, e.Project!.Patches as IReadOnlyDictionary<string, ConversationPatch>))
            .ToList();

        Conflicts    = ConflictDetector.Detect(projects)
                                       .Select(c => new PatchConflictRowViewModel(c,
                                           loaded[c.FirstPatchIndex].ProjectName,
                                           loaded[c.SecondPatchIndex].ProjectName))
                                       .ToList();
        HasConflicts = Conflicts.Count > 0;

        var conflictedEntries = Conflicts
            .SelectMany(c => new[] { loaded[c.Conflict.FirstPatchIndex], loaded[c.Conflict.SecondPatchIndex] })
            .ToHashSet();

        foreach (var entry in Entries)
            entry.HasConflict = conflictedEntries.Contains(entry);

        StatusText = HasConflicts
            ? Loc.FormatCount("PatchManager_ConflictsFound", Conflicts.Count)
            : Entries.Count > 0 ? Loc.Get("PatchManager_NoConflicts") : string.Empty;
    }

    // ── Save / Load load order ────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveLoadOrder()
    {
        var path = await _filePicker.PickSaveFileAsync(
            Loc.Get("PatchManager_SaveLoadOrder"),
            "my_loadorder",
            ".patchlist",
            Loc.Get("FileType_PatchList"));
        if (path is null) return;

        _patchlistPath = path;
        var list = BuildPatchList(path);
        try
        {
            PatchListSerializer.SaveToFile(path, list);
            AppLog.Info($"Saved load order: {path}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to save load order", ex);
            StatusText = Loc.Format("PatchManager_ApplyError", ex.Message);
        }
    }

    [RelayCommand]
    private async Task LoadLoadOrder()
    {
        var path = await _filePicker.PickOpenFileAsync(
            Loc.Get("PatchManager_LoadLoadOrder"),
            ".patchlist",
            Loc.Get("FileType_PatchList"));
        if (path is null) return;
        LoadFromFile(path);
    }

    public void LoadFromFile(string path)
    {
        // Entries are loaded into a local list and only committed once all of them have
        // loaded, so a refused load order leaves the current one untouched.
        var loaded = new List<PatchEntryViewModel>();
        string? current = null;
        try
        {
            var list = PatchListSerializer.LoadFromFile(path);
            foreach (var entry in list.Entries)
            {
                current = PatchListSerializer.ResolvePath(path, entry);
                loaded.Add(LoadEntry(current));
            }

            _patchlistPath = path;
            GameFolder     = list.GameFolder;
            Entries.Clear();
            foreach (var e in loaded) Entries.Add(e);
        }
        catch (UnsupportedSchemaVersionException ex)
        {
            // One newer mod refuses the whole load order, so the rest is never applied
            // without it (GitHub issues 62, 79). current is null when the .patchlist itself is newer.
            foreach (var e in loaded) DeleteTempDir(e.TempDir);
            var offender = Path.GetFileName(current ?? path);
            AppLog.Warn($"PatchManager: refused load order '{path}' ({offender}): {ex.Message}");
            StatusText = SchemaVersionMessages.TooNew(ex, offender);
        }
        catch (Exception ex)
        {
            foreach (var e in loaded) DeleteTempDir(e.TempDir);
            AppLog.Error($"Failed to load load order '{path}'", ex);
            StatusText = Loc.Format("PatchManager_LoadError", path, ex.Message);
        }
    }

    private PatchList BuildPatchList(string patchlistPath)
    {
        var entries = Entries
            .Select(e => PatchListSerializer.BuildEntry(patchlistPath, e.FullPath))
            .ToList();
        return new PatchList(PatchList.CurrentSchemaVersion, GameFolder, entries);
    }

    // ── Apply ─────────────────────────────────────────────────────────────
    // Apply = restore the originals, then apply the whole list (PatchInstaller, issue #76), so
    // applying again after reordering or removing entries replaces the previous mods.

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task Apply()
    {
        var provider = GameDataProviderFactory.Detect(GameFolder);
        if (provider is null)
        {
            StatusText = Loc.Get("Status_FolderNotRecognized");
            return;
        }

        IsApplying = true;
        StatusText = Loc.Get("PatchManager_Applying");
        ApplyCommand.NotifyCanExecuteChanged();

        var entries = Entries.Where(e => e.IsLoaded)
                             .Select(e => new InstallEntry(e.Project!, e.VoFolder))
                             .ToList();
        var gameFolder = GameFolder;
        try
        {
            var result = await Task.Run(() =>
                PatchInstaller.Install(provider, gameFolder, entries, new InstallOptions()));

            if (result is InstallResult.ExternalChanges ext)
            {
                var accept = ConfirmAcceptExternalChanges is not null
                          && await ConfirmAcceptExternalChanges(ext.Paths);
                if (!accept)
                {
                    AppLog.Warn($"Apply cancelled: {ext.Paths.Count} file(s) changed outside the patcher");
                    StatusText = Loc.Get("PatchManager_ApplyCancelledExternal");
                    return;
                }
                result = await Task.Run(() => PatchInstaller.Install(provider, gameFolder, entries,
                                                                     new InstallOptions(AcceptCurrentFiles: true)));
            }

            var applied = (InstallResult.Applied)result;
            foreach (var m in applied.MissingConversations)
                AppLog.Warn($"Conversation not found for patch: {m}");
            foreach (var p in applied.RestoreSkipped)
                AppLog.Warn($"Left as is (changed outside the patcher): {p}");
            AppLog.Info($"Applied {applied.ConversationsPatched} conversation(s) from {entries.Count} project(s)");
            StatusText = Loc.FormatCount("PatchManager_ApplySuccess", applied.ConversationsPatched, gameFolder);
        }
        catch (PatcherBackupCorruptException ex)
        {
            AppLog.Error("Patch application refused: backup manifest unreadable", ex);
            StatusText = Loc.Format("PatchManager_BackupCorrupt", ex.ManifestPath);
        }
        catch (Exception ex)
        {
            AppLog.Error("Patch application failed", ex);
            StatusText = Loc.Format("PatchManager_ApplyError", ex.Message);
        }
        finally
        {
            IsApplying = false;
            ApplyCommand.NotifyCanExecuteChanged();
            RefreshBackupStatus();
        }
    }

    private bool CanApply() => !string.IsNullOrEmpty(GameFolder)
                             && Entries.Count > 0
                             && !IsApplying;

    // ── Remove all mods ───────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRemoveAllMods))]
    private async Task RemoveAllMods()
    {
        if (ConfirmRemoveAllMods is null || !await ConfirmRemoveAllMods()) return;

        IsApplying = true;
        ApplyCommand.NotifyCanExecuteChanged();
        var gameFolder = GameFolder;
        try
        {
            var r = await Task.Run(() => PatchInstaller.Restore(gameFolder));
            foreach (var p in r.Skipped)
                AppLog.Warn($"Remove all mods left a file as is (changed outside the patcher): {p}");
            AppLog.Info($"Removed all mods: {r.Restored} file(s) restored, {r.Skipped.Count} skipped");
            StatusText = r.Skipped.Count == 0
                ? Loc.FormatCount("PatchManager_RemoveAllDone", r.Restored)
                : Loc.Format("PatchManager_RemoveAllPartial", r.Restored, r.Skipped.Count);
        }
        catch (PatcherBackupCorruptException ex)
        {
            AppLog.Error("Remove all mods refused: backup manifest unreadable", ex);
            StatusText = Loc.Format("PatchManager_BackupCorrupt", ex.ManifestPath);
        }
        catch (Exception ex)
        {
            AppLog.Error("Remove all mods failed", ex);
            StatusText = Loc.Format("PatchManager_RemoveAllError", ex.Message);
        }
        finally
        {
            IsApplying = false;
            ApplyCommand.NotifyCanExecuteChanged();
            RefreshBackupStatus();
        }
    }

    private bool CanRemoveAllMods() => HasInstalledMods && !IsApplying;

    // ── Backup status ─────────────────────────────────────────────────────

    partial void OnGameFolderChanged(string value) => RefreshBackupStatus();

    /// Tells the player whether mods are installed in the chosen folder (and so whether
    /// "Remove all mods" has anything to undo).
    public void RefreshBackupStatus()
    {
        if (string.IsNullOrEmpty(GameFolder) || !Directory.Exists(GameFolder))
        {
            HasInstalledMods = false;
            BackupStatusText = string.Empty;
            return;
        }
        try
        {
            HasInstalledMods = PatchInstaller.HasInstalledMods(GameFolder);
            BackupStatusText = HasInstalledMods
                ? Loc.FormatCount("PatchManager_ModsInstalled",
                                  PatcherBackupStore.Open(GameFolder).Entries.Count)
                : Loc.Get("PatchManager_NoModsInstalled");
        }
        catch (PatcherBackupCorruptException ex)
        {
            AppLog.Error("Patch Manager: backup manifest unreadable", ex);
            HasInstalledMods = false;
            BackupStatusText = Loc.Format("PatchManager_BackupCorrupt", ex.ManifestPath);
        }
    }
}
