using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Backup;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

// Poe1/Poe2 provider types are only referenced via IGameDataProvider interface;
// GameDataProviderFactory.Detect returns the concrete type but we only care about the interface.

namespace DialogEditor.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFolderPicker _folderPicker;
    private readonly IFilePicker   _filePicker;

    public GameBrowserViewModel  Browser { get; }
    public ConversationViewModel Canvas  { get; }
    public NodeDetailViewModel   Detail  { get; } = new();

    // ── Active project ────────────────────────────────────────────────────
    private DialogProject? _project;
    private string?        _projectPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _currentProjectName;

    private IGameDataProvider? _provider;
    private ConversationFile?  _currentFile;

    public IGameDataProvider? Provider => _provider;
    private string             _currentGameDirectory = string.Empty;
    private string             _activeGameId         = string.Empty;

    // Paths of conversation files created during the current test session.
    // On restore, these are deleted (there was no original to restore to).
    private readonly List<string> _createdConversationPaths = [];

    /// Set by the UI layer to provide a name-input dialog for new conversations.
    public Func<Task<string?>>? RequestConversationName { get; set; }

    /// Set by the UI layer to surface a patch conflict and ask whether to force-apply.
    /// Returns true if the user chooses Force Apply, false to cancel.
    public Func<PatchConflictException, Task<bool>>? RequestConflictResolution { get; set; }

    /// Set by the UI layer to show a language-code input dialog.
    /// Takes (title, defaultValue) and returns the entered language code, or null if cancelled.
    public Func<string, string?, Task<string?>>? RequestLanguageCode { get; set; }

    /// Conditions from the catalogue filtered to the currently loaded game.
    public IReadOnlyList<ConditionEntry> ActiveConditions
        => string.IsNullOrEmpty(_activeGameId)
            ? []
            : ConditionCatalogue.Instance.ForGame(_activeGameId);

    [ObservableProperty] private string              _statusText          = Loc.Get("Status_OpenFolder");
    [ObservableProperty] private IReadOnlyList<string> _availableLanguages = [];
    [ObservableProperty] private string              _selectedLanguage    = string.Empty;
    [ObservableProperty] private bool                _isModified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowserFlyoutOpen))]
    private bool _isBrowserExpanded = AppSettings.BrowserPinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowserFlyoutOpen))]
    private bool _isBrowserPinned = AppSettings.BrowserPinned;

    [ObservableProperty] private bool    _isDetailExpanded       = AppSettings.DetailExpanded;
    [ObservableProperty] private string? _currentConversationName;

    public bool IsBrowserFlyoutOpen => IsBrowserExpanded && !IsBrowserPinned;

    // ── Window title reflects dirty state ─────────────────────────────────
    public string WindowTitle
    {
        get
        {
            var project = CurrentProjectName is not null ? $" [{CurrentProjectName}]" : string.Empty;
            var dirty   = IsModified && CurrentConversationName is not null ? "● " : string.Empty;
            return CurrentConversationName is not null
                ? $"{dirty}{CurrentConversationName}{project}"
                : Loc.Get("App_Title") + project;
        }
    }

    // ── Unsaved-changes navigation guard ──────────────────────────────────
    private ConversationFile? _pendingFile;
    private Action?           _pendingAction;   // for close/project-switch continuations
    public event Action? UnsavedChangesRequested;

    /// Guard that fires UnsavedChangesRequested if dirty, otherwise runs action immediately.
    public void GuardDirtyThen(Action proceed)
    {
        if (IsModified && CurrentConversationName is not null)
        {
            _pendingAction = proceed;
            UnsavedChangesRequested?.Invoke();
        }
        else
        {
            proceed();
        }
    }

    public void SaveAndProceed()
    {
        SaveCommand.Execute(null);
        Proceed();
    }

    public void DiscardAndProceed() => Proceed();

    public void CancelPendingNavigation()
    {
        _pendingFile   = null;
        _pendingAction = null;
    }

    private void Proceed()
    {
        var file   = _pendingFile;
        var action = _pendingAction;
        _pendingFile   = null;
        _pendingAction = null;
        if (file   is not null) LoadConversationFile(file);
        action?.Invoke();
    }

    // ── Project open state ────────────────────────────────────────────────
    public bool IsProjectOpen => _project is not null;

    private void SetProject(DialogProject? project)
    {
        var prevNew  = _project?.NewConversations;
        var nextNew  = project?.NewConversations;
        _project = project;
        Canvas.IsEditable = project is not null;
        OnPropertyChanged(nameof(IsProjectOpen));
        SaveProjectCommand.NotifyCanExecuteChanged();
        NewConversationCommand.NotifyCanExecuteChanged();
        MergeProjectsCommand.NotifyCanExecuteChanged();
        ExportForTranslationCommand.NotifyCanExecuteChanged();
        ImportTranslationCommand.NotifyCanExecuteChanged();
        // Only re-scan the game folder when NewConversations actually changes —
        // not on every patch save, which would re-enumerate all conversations.
        if (_provider is not null && !ReferenceEquals(prevNew, nextNew))
            Browser.Load(_provider, nextNew);
    }

    // ── Partial hooks ─────────────────────────────────────────────────────
    partial void OnIsBrowserPinnedChanged(bool value)
    {
        AppSettings.BrowserPinned = value;
        IsBrowserExpanded = value;
    }

    partial void OnIsDetailExpandedChanged(bool value) => AppSettings.DetailExpanded = value;

    partial void OnCurrentConversationNameChanged(string? value)
        => OnPropertyChanged(nameof(WindowTitle));

    partial void OnIsModifiedChanged(bool value)
        => OnPropertyChanged(nameof(WindowTitle));

    // ── Constructor ───────────────────────────────────────────────────────
    public MainWindowViewModel(IDispatcher dispatcher, IFolderPicker folderPicker, IFilePicker filePicker)
    {
        _folderPicker = folderPicker;
        _filePicker   = filePicker;
        Browser = new GameBrowserViewModel(dispatcher);
        Canvas  = new ConversationViewModel(dispatcher);

        Browser.ConversationSelected += OnConversationSelected;

        Detail.AddLinkRequested += (fromId, toId) =>
        {
            var src = Canvas.Nodes.FirstOrDefault(n => n.NodeId == fromId);
            var tgt = Canvas.Nodes.FirstOrDefault(n => n.NodeId == toId);
            if (src is not null && tgt is not null)
                Canvas.AddConnection(src.Output, tgt.Input);
        };

        Detail.DeleteLinkRequested += toId =>
        {
            var conn = Canvas.Connections.FirstOrDefault(c =>
                c.Source.Owner == Canvas.SelectedNode &&
                c.Target.Owner?.NodeId == toId);
            if (conn is not null)
                Canvas.DeleteConnection(conn);
        };

        Canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversationViewModel.SelectedNode))
            {
                Detail.Load(Canvas.SelectedNode);
                RefreshDetailLinks();
            }
            if (e.PropertyName == nameof(ConversationViewModel.IsModified))
            {
                IsModified = Canvas.IsModified;
                SaveCommand.NotifyCanExecuteChanged();
                SaveProjectCommand.NotifyCanExecuteChanged();
            }
        };

        Canvas.Connections.CollectionChanged += (_, args) =>
        {
            RefreshDetailLinks();
            // Subscribe to property changes on newly added connections so edits
            // (QuestionNodeTextDisplay, RandomWeight) mark the canvas as modified.
            if (args.NewItems is not null)
                foreach (ConnectionViewModel conn in args.NewItems)
                    conn.PropertyChanged += (_, _) => Canvas.IsModified = true;
        };

        var last = AppSettings.LastGameDirectory;
        if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
            LoadDirectory(last);

        // Crash recovery: if a test was in progress when the app closed, re-enter test mode
        if (AppSettings.GetPendingRestores() is not null)
            TestModeEntered?.Invoke();

        // Re-open last project if one was open
        var lastProject = AppSettings.LastProjectPath;
        if (!string.IsNullOrEmpty(lastProject) && File.Exists(lastProject))
            LoadProject(lastProject);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_provider is null || string.IsNullOrEmpty(value)) return;
        _provider.Language = value;
        AppSettings.LastLanguage = value;
        if (_currentFile is not null)
            OnConversationSelected(_currentFile);
    }

    // ── Link list sync ────────────────────────────────────────────────────
    private void RefreshDetailLinks()
    {
        if (Canvas.SelectedNode is null) return;
        Detail.RefreshLinks(
            Canvas.Connections
                .Where(c => c.Source.Owner == Canvas.SelectedNode));
    }

    // ── Settings ──────────────────────────────────────────────────────────
    public SettingsViewModel CreateSettingsViewModel()
        => new(_currentGameDirectory, _folderPicker);

    // ── Project — New / Open / Save ───────────────────────────────────────
    [RelayCommand]
    private void NewProject()
        => GuardDirtyThen(() => _ = DoNewProject());

    private async Task DoNewProject()
    {
        var path = await _filePicker.PickSaveFileAsync(
            Loc.Get("Dialog_NewProject"),
            Loc.Get("Dialog_NewProjectDefault"),
            ".dialogproject",
            Loc.Get("FileType_DialogProject"));
        if (path is null) return;
        var name = Path.GetFileNameWithoutExtension(path);
        SetProject(DialogProject.Empty(name));
        _projectPath = path;
        DialogProjectSerializer.SaveToFile(path, _project!);
        AppSettings.LastProjectPath = path;
        CurrentProjectName = name;
        AppLog.Info($"New project: {path}");
        StatusText = Loc.Format("Status_ProjectNew", name);
    }

    [RelayCommand]
    private void OpenProject()
        => GuardDirtyThen(() => _ = DoOpenProject());

    private async Task DoOpenProject()
    {
        var path = await _filePicker.PickOpenFileAsync(
            Loc.Get("Dialog_OpenProject"),
            ".dialogproject",
            Loc.Get("FileType_DialogProject"));
        if (path is null) return;
        LoadProject(path);
    }

    private void LoadProject(string path)
    {
        try
        {
            var loaded = DialogProjectSerializer.LoadFromFile(path);
            SetProject(loaded);
            _projectPath = path;
            AppSettings.LastProjectPath = path;
            CurrentProjectName = loaded.Name;
            AppLog.Info($"Opened project: {path}");
            StatusText = Loc.Format("Status_ProjectOpened", loaded.Name, loaded.Patches.Count);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to open project '{path}'", ex);
            StatusText = Loc.Format("Status_ProjectOpenError", path, ex.Message);
        }
    }

    // ── New conversation ──────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanCreateConversation))]
    private async Task NewConversation()
    {
        if (_provider is null || _project is null || RequestConversationName is null) return;

        var name = (await RequestConversationName())?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        // Validate: no path separators or other illegal chars
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusText = Loc.Format("Status_NewConversationDuplicate", name);
            return;
        }

        // Conflict check
        var alreadyExists = _provider.FindConversation(name) is not null
                         || (_project.NewConversations?.Contains(name) == true);
        if (alreadyExists)
        {
            StatusText = Loc.Format("Status_NewConversationDuplicate", name);
            return;
        }

        var file = _provider.BuildNewConversationFile(name);
        SetProject(_project.WithNewConversation(name));

        // Load an empty canvas immediately
        LoadNewConversation(file);
        StatusText = Loc.Format("Status_NewConversationAdded", name);
        AppLog.Info($"New conversation '{name}' added to project");
    }

    private bool CanCreateConversation() => _provider is not null && _project is not null;

    private void LoadNewConversation(ConversationFile file)
    {
        _currentFile = file;
        var empty    = new Conversation(file.Name, [], StringTable.Empty);

        // If the project already has a patch (e.g. re-opened project), reconstruct from it
        if (_project?.Patches.TryGetValue(file.Name, out var existingPatch) == true)
        {
            var baseSnap     = new ConversationEditSnapshot([]);
            var appliedSnap  = PatchApplier.Apply(baseSnap, existingPatch);
            var translations = existingPatch.Translations
                .GetValueOrDefault(_provider?.Language ?? "en");
            var restored     = ConversationSnapshotBuilder.ToConversation(file.Name, appliedSnap, translations);
            Canvas.Load(restored);
        }
        else
        {
            Canvas.Load(empty);
        }

        var savedLayout = _project?.GetLayout(file.Name);
        if (savedLayout is not null) Canvas.RestoreLayout(savedLayout);

        Detail.Clear();
        IsModified = false;
        CurrentConversationName = file.Name;
        if (!IsBrowserPinned) IsBrowserExpanded = false;
    }

    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private void SaveProject()
    {
        if (_project is null || _projectPath is null || _currentFile is null || Canvas.BaseSnapshot is null) return;
        try
        {
            var patch    = DiffEngine.Diff(_currentFile.Name, Canvas.BaseSnapshot, Canvas.BuildSnapshot(), _provider!.Language);
            var layout   = Canvas.GetCurrentLayout();
            SetProject(_project!.WithPatch(patch).WithLayout(_currentFile.Name, layout));
            DialogProjectSerializer.SaveToFile(_projectPath, _project);
            Canvas.IsModified = false;
            IsModified = false;
            SaveCommand.NotifyCanExecuteChanged();
            SaveProjectCommand.NotifyCanExecuteChanged();
            AppLog.Info($"Project saved: {_projectPath}");
            StatusText = Loc.Format("Status_ProjectSaved", _project.Name);
            ConversationSaved?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save project", ex);
            StatusText = Loc.Format("Status_SaveError", _project?.Name ?? "?", ex.Message);
        }
    }

    private bool CanSaveProject() =>
        _project is not null && _projectPath is not null &&
        _currentFile is not null && IsModified;

    // ── Merge projects ────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanMergeProjects))]
    private async Task MergeProjects()
    {
        if (_project is null) return;

        var paths = await _filePicker.PickOpenFilesAsync(
            Loc.Get("Dialog_MergeProjects"),
            ".dialogproject",
            Loc.Get("FileType_DialogProject"));
        if (paths.Count == 0) return;

        try
        {
            var merged = _project;
            foreach (var path in paths)
            {
                var other = DialogProjectSerializer.LoadFromFile(path);
                merged = merged.MergeWith(other);
            }
            SetProject(merged);
            // Save immediately — the merged patches are already computed, no canvas diff needed.
            // IsModified and Canvas.IsModified are intentionally left unchanged so the user's
            // open-conversation edit state is preserved.
            DialogProjectSerializer.SaveToFile(_projectPath!, merged);
            AppLog.Info($"Merged {paths.Count} project(s) into '{merged.Name}'");
            StatusText = Loc.Format("Status_MergeComplete", paths.Count, merged.Name);
        }
        catch (Exception ex)
        {
            AppLog.Error("Merge projects failed", ex);
            StatusText = Loc.Format("Status_MergeError", ex.Message);
        }
    }

    private bool CanMergeProjects() => _project is not null && _projectPath is not null;

    // ── Export / Import for translation ──────────────────────────────────

    private bool IsProjectLoaded() => _project is not null;

    [RelayCommand(CanExecute = nameof(IsProjectLoaded))]
    private async Task ExportForTranslation()
    {
        if (_project is null) return;
        var fmt = ParseFormat(AppSettings.DefaultLocalizationFormat);
        var ext = FormatExtension(fmt);
        var path = await _filePicker.PickSaveFileAsync(
            "Export for Translation",
            "export" + ext,
            new[] { (".csv", "CSV"), (".json", "JSON"), (".xlf", "XLIFF") });
        if (path is null) return;
        var lang = await (RequestLanguageCode?.Invoke("Source language", _provider?.Language)
                          ?? Task.FromResult<string?>(_provider?.Language));
        if (lang is null) return;
        LocalizationExportService.Export(_project, path, fmt, lang);
        StatusText = string.Format("Exported {0} entries to {1}", 0, path);
    }

    [RelayCommand(CanExecute = nameof(IsProjectLoaded))]
    private async Task ImportTranslation()
    {
        if (_project is null) return;
        var path = await _filePicker.PickOpenFileAsync(
            "Import Translation",
            ".csv,.json,.xlf,.xliff",
            "Translation Files");
        if (path is null) return;
        var fmt  = DetectFormat(path);
        var lang = await (RequestLanguageCode?.Invoke("Target language", null)
                          ?? Task.FromResult<string?>(null));
        if (lang is null) return;
        _project  = LocalizationImportService.Import(_project, path, fmt, lang);
        IsModified = true;
        StatusText = string.Format("Imported translation for language '{0}'", lang);
    }

    private static LocalizationExportFormat ParseFormat(string value) =>
        Enum.TryParse<LocalizationExportFormat>(value, ignoreCase: true, out var result)
            ? result
            : LocalizationExportFormat.Csv;

    private static string FormatExtension(LocalizationExportFormat fmt) => fmt switch
    {
        LocalizationExportFormat.Json  => ".json",
        LocalizationExportFormat.Xliff => ".xlf",
        _                              => ".csv",
    };

    private static LocalizationExportFormat DetectFormat(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".json"  => LocalizationExportFormat.Json,
            ".xlf"   => LocalizationExportFormat.Xliff,
            ".xliff" => LocalizationExportFormat.Xliff,
            _        => LocalizationExportFormat.Csv,
        };
    }

    // ── Open folder ───────────────────────────────────────────────────────
    [RelayCommand]
    private async Task OpenFolder()
    {
        var path = await _folderPicker.PickFolderAsync(Loc.Get("Dialog_SelectFolder"));
        if (path is null) return;
        LoadDirectory(path);
    }

    private void LoadDirectory(string path)
    {
        var provider = GameDataProviderFactory.Detect(path);
        if (provider is null)
        {
            AppLog.Warn($"Folder not recognised as PoE1 or PoE2 root: {path}");
            StatusText = Loc.Get("Status_FolderNotRecognized");
            return;
        }

        try
        {
            AppSettings.LastGameDirectory = path;
            _currentGameDirectory = path;
            _provider = provider;
            CurrentConversationName = null;
            SpeakerNameService.Register(provider.LoadSpeakerNames());
            _activeGameId = provider.GameId;
            Detail.ActiveGameId = provider.GameId;
            AvailableLanguages = provider.AvailableLanguages;
            SelectedLanguage   = AppSettings.PickLanguage(AvailableLanguages, AppSettings.LastLanguage);
            Browser.Load(provider, _project?.NewConversations);
            AppLog.Info($"Loaded {provider.GameName} from {path}");
            StatusText = Loc.Format("Status_FolderLoaded", provider.GameName, path);

            if (!AppSettings.IsKnownGameDirectory(path))
                _ = OfferBackupAsync(path);
            AppSettings.MarkGameDirectoryKnown(path);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to initialise game data from {path}", ex);
            StatusText = Loc.Format("Status_LoadError", path, ex.Message);
        }
    }

    // ── Backup offer (first time per game folder) ─────────────────────────
    private async Task OfferBackupAsync(string gameDirectory)
    {
        var pick = await _folderPicker.PickFolderAsync(Loc.Get("Dialog_SelectBackupFolder"));
        if (pick is null) return;

        var timestamp  = DateTime.Now.ToString("yyyy-MM-ddTHH-mm");
        var backupRoot = Path.Combine(pick, timestamp);
        AppSettings.SetBackupPath(gameDirectory, pick);

        StatusText = Loc.Get("Status_BackupInProgress");
        try
        {
            await RunProviderBackupAsync(backupRoot, _provider!);
            AppLog.Info($"Backup written to {backupRoot}");
            StatusText = Loc.Format("Status_BackupComplete", backupRoot);
        }
        catch (Exception ex)
        {
            AppLog.Error("Backup failed", ex);
            StatusText = Loc.Format("Status_BackupError", ex.Message);
        }
    }

    private static async Task RunProviderBackupAsync(string backupRoot, IGameDataProvider provider)
    {
        var (convRoot, stRoot) = provider.GetBackupRoots();
        await BackupService.BackupAsync(convRoot, Path.Combine(backupRoot, "conversations"), default);
        await BackupService.BackupAsync(stRoot,   Path.Combine(backupRoot, "stringtables"),  default);
    }

    // ── Restore backup ────────────────────────────────────────────────────
    [RelayCommand]
    private async Task RestoreBackup()
    {
        var backupPick = AppSettings.GetBackupPath(_currentGameDirectory);
        if (backupPick is null)
        {
            StatusText = Loc.Get("Status_NoBackupFound");
            return;
        }

        // Find the most recent timestamped subfolder
        var subdirs = Directory.GetDirectories(backupPick)
            .OrderByDescending(d => d)
            .ToList();
        if (subdirs.Count == 0) { StatusText = Loc.Get("Status_NoBackupFound"); return; }

        var backupRoot = subdirs[0];
        StatusText     = Loc.Get("Status_RestoreInProgress");
        try
        {
            var (convRoot, stRoot) = _provider!.GetBackupRoots();
            await BackupService.RestoreAsync(
                Path.Combine(backupRoot, "conversations"), convRoot, default);
            await BackupService.RestoreAsync(
                Path.Combine(backupRoot, "stringtables"),  stRoot,  default);
            AppLog.Info("Backup restored");
            StatusText = Loc.Get("Status_RestoreComplete");
            if (_currentFile is not null)
                LoadConversationFile(_currentFile);
        }
        catch (Exception ex)
        {
            AppLog.Error("Restore failed", ex);
            StatusText = Loc.Format("Status_RestoreError", ex.Message);
        }
    }

    // ── Save — delegates to SaveProject (Ctrl+S) ─────────────────────────
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_project is null)
        {
            StatusText = Loc.Get("Status_NoProjectOpen");
            return;
        }
        SaveProject();
    }

    private bool CanSave() => _provider is not null && _currentFile is not null && IsModified;

    // ── Events (listened to by the View) ─────────────────────────────────
    public event Action? TestModeEntered;
    public event Action? TestModeExited;
    public event Action? ConversationSaved;

    // ── Test Patch (applies every patch in the project) ───────────────────
    [RelayCommand(CanExecute = nameof(CanTestPatch))]
    private async Task TestPatch() => await DoTestPatch(ignoreConflicts: false);

    private async Task DoTestPatch(bool ignoreConflicts)
    {
        if (_provider is null || _project is null) return;

        if (_project.Patches.Count == 0)
        {
            StatusText = Loc.Get("Status_ProjectNoPatch");
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var restoreEntries = new List<PendingRestoreEntry>();

        _createdConversationPaths.Clear();

        try
        {
            foreach (var (convName, patch) in _project.Patches)
            {
                // For new (not-yet-on-disk) conversations, skip the backup step
                var file = _provider.FindConversation(convName)
                        ?? (_project.IsNewConversation(convName)
                               ? _provider.BuildNewConversationFile(convName)
                               : null);

                if (file is null)
                {
                    AppLog.Warn($"Conversation not found for patch: {convName}");
                    continue;
                }

                if (File.Exists(file.ConversationPath))
                {
                    var origConv   = file.ConversationPath;
                    var origSt     = _provider.GetStringTablePath(file);
                    var backupConv = Path.Combine(tempDir, convName + ".conversation.bak");
                    var backupSt   = Path.Combine(tempDir, convName + ".stringtable.bak");

                    File.Copy(origConv, backupConv);
                    if (File.Exists(origSt)) File.Copy(origSt, backupSt);
                    restoreEntries.Add(new PendingRestoreEntry(backupConv, backupSt, origConv, origSt));
                }
                // else: new conversation — nothing to back up
            }

            // Persist restore info before writing game files (crash safety)
            AppSettings.SetPendingRestores(restoreEntries);

            foreach (var (convName, patch) in _project.Patches)
            {
                var file = _provider.FindConversation(convName)
                        ?? (_project.IsNewConversation(convName)
                               ? _provider.BuildNewConversationFile(convName)
                               : null);
                if (file is null) continue;

                // Create blank template if file doesn't exist yet
                if (!File.Exists(file.ConversationPath))
                {
                    _provider.InitializeConversationFile(file);
                    _createdConversationPaths.Add(file.ConversationPath);
                }

                var conversation = _provider.LoadConversation(file);
                var baseSnap     = ConversationSnapshotBuilder.Build(conversation);
                var merged       = PatchApplier.Apply(baseSnap, patch, ignoreConflicts);
                _provider.SaveConversation(file, merged);
            }

            AppLog.Info($"Test: applied {_project.Patches.Count} patch(es) from project '{_project.Name}'");
            TestModeEntered?.Invoke();
        }
        catch (PatchConflictException ex)
        {
            AppLog.Error($"Patch conflict testing project '{_project.Name}'", ex);

            if (!ignoreConflicts && RequestConflictResolution is not null)
            {
                // Restore partial writes before asking — keeps game files clean while user decides
                RestoreFilesFromBackup(restoreEntries);
                AppSettings.ClearPendingRestores();

                var force = await RequestConflictResolution(ex);
                if (force)
                    await DoTestPatch(ignoreConflicts: true);
                else
                    StatusText = Loc.Format("Status_PatchConflictCancelled", ex.NodeId, ex.FieldName);
            }
            else
            {
                AppSettings.ClearPendingRestores();
                StatusText = Loc.Format("Status_PatchConflict",
                    ex.NodeId, ex.FieldName, ex.ExpectedFrom, ex.ActualValue);
            }
        }
        catch (Exception ex)
        {
            AppSettings.ClearPendingRestores();
            AppLog.Error($"Failed to test project '{_project?.Name}'", ex);
            StatusText = Loc.Format("Status_TestApplyError", _project!.Name, ex.Message);
        }
    }

    private static void RestoreFilesFromBackup(IReadOnlyList<PendingRestoreEntry> entries)
    {
        foreach (var r in entries)
        {
            if (File.Exists(r.BackupConvPath))
                File.Copy(r.BackupConvPath, r.OriginalConvPath, overwrite: true);
            if (File.Exists(r.BackupStPath))
                File.Copy(r.BackupStPath, r.OriginalStPath, overwrite: true);
        }
    }

    private bool CanTestPatch() => _provider is not null && _project is not null;

    // ── Restore Conversations (all entries in project) ────────────────────
    [RelayCommand]
    private void RestoreConversation()
    {
        var entries = AppSettings.GetPendingRestores();
        if (entries is null || entries.Count == 0) return;
        try
        {
            var tempDirsToDelete = new HashSet<string>();
            foreach (var r in entries)
            {
                File.Copy(r.BackupConvPath, r.OriginalConvPath, overwrite: true);
                if (File.Exists(r.BackupStPath))
                    File.Copy(r.BackupStPath, r.OriginalStPath, overwrite: true);
                tempDirsToDelete.Add(Path.GetDirectoryName(r.BackupConvPath)!);
            }

            foreach (var dir in tempDirsToDelete)
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex) { AppLog.Warn($"Could not delete temp backup folder: {ex.Message}"); }

            // Delete any conversation files that were created from scratch
            foreach (var created in _createdConversationPaths)
            {
                try { if (File.Exists(created)) File.Delete(created); }
                catch (Exception ex) { AppLog.Warn($"Could not delete created conversation file: {ex.Message}"); }
            }
            _createdConversationPaths.Clear();

            AppSettings.ClearPendingRestores();
            AppLog.Info($"Restored {entries.Count} conversation(s)");
            StatusText = Loc.Get("Status_RestoreComplete2");

            if (_currentFile is not null)
            {
                // For new conversations, reload as empty canvas (file no longer exists)
                if (_project?.IsNewConversation(_currentFile.Name) == true
                    && !File.Exists(_currentFile.ConversationPath))
                    LoadNewConversation(_currentFile);
                else
                    LoadConversationFile(_currentFile);
            }
            TestModeExited?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to restore conversations", ex);
            StatusText = Loc.Format("Status_SaveError", _project?.Name ?? "?", ex.Message);
        }
    }

    // ── Conversation selection ────────────────────────────────────────────
    private void OnConversationSelected(ConversationFile file)
    {
        if (_provider is null) return;

        if (IsModified && _currentFile is not null)
        {
            _pendingFile = file;
            UnsavedChangesRequested?.Invoke();
            return;
        }

        if (_project?.IsNewConversation(file.Name) == true && !File.Exists(file.ConversationPath))
            LoadNewConversation(file);
        else
            LoadConversationFile(file);
    }

    private void LoadConversationFile(ConversationFile file)
    {
        if (_provider is null) return;
        try
        {
            _currentFile = file;
            var conversation = _provider.LoadConversation(file);
            Canvas.Load(conversation);

            // Restore saved layout if the project has positions for this conversation.
            // Runs after AutoLayout so it always wins over the algorithmic positions.
            var savedLayout = _project?.GetLayout(file.Name);
            if (savedLayout is not null)
                Canvas.RestoreLayout(savedLayout);

            Detail.Clear();
            IsModified = false;
            CurrentConversationName = file.Name;
            if (!IsBrowserPinned) IsBrowserExpanded = false;
            if (_project is null)
                StatusText = Loc.Get("Status_NoProjectReadOnly");
            else
                StatusText = Loc.Format("Status_ConversationLoaded", file.Name, conversation.Nodes.Count);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load conversation '{file.Name}'", ex);
            StatusText = Loc.Format("Status_LoadError", file.Name, ex.Message);
        }
    }
}
