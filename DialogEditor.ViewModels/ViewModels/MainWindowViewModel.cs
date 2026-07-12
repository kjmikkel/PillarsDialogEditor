using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Backup;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Export;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Import;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Changelog;
using DialogEditor.Patch.Diff;
using DialogEditor.Patch.GitConflict;
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
    public GuidedTourViewModel   Tour    { get; } = new();

    // ── Active project ────────────────────────────────────────────────────
    private DialogProject? _project;
    private string?        _projectPath;

    // Set when a conflicted project is detected on startup but resolution is deferred
    // behind a "Resolve…" action rather than shown immediately.
    private string? _pendingConflictPath;

    // ── Git attribution (read-only "last edited by") ──────────────────────
    /// Set by the host (View): loads per-node blame for a project file. Kept off the VM
    /// so the VM stays free of the concrete git runner. Built lazily and cached per path.
    public Func<string, IReadOnlyList<NodeBlame>>? AttributionLoader { get; set; }
    private IReadOnlyDictionary<(string Conv, int NodeId), NodeBlame> _attribution
        = new Dictionary<(string, int), NodeBlame>();
    private string? _attributionPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _currentProjectName;

    private IGameDataProvider? _provider;
    private ConversationFile?  _currentFile;

    public IGameDataProvider? Provider    => _provider;
    public string?            ProjectPath => _projectPath;
    private string             _currentGameDirectory = string.Empty;
    private string             _activeGameId         = string.Empty;

    // Paths of conversation files created during the current test session.
    // On restore, these are deleted (there was no original to restore to).
    private readonly List<string> _createdConversationPaths = [];

    /// Set by the UI layer to provide a name-input dialog for new conversations.
    public Func<Task<string?>>? RequestConversationName { get; set; }

    /// Set by the UI layer to ask for a conversation name pre-filled with a suggestion.
    /// Returns the confirmed name, or null if cancelled.
    public Func<string, Task<string?>>? RequestConversationNameWithSuggestion { get; set; }

    /// Set by the UI layer to surface a patch conflict and ask whether to force-apply.
    /// Returns true if the user chooses Force Apply, false to cancel.
    public Func<PatchConflictException, Task<bool>>? RequestConflictResolution { get; set; }

    /// Set by the UI: asks the user to save the current copy before bringing in
    /// changes. Returns true to proceed (after saving), false to abort.
    public Func<Task<bool>>? ConfirmSaveBeforeApply { get; set; }

    private DialogProject? _preApplyProject;

    /// Set by the UI layer to present git merge conflicts for resolution.
    /// Returns the merged project to load, or null if the user cancelled.
    public Func<GitConflictResolutionViewModel, Task<DialogProject?>>? ShowGitConflictResolution { get; set; }

    /// Set by the UI layer to show a language-code input dialog.
    /// Takes (title, defaultValue) and returns the entered language code, or null if cancelled.
    public Func<string, string?, Task<string?>>? RequestLanguageCode { get; set; }

    /// Set by the UI layer to show an informational dialog listing import warnings.
    /// Awaited before the imported conversation is added to the project.
    public Func<IReadOnlyList<ImportWarning>, Task>? ShowImportWarnings { get; set; }

    /// Set by the UI layer to open the Export Conversations window.
    public Action<ExportConversationsViewModel>? ShowExportConversations { get; set; }

    /// Set by the UI layer to surface a caught operation failure (save, open,
    /// import, …) in the exception report window — status-bar text alone is too
    /// easy to miss for a failed operation.
    public Action<Exception>? ReportError { get; set; }

    /// Conditions from the catalogue filtered to the currently loaded game.
    public IReadOnlyList<ConditionEntry> ActiveConditions
        => string.IsNullOrEmpty(_activeGameId)
            ? []
            : ConditionCatalogue.Instance.ForGame(_activeGameId);

    [ObservableProperty] private string              _statusText          = Loc.Get("Status_OpenFolder");
    [ObservableProperty] private string              _focusHintText       = string.Empty;
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

    // ── Status bar shows the focused control's hint when present ──────────
    /// What the status bar TextBlock actually displays: the focused control's
    /// AutomationProperties.HelpText (set by MainWindow's GotFocus handler) when
    /// present, otherwise the last operation's StatusText.
    public string DisplayStatusText =>
        string.IsNullOrEmpty(FocusHintText) ? StatusText : FocusHintText;

    // ── Unsaved-changes navigation guard ──────────────────────────────────
    private ConversationFile?              _pendingFile;
    private Action?                        _pendingAction;   // for close/project-switch continuations
    private TaskCompletionSource<bool>?    _unsavedDecision;
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

    /// Awaitable form of the unsaved-edits guard, for flows that must continue on the
    /// same call stack (branch switching). Returns true to proceed (clean, or after
    /// Save/Discard), false if the user Cancelled. Reuses the existing
    /// UnsavedChangesRequested dialog plumbing.
    public Task<bool> EnsureNoUnsavedEditsAsync()
    {
        if (!(IsModified && CurrentConversationName is not null))
            return Task.FromResult(true);

        // Defensive: resolve any stale pending decision and drop a stale pending-file
        // load so this flow never inherits another guard's continuation (the UI is
        // modal so this should not happen, but the shared state makes it worth guarding).
        _unsavedDecision?.TrySetResult(false);
        _pendingFile = null;

        _unsavedDecision = new TaskCompletionSource<bool>();
        _pendingAction = () => { _unsavedDecision?.TrySetResult(true); _unsavedDecision = null; };
        UnsavedChangesRequested?.Invoke();
        return _unsavedDecision.Task;
    }

    public void SaveAndProceed()
    {
        SaveCommand.Execute(null);
        Proceed();
    }

    public void DiscardAndProceed()
    {
        // The user consciously discarded these edits — the next launch must not
        // resurrect them from the autosave sidecar (spec 2026-07-12 §3).
        if (_projectPath is not null) AutosaveRecovery.TryDelete(_projectPath);
        Proceed();
    }

    public void CancelPendingNavigation()
    {
        _pendingFile   = null;
        _pendingAction = null;
        _unsavedDecision?.TrySetResult(false);
        _unsavedDecision = null;
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

    /// The active game id ("poe1"/"poe2"/""), for consumers that validate against
    /// the tag vocabulary (Flow Analytics token validation).
    public string ActiveGameId => _activeGameId;

    /// The open conversation's saved translations (language → per-node text), or
    /// empty when no conversation/patch is loaded. Used by Flow Analytics to
    /// validate translation text for the open conversation.
    public IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> CurrentConversationTranslations
        => _project is not null && _currentFile is not null &&
           _project.Patches.TryGetValue(_currentFile.Name, out var patch)
            ? patch.Translations
            : new Dictionary<string, IReadOnlyList<NodeTranslation>>();

    /// True when a PoE2 game folder is loaded and a conversation with nodes is open.
    /// Guards the Validate VO menu item and CreateVoValidationViewModel().
    public bool CanValidateVO =>
        !string.IsNullOrEmpty(_currentGameDirectory)
        && string.Equals(_activeGameId, "poe2", StringComparison.OrdinalIgnoreCase)
        && Canvas.Nodes.Count > 0;

    /// True when a saved project is open and a PoE2 game folder is loaded.
    /// Guards the project-wide "Batch import VO (all conversations)…" menu item.
    /// _projectPath is required because row destinations live in _vo/ next to
    /// the project file — an unsaved new project cannot batch-import.
    public bool CanBatchImportVoAll =>
        _project is not null
        && _provider is not null
        && _projectPath is not null
        && !string.IsNullOrEmpty(_currentGameDirectory)
        && string.Equals(_activeGameId, "poe2", StringComparison.OrdinalIgnoreCase);

    /// True when a saved project is open — gates the "Export Mod Bundle…" menu
    /// item. The pack includes vo/ exactly when _vo/ exists (see VoPackExporter),
    /// so export is meaningful for any saved project, voiced or text-only.
    public bool CanExportModBundle => ProjectPath is not null;

    private void SetProject(DialogProject? project)
    {
        var prevNew  = _project?.NewConversations;
        var nextNew  = project?.NewConversations;
        _project = project;
        Canvas.IsEditable = project is not null;
        OnPropertyChanged(nameof(IsProjectOpen));
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        CloseProjectCommand.NotifyCanExecuteChanged();
        NewConversationCommand.NotifyCanExecuteChanged();
        ImportConversationCommand.NotifyCanExecuteChanged();
        ExportConversationsCommand.NotifyCanExecuteChanged();
        MergeProjectsCommand.NotifyCanExecuteChanged();
        ExportForTranslationCommand.NotifyCanExecuteChanged();
        ImportTranslationCommand.NotifyCanExecuteChanged();
        BatchImportVoAllCommand.NotifyCanExecuteChanged();
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

    partial void OnStatusTextChanged(string value)
        => OnPropertyChanged(nameof(DisplayStatusText));

    partial void OnFocusHintTextChanged(string value)
        => OnPropertyChanged(nameof(DisplayStatusText));

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

        Detail.AttributionLookup = LookupAttribution;

        // Feeds the detail-pane's alias shared-count: the open project's patched
        // ExternalVO values (added + modified nodes) with the live canvas nodes
        // winning for the currently-open conversation, so an in-progress edit is
        // reflected immediately rather than only after the next diff/save.
        Detail.ProjectAliasOverlay = () => VoAliasOverlayBuilder.Build(
            _project?.Patches,
            CurrentConversationName,
            Canvas.Nodes.Select(n => (n.NodeId, n.ExternalVO)));

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
                SaveProjectAsCommand.NotifyCanExecuteChanged();
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

        Canvas.ConnectModeChanged += (_, e) =>
        {
            StatusText = e.Change switch
            {
                ConnectModeChange.Started   => Loc.Format("Status_ConnectMode_Started",   e.Source.NodeId),
                ConnectModeChange.Connected => Loc.Format("Status_ConnectMode_Connected", e.Source.NodeId, e.Target!.NodeId),
                ConnectModeChange.Cancelled => Loc.Get("Status_ConnectMode_Cancelled"),
                _ => StatusText,
            };
        };

        var last = AppSettings.LastGameDirectory;
        if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
            LoadDirectory(last);

        // Crash recovery: if a test was in progress when the app closed, re-enter test mode
        if (AppSettings.GetPendingRestores() is not null)
            TestModeEntered?.Invoke();

        // Re-opening the last project is deferred to ReopenLastProjectOnStartup(),
        // invoked by the View once the window is shown and callbacks are wired — so a
        // conflicted project can offer resolution rather than only showing guidance.
    }

    /// Re-opens the last project, if any. Must be called by the View after the main
    /// window is shown and ShowGitConflictResolution is wired (see the ctor note),
    /// otherwise a conflicted project would silently fall back to a guidance message.
    public void ReopenLastProjectOnStartup()
    {
        var lastProject = AppSettings.LastProjectPath;
        if (!string.IsNullOrEmpty(lastProject) && File.Exists(lastProject))
            _ = LoadProjectAsync(lastProject, offerDeferred: true);
    }

    // ── Deferred git conflict resolution (startup) ────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveConflictsCommand))]
    private bool _hasPendingConflictResolution;

    [RelayCommand(CanExecute = nameof(HasPendingConflictResolution))]
    private async Task ResolveConflicts()
    {
        var path = _pendingConflictPath;
        if (path is null || !File.Exists(path))
        {
            ClearPendingConflict();
            return;
        }
        await LoadProjectAsync(path, offerDeferred: false);
    }

    private void ClearPendingConflict()
    {
        _pendingConflictPath         = null;
        HasPendingConflictResolution = false;
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
    public SettingsViewModel CreateSettingsViewModel(IFontScaleApplier? fontScaleApplier = null)
        => new(_currentGameDirectory, _folderPicker, fontScaleApplier,
               SpellStoreFactory?.Invoke());

    /// Returns a ready-to-run VoValidationViewModel for the current conversation,
    /// or null if CanValidateVO is false (e.g. wrong game, no nodes loaded).
    public VoValidationViewModel? CreateVoValidationViewModel()
    {
        if (!CanValidateVO) return null;
        var snapshot = Canvas.BuildSnapshot();
        var vm = new VoValidationViewModel(
            snapshot.Nodes, Canvas.ConversationName,
            _currentGameDirectory, _activeGameId, _projectPath);

        // Orphan section only makes sense with a saved project (the _vo/ folder
        // lives next to the project file).
        if (_project is not null && _provider is not null && _projectPath is not null)
        {
            var project     = _project;
            var provider    = _provider;
            var projectPath = _projectPath;
            var convName    = Canvas.ConversationName;
            vm.VoRootPath    = Path.Combine(Path.GetDirectoryName(projectPath)!, "_vo");
            vm.OrphanScanner = _ => VoOrphanScanner.FindOrphans(
                project, provider, projectPath, convName, snapshot);
        }
        return vm;
    }

    /// Seam for the Validate Text Tags dirty guard (three-way consent). Null in unit
    /// tests that don't exercise the dialog — a dirty project with no seam wired
    /// treats the request as cancelled rather than silently scanning stale state.
    /// The View wires SaveBeforeScanDialog.
    public Func<Task<ScanDirtyChoice>>? ConfirmScanWithUnsavedChanges { get; set; }

    /// Supplies the spelling store for the Validate Text sweep. Null (the unit-test
    /// default) disables spelling in the sweep; the View wires it to
    /// SpellDictionaryStore.Default at startup.
    public Func<SpellDictionaryStore?>? SpellStoreFactory { get; set; }

    /// Test ▸ Validate Text…: returns a ready view-model over the SAVED project,
    /// or null when no project is open / the user cancels the dirty guard.
    public async Task<TextTagValidationViewModel?> RequestTextTagValidationAsync()
    {
        if (_project is null) return null;

        if (IsModified)
        {
            var choice = ConfirmScanWithUnsavedChanges is null
                ? ScanDirtyChoice.Cancel
                : await ConfirmScanWithUnsavedChanges();
            if (choice == ScanDirtyChoice.Cancel) return null;
            if (choice == ScanDirtyChoice.SaveAndScan) SaveProject();
        }

        var store = SpellStoreFactory?.Invoke();
        var spell = store is null ? null : new SpellCheckService(store);

        // The closure reads the current fields, so Refresh in the open window picks
        // up saves made later in the session.
        return new TextTagValidationViewModel(
            () => _project is null
                ? []
                : ProjectTextTagScanner.Scan(
                    _project, _activeGameId, _provider?.Language ?? "", spell: spell),
            addWord: store is null ? null : store.AddWord);
    }

    /// Opens the batch VO import dialog in multi-conversation mode.
    /// Wired by MainWindow.axaml.cs; null in unit tests that don't need the dialog.
    public Func<IReadOnlyList<BatchVoRowViewModel>, Task>? ShowBatchVoImportAll { get; set; }

    [RelayCommand(CanExecute = nameof(CanBatchImportVoAll))]
    private async Task BatchImportVoAll()
    {
        if (_project is null || _provider is null || _projectPath is null) return;
        // Capture locals: the scan runs on a worker thread and the fields are mutable.
        var project     = _project;
        var provider    = _provider;
        var projectPath = _projectPath;
        var gameRoot    = _currentGameDirectory;
        var gameId      = _activeGameId;
        var openName    = Canvas.Nodes.Count > 0 ? Canvas.ConversationName : null;
        var snapshot    = openName is not null ? Canvas.BuildSnapshot() : null;

        try
        {
            var rows = await Task.Run(() => ProjectVoRowScanner.BuildRows(
                project, provider, projectPath, gameRoot, gameId, openName, snapshot));

            if (rows.Count == 0)
            {
                StatusText = Loc.Get("Status_BatchImportVoAllEmpty");
                return;
            }

            if (ShowBatchVoImportAll is not null)
                await ShowBatchVoImportAll(rows);
            Detail.Refresh();   // the selected node's VO status row may have flipped to ✓
        }
        catch (OperationCanceledException) { /* deliberate cancellation — swallow silently */ }
        catch (Exception ex)
        {
            AppLog.Error("Project-wide batch VO scan failed", ex);
            StatusText = Loc.Get("Status_BatchImportVoAllFailed");
            ReportError?.Invoke(ex);
        }
    }

    // ── Project — New / Open / Save ───────────────────────────────────────

    /// MRU list of recently opened/created/saved-as projects (newest first) for the
    /// File ▸ Recent Projects submenu. Reads through to AppSettings; the submenu is
    /// rebuilt on open, and this raises change notification after each mutation.
    public IReadOnlyList<string> RecentProjects => AppSettings.RecentProjects;

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
        Detail.ProjectPath  = _projectPath;
        Canvas.ProjectPath  = _projectPath;
        OnPropertyChanged(nameof(CanExportModBundle));
        BatchImportVoAllCommand.NotifyCanExecuteChanged();   // gate depends on _projectPath
        DialogProjectSerializer.SaveToFile(path, _project!);
        AppSettings.LastProjectPath = path;
        AppSettings.AddRecentProject(path);
        OnPropertyChanged(nameof(RecentProjects));
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

    /// View-supplied confirm for a recent entry whose file is missing: returns true to
    /// remove it from the list, false to keep it. Null in headless/tests.
    public Func<string, Task<bool>>? ConfirmRemoveMissingProject { get; set; }

    [RelayCommand]
    private void OpenRecentProject(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path))
        {
            _ = HandleMissingRecentProjectAsync(path);
            return;
        }
        GuardDirtyThen(() => LoadProject(path));
    }

    private async Task HandleMissingRecentProjectAsync(string path)
    {
        AppLog.Warn($"Recent project not found: {path}");
        StatusText = Loc.Format("Status_RecentProjectMissing", path);
        if (ConfirmRemoveMissingProject is null) return;
        if (await ConfirmRemoveMissingProject(path))
        {
            AppSettings.RemoveRecentProject(path);
            OnPropertyChanged(nameof(RecentProjects));
        }
    }

    [RelayCommand]
    private void ClearRecentProjects()
    {
        AppSettings.ClearRecentProjects();
        OnPropertyChanged(nameof(RecentProjects));
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void CloseProject()
        => GuardDirtyThen(DoCloseProject);

    private void DoCloseProject()
    {
        var name = CurrentProjectName ?? "?";
        AppLog.Info($"Closed project: {_projectPath}");
        CloseProjectCore(Loc.Format("Status_ProjectClosed", name));
        // User-intent extras the branch-switch teardown must not do: the canvas may
        // show patched content that exists nowhere after close, and a deliberate
        // close should stick across restarts (no auto-reopen of this project).
        Canvas.Clear();
        Detail.Clear();
        CurrentConversationName = null;
        _currentFile = null;
        OnPropertyChanged(nameof(CanValidateVO));
        AppSettings.LastProjectPath = null;
    }

    // Explicit opens (File > Open) resolve conflicts immediately; startup re-open
    // defers to a "Resolve…" prompt (offerDeferred: true) so we don't slam a modal
    // over a freshly shown window.
    private void LoadProject(string path) => _ = LoadProjectAsync(path, offerDeferred: false);

    /// Re-reads the open project after the working tree changed underneath it (branch
    /// switch). If the file no longer exists on the new branch, closes the project.
    /// Invalidates the HEAD-based attribution cache so "last edited" recomputes.
    public void ReloadCurrentProjectFromDisk()
    {
        var path = _projectPath;
        if (path is null) return;

        if (!File.Exists(path))
        {
            AppLog.Info($"Project file not present on current branch: {path}");
            CloseProjectCore(Loc.Format("Status_ProjectNotOnBranch", path));
            return;
        }

        _attributionPath = null;       // HEAD moved → stale blame
        _ = LoadProjectAsync(path, offerDeferred: false);
    }

    /// Shared project-state teardown, used by the Close Project command and by
    /// ReloadCurrentProjectFromDisk when the file vanished on a branch switch.
    /// Deliberately does NOT clear AppSettings.LastProjectPath or the canvas:
    /// on a branch switch the file may reappear on switching back, and that path
    /// keeps the canvas as-is. The user command layers those two on top.
    private void CloseProjectCore(string statusText)
    {
        SetProject(null);
        _projectPath = null;
        Detail.ProjectPath  = null;
        Canvas.ProjectPath  = null;
        OnPropertyChanged(nameof(CanExportModBundle));
        BatchImportVoAllCommand.NotifyCanExecuteChanged();   // gate depends on _projectPath
        CurrentProjectName = null;
        IsModified = false;        // nothing open → not dirty
        _attributionPath = null;   // force attribution rebuild next time
        StatusText = statusText;
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
    }

    // Per-node attribution for the node detail panel. Built lazily on first lookup after
    // the project path changes (blame is HEAD-based, so it's stable for the open project).
    private NodeBlame? LookupAttribution(string conversationName, int nodeId)
    {
        if (_projectPath != _attributionPath)
        {
            _attributionPath = _projectPath;
            _attribution = BuildAttribution(_projectPath);
        }
        return _attribution.GetValueOrDefault((conversationName, nodeId));
    }

    private IReadOnlyDictionary<(string, int), NodeBlame> BuildAttribution(string? path)
    {
        if (path is null || AttributionLoader is null)
            return new Dictionary<(string, int), NodeBlame>();
        try
        {
            return AttributionLoader(path).ToDictionary(b => (b.ConversationName, b.NodeId));
        }
        catch (DiffException ex)
        {
            AppLog.Warn($"MainWindowViewModel: could not load attribution: {ex.Message}");
            return new Dictionary<(string, int), NodeBlame>();
        }
    }

    /// Seam for the autosave restore offer (argument: the sidecar's local timestamp;
    /// true = restore). Null in unit tests that don't exercise it — a newer sidecar
    /// is then left untouched (never destroy recovery data because no dialog was
    /// wired) and the saved file loads normally. The View wires AutosaveRestoreDialog.
    public Func<DateTime, Task<bool>>? ConfirmRestoreAutosave { get; set; }

    private async Task LoadProjectAsync(string path, bool offerDeferred)
    {
        // Crash recovery (spec 2026-07-12 §4): a sidecar newer than the project file
        // holds work lost to a crash/kill — offer to restore it before loading.
        var recovery = AutosaveRecovery.Check(path);
        if (recovery.State == AutosaveState.Stale)
        {
            AutosaveRecovery.TryDelete(path); // save happened through another route
        }
        else if (recovery.State == AutosaveState.Newer && ConfirmRestoreAutosave is not null)
        {
            var restore = await ConfirmRestoreAutosave(recovery.SidecarTimeUtc!.Value.ToLocalTime());
            if (restore)
            {
                try
                {
                    var recovered = DialogProjectSerializer.LoadFromFile(recovery.SidecarPath);
                    FinishLoad(recovered, path);
                    IsModified = true;   // recovered state is unsaved until an explicit save
                    StatusText = Loc.Get("Status_AutosaveRestored");
                    return;              // sidecar is KEPT until that save (double-crash protection)
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Autosave restore failed for '{path}': {ex.Message} — loading saved file");
                    AutosaveRecovery.TryDelete(path); // corrupt sidecar: don't re-offer forever
                }
            }
            else
            {
                AutosaveRecovery.TryDelete(path);    // user declined — respect it
            }
        }

        try
        {
            var text = File.ReadAllText(path);

            if (GitConflictMarkers.HasMarkers(text))
            {
                await LoadConflictedProjectAsync(path, text, offerDeferred);
                return;
            }

            var loaded = DialogProjectSerializer.Deserialize(text);
            FinishLoad(loaded, path);
            ClearPendingConflict();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to open project '{path}'", ex);
            StatusText = Loc.Format("Status_ProjectOpenError", path, ex.Message);
            ReportError?.Invoke(ex);
        }
    }

    private async Task LoadConflictedProjectAsync(string path, string text, bool offerDeferred)
    {
        var (mineText, theirsText) = GitConflictMarkers.SplitSides(text);

        DialogProject mine, theirs;
        List<MergeConflict> conflicts;
        try
        {
            mine      = DialogProjectSerializer.Deserialize(mineText);
            theirs    = DialogProjectSerializer.Deserialize(theirsText);
            conflicts = GitMergeAnalyzer.Analyze(mine, theirs);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Git-conflicted project '{path}' has unparseable sides: {ex.Message}");
            StatusText = Loc.Format("Status_ProjectGitConflictUnparseable", path);
            return;
        }

        if (conflicts.Count == 0)            // markers but sides agree — just load mine
        {
            FinishLoad(mine, path);
            ClearPendingConflict();
            return;
        }

        if (ShowGitConflictResolution is null)   // no resolution UI wired — guide the user
        {
            StatusText = Loc.FormatCount("Status_ProjectGitConflictDetected", conflicts.Count, path);
            return;
        }

        if (offerDeferred)   // startup: don't auto-show the modal — offer a Resolve… action
        {
            _pendingConflictPath        = path;
            HasPendingConflictResolution = true;
            StatusText = Loc.FormatCount("Status_ProjectGitConflictPending", conflicts.Count, path);
            return;
        }

        var vm = new GitConflictResolutionViewModel(mine, theirs, conflicts);
        var merged = await ShowGitConflictResolution(vm);
        if (merged is null)
        {
            // Leave any pending prompt intact so the user can retry.
            StatusText = Loc.Get("Status_ProjectGitConflictCancelled");
            return;
        }

        FinishLoad(merged, path);
        IsModified = true;   // open in memory; user Saves to write back to `path`
        ClearPendingConflict();
        SaveCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        StatusText = Loc.Format("Status_ProjectGitConflictResolved", merged.Name);
    }

    private void FinishLoad(DialogProject loaded, string path)
    {
        SetProject(loaded);
        _projectPath = path;
        Detail.ProjectPath  = _projectPath;
        Canvas.ProjectPath  = _projectPath;
        OnPropertyChanged(nameof(CanExportModBundle));
        BatchImportVoAllCommand.NotifyCanExecuteChanged();   // gate depends on _projectPath
        AppSettings.LastProjectPath = path;
        AppSettings.AddRecentProject(path);
        OnPropertyChanged(nameof(RecentProjects));
        CurrentProjectName = loaded.Name;
        AppLog.Info($"Opened project: {path}");
        StatusText = Loc.FormatCount("Status_ProjectOpened", loaded.Patches.Count, loaded.Name);
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
            StatusText = Loc.Format("Status_NewConversationInvalidName", name);
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

    [RelayCommand(CanExecute = nameof(CanCreateConversation))]
    private async Task ImportConversation()
    {
        if (_provider is null || _project is null) return;

        // Build file filter tuples using localizable labels
        var fileTypes = new (string Extension, string Label)[]
        {
            (".csv",  Loc.Get("FileType_CsvDialog")),
            (".json", Loc.Get("FileType_JsonDialog")),
            (".xml",  Loc.Get("FileType_ArticyXml")),
            (".yarn", Loc.Get("FileType_YarnSpinner")),
        };

        var path = await _filePicker.PickOpenFileAsync(
            Loc.Get("Dialog_ImportConversation"),
            fileTypes);
        if (path is null) return;

        var importer = DialogImporterFactory.GetForFile(path);
        if (importer is null)
        {
            StatusText = Loc.Format("Status_ImportConversationUnknownFormat", Path.GetFileName(path));
            return;
        }

        ImportedConversation imported;
        try
        {
            imported = importer.Import(path);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to import conversation from '{path}'", ex);
            StatusText = Loc.Format("Status_ImportConversationError", Path.GetFileName(path), ex.Message);
            ReportError?.Invoke(ex);
            return;
        }

        if (imported.Warnings.Count > 0)
            await (ShowImportWarnings?.Invoke(imported.Warnings) ?? Task.CompletedTask);

        // Ask user to confirm or change the name
        var suggested = imported.SuggestedName;
        var name = RequestConversationNameWithSuggestion is not null
            ? (await RequestConversationNameWithSuggestion(suggested))?.Trim()
            : (await (RequestConversationName?.Invoke() ?? Task.FromResult<string?>(suggested)))?.Trim();

        if (string.IsNullOrEmpty(name)) return;

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusText = Loc.Format("Status_NewConversationInvalidName", name);
            return;
        }

        var alreadyExists = _provider.FindConversation(name) is not null
                         || (_project.NewConversations?.Contains(name) == true);
        if (alreadyExists)
        {
            StatusText = Loc.Format("Status_NewConversationDuplicate", name);
            return;
        }

        // Build the patch from imported data
        var patch = new ConversationPatch(
            name, ConversationPatch.CurrentSchemaVersion,
            imported.Nodes, [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { [_provider.Language] = imported.Texts }
        };

        // Auto-layout: convert NodeEditSnapshot → ConversationNode just for layout
        var layoutNodes = imported.Nodes
            .Select(s => new ConversationNode(
                s.NodeId, s.IsPlayerChoice, s.SpeakerCategory,
                s.SpeakerGuid, s.ListenerGuid,
                s.Links.Select(l => new NodeLink(l.FromNodeId, l.ToNodeId, [], l.RandomWeight, l.QuestionNodeTextDisplay)).ToList(),
                [], [],
                s.DisplayType, s.Persistence, s.ActorDirection,
                s.Comments, s.ExternalVO, s.HasVO, s.HideSpeaker))
            .ToList();

        var layout = new Dictionary<int, LayoutPoint>();
        AutoLayoutService.Apply(layoutNodes, (id, x, y) => layout[id] = new LayoutPoint(x, y));

        SetProject(_project
            .WithNewConversation(name)
            .WithPatch(patch)
            .WithLayout(name, layout));

        var file = _provider.BuildNewConversationFile(name);
        LoadNewConversation(file);

        AppLog.Info($"Imported conversation '{name}' from '{path}' ({imported.Nodes.Count} nodes)");
        if (imported.Warnings.Count > 0)
        {
            var constructs = string.Join(", ", imported.Warnings.Select(w => $"<<{w.Construct}>>"));
            StatusText = Loc.Format("Status_ImportConversationAddedWithWarnings",
                name, imported.Nodes.Count, constructs);
        }
        else
        {
            StatusText = Loc.Format("Status_ImportConversationAdded", name, imported.Nodes.Count);
        }
    }

    private bool CanCreateConversation() => _provider is not null && _project is not null;

    [RelayCommand(CanExecute = nameof(IsProjectLoaded))]
    private void ExportConversations()
    {
        if (_provider is null || _project is null) return;

        var allNames = _provider.EnumerateConversations()
            .Select(f => f.Name)
            .Concat(_project.NewConversations ?? [])
            .OrderBy(n => n)
            .ToList();

        IReadOnlyList<NodeEditSnapshot> FetchNodes(string name)
        {
            if (name == _currentFile?.Name)
                return Canvas.BuildSnapshot().Nodes;
            if (_project.Patches.TryGetValue(name, out var patch))
                return PatchApplier.Apply(new ConversationEditSnapshot([]), patch).Nodes;
            return [];
        }

        var exportVm = new ExportConversationsViewModel(
            allNames,
            _currentFile?.Name,
            FetchNodes,
            _filePicker,
            _folderPicker);

        ShowExportConversations?.Invoke(exportVm);
    }

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
            // Baseline must stay the *empty* snapshot: SaveProject re-diffs baseline →
            // canvas, and F5 applies the result to a blank template. A patched baseline
            // would shrink the saved patch to this session's delta, dropping the nodes
            // created in earlier sessions.
            Canvas.Load(restored, baseSnap);
        }
        else
        {
            Canvas.Load(empty);
        }

        var savedLayout = _project?.GetLayout(file.Name);
        if (savedLayout is not null) Canvas.RestoreLayout(savedLayout);

        var savedAnnotations = _project?.GetAnnotations(file.Name);
        if (savedAnnotations is not null) Canvas.RestoreAnnotations(savedAnnotations);

        Detail.Canvas = Canvas;
        Canvas.Detail = Detail;
        var existingComments2 = _project?.Patches.TryGetValue(file.Name, out var p2) == true
            ? p2.NodeComments
            : new Dictionary<int, string>();
        Canvas.LoadNodeComments(existingComments2);

        Detail.Clear();
        IsModified = false;
        CurrentConversationName = file.Name;
        if (!IsBrowserPinned) IsBrowserExpanded = false;
    }

    /// Periodic autosave (wired to a 60 s DispatcherTimer in MainWindow). Writes a
    /// sidecar next to the project file while there are unsaved changes; never
    /// touches the real file, never clears IsModified, never throws.
    /// Spec: docs/superpowers/specs/2026-07-12-autosave-design.md.
    public void AutosaveTick()
    {
        if (_project is null || _projectPath is null || !IsModified) return;
        try
        {
            FoldCanvasIntoProject();
            var sidecar = AutosaveRecovery.SidecarPath(_projectPath);
            DialogProjectSerializer.SaveToFile(sidecar, _project!);
            AppLog.Info($"Autosaved to {sidecar}");
        }
        catch (Exception ex)
        {
            // Autosave must never interrupt writing — log and carry on.
            AppLog.Warn($"Autosave failed: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private void SaveProject()
    {
        if (_project is null || _projectPath is null) return;
        try
        {
            FoldCanvasIntoProject();
            DialogProjectSerializer.SaveToFile(_projectPath, _project!);
            AutosaveRecovery.TryDelete(_projectPath); // changes are now in the real file
            Canvas.IsModified = false;
            IsModified = false;
            SaveCommand.NotifyCanExecuteChanged();
            SaveProjectCommand.NotifyCanExecuteChanged();
            SaveProjectAsCommand.NotifyCanExecuteChanged();
            AppLog.Info($"Project saved: {_projectPath}");
            StatusText = Loc.Format("Status_ProjectSaved", _project.Name);
            ConversationSaved?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save project", ex);
            StatusText = Loc.Format("Status_SaveError", _project?.Name ?? "?", ex.Message);
            ReportError?.Invoke(ex);
        }
    }

    private bool CanSaveProject() =>
        _project is not null && _projectPath is not null && IsModified;

    // ── Save As — rebind to a new file (spec: 2026-07-05-save-project-as) ──
    [RelayCommand(CanExecute = nameof(CanSaveProjectAs))]
    private async Task SaveProjectAs()
    {
        if (_project is null || _projectPath is null) return;

        var path = await _filePicker.PickSaveFileAsync(
            Loc.Get("Dialog_SaveProjectAs"),
            Path.GetFileNameWithoutExtension(_projectPath),
            ".dialogproject",
            Loc.Get("FileType_DialogProject"));
        if (path is null) return;

        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_projectPath),
                StringComparison.OrdinalIgnoreCase))
        {
            SaveProject();   // same file — a plain save; no rename, no rebind
            return;
        }

        var oldPath = _projectPath;
        var oldName = _project.Name;
        try
        {
            FoldCanvasIntoProject();
            // Rename before writing so the file carries the new name; rebind the
            // path only after the write succeeds so a failed save leaves the
            // editor bound to the original file.
            SetProject(_project! with { Name = Path.GetFileNameWithoutExtension(path) });
            DialogProjectSerializer.SaveToFile(path, _project!);
        }
        catch (Exception ex)
        {
            SetProject(_project! with { Name = oldName });
            AppLog.Error($"Failed to save project as '{path}'", ex);
            StatusText = Loc.Format("Status_SaveError", oldName, ex.Message);
            ReportError?.Invoke(ex);
            return;
        }

        _projectPath        = path;
        Detail.ProjectPath  = path;
        Canvas.ProjectPath  = path;

        // The just-saved state needs no recovery sidecar — under either path.
        AutosaveRecovery.TryDelete(oldPath);
        AutosaveRecovery.TryDelete(path);

        var voCopyError = CopyVoFolder(oldPath, path);
        if (voCopyError is not null)
            ReportError?.Invoke(voCopyError);

        Canvas.IsModified = false;
        IsModified = false;
        SaveCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanExportModBundle));
        BatchImportVoAllCommand.NotifyCanExecuteChanged();
        AppSettings.LastProjectPath = path;
        AppSettings.AddRecentProject(path);
        OnPropertyChanged(nameof(RecentProjects));
        CurrentProjectName = _project!.Name;
        AppLog.Info($"Project saved as: {path}");
        StatusText = voCopyError is null
            ? Loc.Format("Status_ProjectSavedAs", _project!.Name)
            : Loc.Format("Status_SaveAsVoCopyFailed", _project!.Name, voCopyError.Message);
        ConversationSaved?.Invoke();
    }

    private bool CanSaveProjectAs() =>
        _project is not null && _projectPath is not null;

    /// Copies the _vo/ sidecar folder next to the new project file when the
    /// directory changed (same directory → the folder is already adjacent).
    /// Returns null on success or nothing-to-copy, else the exception —
    /// a failed copy must not roll back the already-written project file.
    private static Exception? CopyVoFolder(string oldPath, string newPath)
    {
        try
        {
            var oldDir = Path.GetDirectoryName(oldPath)!;
            var newDir = Path.GetDirectoryName(newPath)!;
            if (string.Equals(Path.GetFullPath(oldDir), Path.GetFullPath(newDir),
                    StringComparison.OrdinalIgnoreCase))
                return null;
            var source = Path.Combine(oldDir, "_vo");
            if (!Directory.Exists(source)) return null;
            CopyDirectoryRecursive(source, Path.Combine(newDir, "_vo"));
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error("Save As: copying the _vo/ sidecar folder failed", ex);
            return ex;
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    /// With a conversation open, folds the canvas edits into its patch on _project.
    /// With no conversation open (e.g. a freshly conflict-merged project), the
    /// in-memory _project is already complete — no-op.
    private void FoldCanvasIntoProject()
    {
        if (_currentFile is null || Canvas.BaseSnapshot is null) return;

        var patch  = DiffEngine.Diff(_currentFile.Name, Canvas.BaseSnapshot, Canvas.BuildSnapshot(), _provider!.Language);
        patch = patch with { NodeComments = Canvas.NodeComments };

        // WithPatch replaces the stored patch wholesale, but the diff only knows
        // the canvas language — carry over imported translations for every other
        // language, or they would be silently erased on each save. The current
        // language always takes the freshly diffed value (including "no entry"
        // when the text was reverted to vanilla).
        if (_project!.Patches.TryGetValue(_currentFile.Name, out var prior)
            && prior.Translations.Count > 0)
        {
            var mergedTranslations =
                new Dictionary<string, IReadOnlyList<NodeTranslation>>(prior.Translations);
            mergedTranslations.Remove(_provider.Language);
            foreach (var (lang, entries) in patch.Translations)
                mergedTranslations[lang] = entries;
            patch = patch with { Translations = mergedTranslations };
        }
        var layout      = Canvas.GetCurrentLayout();
        var annotations = Canvas.GetCurrentAnnotations();
        SetProject(_project!.WithPatch(patch).WithLayout(_currentFile.Name, layout)
            .WithAnnotations(_currentFile.Name, annotations));
    }

    // ── Apply from diff ───────────────────────────────────────────────────

    /// Brings an applied project (from the compare window) into the live editor:
    /// guards on unsaved changes, snapshots the prior state, swaps + saves.
    public async Task ApplyFromDiff(DialogProject applied)
    {
        if (_projectPath is null) return;

        if (IsModified)
        {
            if (ConfirmSaveBeforeApply is null) return;
            var proceed = await ConfirmSaveBeforeApply();
            if (!proceed) return;
            SaveProject();   // flush the user's real open edits first
        }

        _preApplyProject = _project;
        SetProject(applied);
        // Serialize `applied` directly — do NOT call SaveProject() here.
        // SaveProject() re-folds the currently-open canvas back into the project,
        // which would overwrite the just-applied patch with the stale canvas snapshot.
        // Mirror the MergeProjects precedent: write the already-computed project directly.
        try
        {
            DialogProjectSerializer.SaveToFile(_projectPath!, applied);
            Canvas.IsModified = false;
            IsModified = false;
        }
        catch (Exception ex)
        {
            AppLog.Error("ApplyFromDiff: save failed", ex);
            StatusText = Loc.Format("Status_SaveError", applied.Name, ex.Message);
            ReportError?.Invoke(ex);
            return;
        }
        UndoApplyCommand.NotifyCanExecuteChanged();
        StatusText = Loc.Format("Status_BroughtInApplied", applied.Name);
    }

    [RelayCommand(CanExecute = nameof(CanUndoApply))]
    private void UndoApply()
    {
        if (_preApplyProject is null) return;
        var previous = _preApplyProject;
        _preApplyProject = null;
        SetProject(previous);
        // Serialize `previous` directly — do NOT call SaveProject() here.
        // SaveProject() would re-fold the open canvas over the restored project,
        // losing the undo. Mirror the MergeProjects / ApplyFromDiff precedent.
        try
        {
            DialogProjectSerializer.SaveToFile(_projectPath!, previous);
            Canvas.IsModified = false;
            IsModified = false;
        }
        catch (Exception ex)
        {
            AppLog.Error("UndoApply: save failed", ex);
            StatusText = Loc.Format("Status_SaveError", previous.Name, ex.Message);
            ReportError?.Invoke(ex);
            return;
        }
        UndoApplyCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndoApply() => _preApplyProject is not null;

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
            StatusText = Loc.FormatCount("Status_MergeComplete", paths.Count, merged.Name);
        }
        catch (Exception ex)
        {
            AppLog.Error("Merge projects failed", ex);
            StatusText = Loc.Format("Status_MergeError", ex.Message);
            ReportError?.Invoke(ex);
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
        var allTypes = new (string, string)[] { (".csv", "CSV"), (".json", "JSON"), (".xlf", "XLIFF") };
        var ordered = allTypes.OrderByDescending(t => t.Item1 == ext).ToArray();
        var path = await _filePicker.PickSaveFileAsync(
            "Export for Translation",
            "export" + ext,
            ordered);
        if (path is null) return;
        var lang = await (RequestLanguageCode?.Invoke("Source language", _provider?.Language)
                          ?? Task.FromResult<string?>(_provider?.Language));
        if (lang is null) return;
        var count = _project.Patches.Values
            .Where(p => p.Translations.ContainsKey(lang))
            .Sum(p => p.Translations[lang].Count);
        LocalizationExportService.Export(_project, path, fmt, lang);
        StatusText = Loc.Format("Localization_StatusExported", count, Path.GetFileName(path));
    }

    [RelayCommand(CanExecute = nameof(IsProjectLoaded))]
    private async Task ImportTranslation()
    {
        if (_project is null) return;
        var path = await _filePicker.PickOpenFileAsync(
            "Import Translation",
            new[] { (".csv", "CSV"), (".json", "JSON"), (".xlf", "XLIFF") });
        if (path is null) return;
        var fmt          = DetectFormat(path);
        var suggestedLang = LocalizationImportService.DetectLanguage(path, fmt,
            ex => AppLog.Warn($"Language auto-detect failed for '{path}': {ex.Message}"));
        var lang = await (RequestLanguageCode?.Invoke("Target language", suggestedLang)
                          ?? Task.FromResult<string?>(suggestedLang));
        if (lang is null) return;
        _project  = LocalizationImportService.Import(_project, path, fmt, lang);
        IsModified = true;
        var count = _project.Patches.Values
            .Sum(p => p.Translations.TryGetValue(lang, out var t) ? t.Count : 0);
        StatusText = Loc.Format("Localization_StatusImported", count, lang);
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

            // Populate GameDataNameService for all lookup kinds.
            // Speaker kind is populated from the already-loaded speaker data to avoid re-parsing.
            GameDataNameService.Clear();
            var speakerEntries = SpeakerNameService.All
                .Select(s => new NamedEntry($"{s.Name} — {s.Guid}", s.Guid))
                .Where(ne => !string.IsNullOrWhiteSpace(ne.DisplayName))
                .ToList();
            GameDataNameService.Register("Speaker", speakerEntries);

            foreach (var (kind, entries) in provider.LoadGameDataNames())
            {
                var namedEntries = entries
                    .Select(e => string.IsNullOrEmpty(e.Id)
                        ? new NamedEntry(e.Name, e.Name)
                        : new NamedEntry($"{e.Name} — {e.Id}", e.Id))
                    .Where(ne => !string.IsNullOrWhiteSpace(ne.DisplayName))
                    .OrderBy(ne => ne.DisplayName)
                    .ToList();
                GameDataNameService.Register(kind, namedEntries);
            }

            _activeGameId = provider.GameId;
            Detail.ActiveGameId = provider.GameId;
            Detail.ActiveLanguage = provider.Language;
            Detail.GameRoot = path;
            ChatterPrefixService.Register(provider.LoadChatterPrefixes());

            // Reverse alias index for the detail pane's "also used by N nodes"
            // line. Disk-only and PoE2-only; a few seconds in the background.
            VoAliasIndexService.Clear();
            if (string.Equals(provider.GameId, "poe2", StringComparison.OrdinalIgnoreCase))
            {
                var aliasScanRoot = path;
                _ = Task.Run(() =>
                {
                    try { VoAliasIndexService.Rebuild(aliasScanRoot); }
                    catch (Exception ex)
                    {
                        AppLog.Error($"VO alias index rebuild failed: {ex.Message}");
                        ReportError?.Invoke(ex);
                    }
                });
            }
            OnPropertyChanged(nameof(CanValidateVO));
            BatchImportVoAllCommand.NotifyCanExecuteChanged();   // gate depends on game folder + id
            AvailableLanguages = provider.AvailableLanguages;
            SelectedLanguage   = AppSettings.PickLanguage(AvailableLanguages, AppSettings.LastLanguage);
            Browser.Load(provider, _project?.NewConversations);
            AppLog.Info($"Loaded {provider.GameName} from {path}");
            StatusText = Loc.Format("Status_FolderLoaded", provider.GameName, path);
            CreateSampleProjectCommand.NotifyCanExecuteChanged();

            if (!AppSettings.IsKnownGameDirectory(path))
                _ = OfferBackupAsync(path);
            AppSettings.MarkGameDirectoryKnown(path);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to initialise game data from {path}", ex);
            StatusText = Loc.Format("Status_LoadError", path, ex.Message);
            ReportError?.Invoke(ex);
        }
    }

    // ── Create Sample Project (Help menu) ─────────────────────────────────
    private bool CanCreateSample() => _provider is not null;

    [RelayCommand(CanExecute = nameof(CanCreateSample))]
    private async Task CreateSampleProjectAsync()
    {
        if (_provider is null) return;

        string? folder;
        try { folder = await _folderPicker.PickFolderAsync(Loc.Get("Sample_SelectFolder")); }
        catch (OperationCanceledException) { return; }
        if (folder is null) return;   // cancelled

        if (Directory.EnumerateFileSystemEntries(folder).Any())
        {
            StatusText = Loc.Get("Sample_FolderNotEmpty");
            return;
        }

        var service = new SampleProjectService(new ProcessGitRunner());
        SampleBuild build;
        try
        {
            build = service.BuildSample(_provider);
        }
        catch (SampleConversationNotFoundException ex)
        {
            AppLog.Warn($"Create sample: {ex.Message}");
            StatusText = Loc.Get("Sample_ConversationMissing");
            return;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Create sample: build failed: {ex}");
            StatusText = Loc.Get("Sample_BuildFailed");
            ReportError?.Invoke(ex);
            return;
        }

        var projectPath = Path.Combine(folder, build.ProjectFileName);
        var seed = service.SeedHistory(folder, build);
        if (seed != SampleSeedResult.Seeded)
            DialogProjectSerializer.SaveToFile(projectPath, build.Final);  // guarantee an openable file

        StatusText = seed switch
        {
            SampleSeedResult.Seeded     => Loc.Format("Sample_Created", build.ProjectFileName),
            SampleSeedResult.GitMissing => Loc.Get("Sample_CreatedNoGit"),
            _                           => Loc.Get("Sample_CreatedHistoryPartial"),
        };
        if (seed == SampleSeedResult.Partial)
            AppLog.Warn("Create sample: git history seeding failed part-way; wrote the final project.");

        await LoadProjectAsync(projectPath, offerDeferred: false);
    }

    // ── Open Walkthrough (Help menu) ──────────────────────────────────────
    private const string WalkthroughFileName = "walkthrough.md";
    private const string WalkthroughUrl = "https://github.com/kjmikkel/PillarsDialogEditor/blob/main/docs/walkthrough.md";

    /// Test/extension seam: tries each candidate (bundled path, then URL) and returns true on
    /// the first that opens. Defaults to launching via the OS handler.
    public Func<IReadOnlyList<string>, bool>? WalkthroughOpener { get; set; }

    [RelayCommand]
    private void OpenWalkthrough()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docs", WalkthroughFileName),
            WalkthroughUrl,
        };
        var opener = WalkthroughOpener ?? LaunchFirstAvailable;
        if (!opener(candidates))
        {
            AppLog.Warn("Open walkthrough: no candidate could be opened.");
            StatusText = Loc.Get("Walkthrough_OpenFailed");
        }
    }

    private static bool LaunchFirstAvailable(IReadOnlyList<string> candidates)
    {
        foreach (var c in candidates)
        {
            var isFile = !c.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            if (isFile && !File.Exists(c)) continue;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(c) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Open walkthrough: failed to launch '{c}': {ex.Message}");
            }
        }
        return false;
    }

    // ── Guided tour (Help menu) ───────────────────────────────────────────
    [RelayCommand]
    private void StartGuidedTour() => Tour.Start();

    // ── Changelog (Help menu) ─────────────────────────────────────────────
    private const string ChangelogFileName = "CHANGELOG.md";

    /// Test seam: returns the raw changelog text, or null when unavailable.
    public Func<string?>? ChangelogReader { get; set; }

    /// Set by the UI layer to open the changelog reader window.
    public Action<ChangelogViewModel>? ShowChangelog { get; set; }

    // Test seams for the launch "what's new" greeting (default to AppSettings/AppVersion).
    public Func<string>?   LastSeenVersionGetter  { get; set; }
    public Action<string>? LastSeenVersionSetter  { get; set; }
    public Func<string>?   CurrentVersionProvider { get; set; }

    /// Called once at startup: if the app version advanced since last run, open the
    /// changelog filtered to the new releases. Always records the current version so it
    /// never re-shows. See docs/superpowers/specs/2026-07-07-whats-new-on-launch-design.md.
    public void ShowWhatsNewIfUpdated()
    {
        var current = (CurrentVersionProvider ?? (() => AppVersion.Current))();
        if (string.IsNullOrEmpty(current) || current == "unknown")
            return; // no usable version — never show, never poison the baseline

        var lastSeen = (LastSeenVersionGetter ?? (() => AppSettings.LastSeenVersion))();

        var read     = ChangelogReader ?? DefaultChangelogReader;
        var text     = read();
        if (text is null) AppLog.Warn("What's new: CHANGELOG.md unavailable.");
        var releases = text is null
            ? Array.Empty<ChangelogRelease>()
            : ChangelogParser.Parse(text);

        var result = WhatsNewDecider.Decide(lastSeen, current, releases);
        if (result.ReleasesToShow.Count > 0)
            ShowChangelog?.Invoke(new ChangelogViewModel(
                result.ReleasesToShow, isWhatsNew: true, version: current));

        (LastSeenVersionSetter ?? (v => AppSettings.LastSeenVersion = v))(current);
    }

    [RelayCommand]
    private void Changelog()
    {
        var read = ChangelogReader ?? DefaultChangelogReader;
        var text = read();
        if (text is null) AppLog.Warn("Changelog: CHANGELOG.md unavailable.");
        var releases = text is null
            ? Array.Empty<ChangelogRelease>()
            : ChangelogParser.Parse(text);
        ShowChangelog?.Invoke(new ChangelogViewModel(releases));
    }

    private static string? DefaultChangelogReader()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, ChangelogFileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Changelog read failed: {ex.Message}");
            return null;
        }
    }

    // ── About (Help menu) ─────────────────────────────────────────────────
    public const string RepositoryUrl = "https://github.com/kjmikkel/PillarsDialogEditor";
    public const string IssuesUrl     = "https://github.com/kjmikkel/PillarsDialogEditor/issues";
    public const string DocsUrl       = "https://github.com/kjmikkel/PillarsDialogEditor#readme";

    /// Set by the UI layer to open the About window.
    public Action<AboutViewModel>? ShowAbout { get; set; }

    [RelayCommand]
    private void About()
        => ShowAbout?.Invoke(new AboutViewModel(AppVersion.Current, RepositoryUrl, DocsUrl));

    /// Set by the UI layer to open the dialog-text tag reference window.
    public Action<TagReferenceViewModel>? ShowTagReference { get; set; }

    [RelayCommand]
    private void TagReference()
        => ShowTagReference?.Invoke(new TagReferenceViewModel(_activeGameId));

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
            ReportError?.Invoke(ex);
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
            ReportError?.Invoke(ex);
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
                    // An empty BackupStPath means "no original existed" — restore then
                    // deletes the stringtable the patch created instead of copying back.
                    if (File.Exists(origSt)) File.Copy(origSt, backupSt);
                    else backupSt = string.Empty;
                    restoreEntries.Add(new PendingRestoreEntry(backupConv, backupSt, origConv, origSt));
                }
                // else: new conversation — nothing to back up

                // The apply loop below also writes stringtables for every *other*
                // installed language carried by the patch (TranslationApplier) —
                // back those up too, as stringtable-only entries.
                var installed = _provider.AvailableLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var lang in patch.Translations.Keys)
                {
                    if (string.Equals(lang, _provider.Language, StringComparison.OrdinalIgnoreCase)
                        || !installed.Contains(lang))
                        continue;
                    var langSt       = _provider.GetStringTablePath(file, lang);
                    var backupLangSt = Path.Combine(tempDir, $"{convName}.{lang}.stringtable.bak");
                    if (File.Exists(langSt)) File.Copy(langSt, backupLangSt);
                    else backupLangSt = string.Empty;
                    restoreEntries.Add(new PendingRestoreEntry(
                        string.Empty, backupLangSt, string.Empty, langSt));
                }
            }

            // Persist restore info before writing game files (crash safety)
            AppSettings.SetPendingRestores(restoreEntries);

            // Copy _vo/ files to the game's VO directory (PoE2 only) and append their
            // backup entries so that F6 can restore or remove them.
            if (string.Equals(_activeGameId, "poe2", StringComparison.OrdinalIgnoreCase))
            {
                SyncVoToGame(restoreEntries);
                // Re-persist so the VO entries survive a crash between here and the patch writes.
                AppSettings.SetPendingRestores(restoreEntries);
            }

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
                // The bundle/XML above only carries structure — node text lives in the
                // per-language stringtables. Without this, added or edited lines are
                // invisible in-game (B-005). Mirrors the dialog-patcher CLI.
                TranslationApplier.WriteTranslations(file, patch, _provider);
            }

            AppLog.Info($"Test: applied {_project.Patches.Count} patch(es) from project '{_project.Name}'");
            TestModeEntered?.Invoke();
        }
        catch (PatchConflictException ex)
        {
            // error-window-exempt: a patch conflict is a domain condition with its own
            // interactive recovery (RequestConflictResolution dialog / detailed status),
            // not an unexpected failure — the report window would stack on that dialog.
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
            ReportError?.Invoke(ex);
        }
    }

    private static void RestoreFilesFromBackup(IReadOnlyList<PendingRestoreEntry> entries)
    {
        foreach (var r in entries)
        {
            // Restore the original file if a backup exists, or remove the file that was
            // added by the patch (e.g. a new VO .wem) when there was no original to back up.
            if (!string.IsNullOrEmpty(r.BackupConvPath) && File.Exists(r.BackupConvPath))
                File.Copy(r.BackupConvPath, r.OriginalConvPath, overwrite: true);
            else if (string.IsNullOrEmpty(r.BackupConvPath) && File.Exists(r.OriginalConvPath))
                File.Delete(r.OriginalConvPath); // VO file added by patch — remove on restore

            if (!string.IsNullOrEmpty(r.BackupStPath) && File.Exists(r.BackupStPath)
                && !string.IsNullOrEmpty(r.OriginalStPath))
                File.Copy(r.BackupStPath, r.OriginalStPath, overwrite: true);
            else if (string.IsNullOrEmpty(r.BackupStPath) && !string.IsNullOrEmpty(r.OriginalStPath)
                && File.Exists(r.OriginalStPath))
                File.Delete(r.OriginalStPath); // stringtable created by the patch — remove on restore
        }
    }

    /// <summary>
    /// Copies every .wem file in the project's <c>_vo/</c> folder to the game's VO root,
    /// backing up any existing game file first. A <see cref="PendingRestoreEntry"/> is
    /// appended to <paramref name="restoreEntries"/> for each file so that F6 can
    /// restore the original (or delete the newly added file when there was no original).
    /// </summary>
    /// <remarks>
    /// Only called when <c>_activeGameId</c> is "poe2" — the VO path is PoE2-specific.
    /// <c>BackupConvPath</c> stores the backup path (or empty string when no original
    /// existed); <c>OriginalConvPath</c> stores the game destination path. String-table
    /// fields are left empty because audio files have no associated string table.
    /// </remarks>
    private void SyncVoToGame(IList<PendingRestoreEntry> restoreEntries)
    {
        if (ProjectPath is null) return;

        var voFolder = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo");
        if (!Directory.Exists(voFolder)) return;

        var gameVoRoot = Path.Combine(_currentGameDirectory,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");

        foreach (var localFile in Directory.EnumerateFiles(voFolder, "*.wem", SearchOption.AllDirectories))
        {
            try
            {
                var relative  = Path.GetRelativePath(voFolder, localFile).Replace('\\', '/');
                var gameDest  = Path.Combine(gameVoRoot, relative);
                var backupDir = Path.Combine(Path.GetTempPath(), "PillarsDialogEditor",
                    "vobackup", Guid.NewGuid().ToString("N")[..8]);

                Directory.CreateDirectory(Path.GetDirectoryName(gameDest)!);

                var backupPath = Path.Combine(backupDir, Path.GetFileName(gameDest));

                // Back up the existing game file if present; otherwise backupPath won't exist
                // and F6 will delete the file rather than restore it.
                if (File.Exists(gameDest))
                {
                    Directory.CreateDirectory(backupDir);
                    File.Copy(gameDest, backupPath);
                }

                // Record BEFORE writing — if File.Copy fails mid-write the entry already
                // exists so F6 can restore or remove the file even on a partial write.
                restoreEntries.Add(new PendingRestoreEntry(
                    File.Exists(backupPath) ? backupPath : string.Empty,
                    string.Empty,
                    gameDest,
                    string.Empty));

                File.Copy(localFile, gameDest, overwrite: true);
                AppLog.Info($"VO sync: {relative} → {gameDest}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error($"SyncVoToGame: failed to copy '{localFile}'", ex);
                // Continue with remaining files — partial sync is better than aborting;
                // the window's per-type dedupe keeps repeated per-file failures to one report.
                ReportError?.Invoke(ex);
            }
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
                // Restore the backed-up file, or remove a file that was newly added by the patch.
                if (!string.IsNullOrEmpty(r.BackupConvPath) && File.Exists(r.BackupConvPath))
                {
                    File.Copy(r.BackupConvPath, r.OriginalConvPath, overwrite: true);
                    tempDirsToDelete.Add(Path.GetDirectoryName(r.BackupConvPath)!);
                }
                else if (string.IsNullOrEmpty(r.BackupConvPath) && File.Exists(r.OriginalConvPath))
                    File.Delete(r.OriginalConvPath); // VO file added by patch — remove on restore

                if (!string.IsNullOrEmpty(r.BackupStPath) && File.Exists(r.BackupStPath)
                    && !string.IsNullOrEmpty(r.OriginalStPath))
                    File.Copy(r.BackupStPath, r.OriginalStPath, overwrite: true);
                else if (string.IsNullOrEmpty(r.BackupStPath) && !string.IsNullOrEmpty(r.OriginalStPath)
                    && File.Exists(r.OriginalStPath))
                    File.Delete(r.OriginalStPath); // stringtable created by the patch — remove on restore
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
            ReportError?.Invoke(ex);
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

            // Reapply this conversation's saved patch so the canvas shows the user's
            // edits, not the vanilla game file. BaseSnapshot must stay the *vanilla*
            // state: SaveProject re-diffs BaseSnapshot → canvas and replaces the stored
            // patch wholesale, so a patched baseline would silently erase every edit
            // made in earlier sessions (and F5 applies patches against vanilla).
            if (_project?.Patches.TryGetValue(file.Name, out var storedPatch) == true)
            {
                var vanillaSnap = ConversationSnapshotBuilder.Build(conversation);
                ConversationEditSnapshot patchedSnap;
                try
                {
                    patchedSnap = PatchApplier.Apply(vanillaSnap, storedPatch);
                }
                catch (PatchConflictException conflict)
                {
                    // Game file changed underneath the patch (e.g. a game update).
                    // Still show the user's edits — force-apply for display and surface
                    // the mismatch; F5 keeps its strict conflict flow for real writes.
                    AppLog.Warn($"Patch for '{file.Name}' no longer matches game data " +
                        $"(node {conflict.NodeId}, field '{conflict.FieldName}'); forcing apply for display");
                    patchedSnap = PatchApplier.Apply(vanillaSnap, storedPatch, ignoreConflicts: true);
                    StatusText = Loc.Format("Status_PatchBaselineMismatch", file.Name);
                }
                var translations = storedPatch.Translations.GetValueOrDefault(_provider.Language);
                var patched = ConversationSnapshotBuilder.ToConversation(file.Name, patchedSnap, translations);
                Canvas.Load(patched, vanillaSnap);
            }
            else
            {
                Canvas.Load(conversation);
            }

            // Restore saved layout if the project has positions for this conversation.
            // Runs after AutoLayout so it always wins over the algorithmic positions.
            var savedLayout = _project?.GetLayout(file.Name);
            if (savedLayout is not null)
                Canvas.RestoreLayout(savedLayout);

            var savedAnnotations2 = _project?.GetAnnotations(file.Name);
            if (savedAnnotations2 is not null)
                Canvas.RestoreAnnotations(savedAnnotations2);

            Detail.Canvas = Canvas;
            Canvas.Detail = Detail;
            var existingComments = _project?.Patches.TryGetValue(file.Name, out var p) == true
                ? p.NodeComments
                : new Dictionary<int, string>();
            Canvas.LoadNodeComments(existingComments);

            Detail.Clear();
            IsModified = false;
            CurrentConversationName = file.Name;
            OnPropertyChanged(nameof(CanValidateVO));
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
            ReportError?.Invoke(ex);
        }
    }
}
