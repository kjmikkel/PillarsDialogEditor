using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
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
    [ObservableProperty] private bool   _isApplying;

    public bool HasEntries => Entries.Count > 0;

    public IReadOnlyList<PatchConflict> Conflicts { get; private set; } = [];

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

            PatchEntryViewModel entry;
            try
            {
                string projectFilePath = path;
                string? voFolder = null;

                string? tempDir = null;
                if (DialogPackHelper.IsDialogPack(path))
                {
                    // TempDir is kept alive until apply (vo/ is needed) or removal.
                    var extracted   = DialogPackHelper.Extract(path);
                    projectFilePath = extracted.ProjectFilePath;
                    voFolder        = extracted.VoFolderPath;
                    tempDir         = extracted.TempDir;
                }

                var project = DialogProjectSerializer.LoadFromFile(projectFilePath);
                entry = new PatchEntryViewModel(path, project, voFolder, tempDir);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to load '{path}'", ex);
                entry = new PatchEntryViewModel(path, ex.Message);
            }
            Entries.Add(entry);
        }
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
        var projects = Entries
            .Where(e => e.IsLoaded)
            .Select(e => (e.ProjectName, e.Project!.Patches as IReadOnlyDictionary<string, ConversationPatch>))
            .ToList();

        Conflicts    = ConflictDetector.Detect(projects);
        HasConflicts = Conflicts.Count > 0;

        var conflictedEntries = Conflicts
            .SelectMany(c => new[] { c.FirstPatchIndex, c.SecondPatchIndex })
            .ToHashSet();

        for (int i = 0; i < Entries.Count; i++)
            Entries[i].HasConflict = conflictedEntries.Contains(i);

        StatusText = HasConflicts
            ? Loc.Format("PatchManager_ConflictsFound", Conflicts.Count)
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
        try
        {
            var list = PatchListSerializer.LoadFromFile(path);
            _patchlistPath = path;
            GameFolder     = list.GameFolder;
            Entries.Clear();

            foreach (var entry in list.Entries)
            {
                var resolved = PatchListSerializer.ResolvePath(path, entry);
                PatchEntryViewModel vm;
                try
                {
                    var project = DialogProjectSerializer.LoadFromFile(resolved);
                    vm = new PatchEntryViewModel(resolved, project);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to load project '{resolved}'", ex);
                    vm = new PatchEntryViewModel(resolved, ex.Message);
                }
                Entries.Add(vm);
            }
        }
        catch (Exception ex)
        {
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

        try
        {
            await Task.Run(() => ApplyPatches(provider));
            var convCount = Entries.Where(e => e.IsLoaded)
                                   .Sum(e => e.Project!.Patches.Count);
            AppLog.Info($"Applied {convCount} patch(es) from {Entries.Count} project(s)");
            StatusText = Loc.Format("PatchManager_ApplySuccess", convCount, GameFolder);
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
        }
    }

    private bool CanApply() => !string.IsNullOrEmpty(GameFolder)
                             && Entries.Count > 0
                             && !IsApplying;

    private void ApplyPatches(IGameDataProvider provider)
    {
        // Collect all conversation names across all loaded projects
        var allConversations = Entries
            .Where(e => e.IsLoaded)
            .SelectMany(e => e.Project!.Patches.Keys)
            .Concat(Entries.Where(e => e.IsLoaded)
                           .SelectMany(e => e.Project!.NewConversations ?? []))
            .Distinct()
            .ToList();

        foreach (var convName in allConversations)
        {
            // Gather patches in order
            var patches = Entries
                .Where(e => e.IsLoaded && e.Project!.Patches.ContainsKey(convName))
                .Select(e => e.Project!.Patches[convName])
                .ToList();

            if (patches.Count == 0) continue;

            var merged = PatchMerger.Merge(convName, patches);

            // Resolve the conversation file (handles new conversations too)
            var isNew = Entries.Any(e => e.IsLoaded
                                      && (e.Project!.NewConversations?.Contains(convName) == true));
            var file  = provider.FindConversation(convName)
                     ?? (isNew ? provider.BuildNewConversationFile(convName) : null);

            if (file is null)
            {
                AppLog.Warn($"Conversation not found for patch: {convName}");
                continue;
            }

            if (!File.Exists(file.ConversationPath))
                provider.InitializeConversationFile(file);

            var conversation = provider.LoadConversation(file);
            var baseSnap     = ConversationSnapshotBuilder.Build(conversation);
            var applied      = PatchApplier.Apply(baseSnap, merged);
            provider.SaveConversation(file, applied);
        }

        // Copy any VO files from .dialogpack entries
        var gameVoRoot = Path.Combine(GameFolder,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");

        foreach (var entry in Entries.Where(e => e.IsLoaded && e.VoFolder is not null))
        {
            try
            {
                DialogPackHelper.CopyVoToGame(entry.VoFolder!, gameVoRoot);
                AppLog.Info($"Copied VO files from: {entry.FullPath}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to copy VO from '{entry.FullPath}': {ex.Message}", ex);
                throw; // surface to caller
            }
        }
    }
}
