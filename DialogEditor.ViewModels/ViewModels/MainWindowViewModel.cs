using DialogEditor.Core.Analytics;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using DialogEditor.Patch.Schema;
using DialogEditor.Patch.Install;
using DialogEditor.Patch.Changelog;
using DialogEditor.Patch.Diff;
using DialogEditor.Patch.GitConflict;
using DialogEditor.ViewModels.Resources;
using DialogEditor.Core.Logging;
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

    /// The writer's assumed game state (View > Game State): one for the whole session, shared
    /// across conversations because globals are game-wide. Node Details reads the same state
    /// to show each condition list's verdict.
    public GameStateViewModel    GameState { get; }

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

    // Conversation GUID → name, cached on folder open (issue #14). Its source,
    // LoadGameDataNames(), parses every bundle on disk and is documented as called once per
    // folder open, so it must not run per analysis. GameDataNameService is unsuitable: it
    // stores NamedEntry(DisplayName, StoredValue) with DisplayName composed as
    // "{name} — {id}", so recovering the name would mean splitting on " — ".
    private IReadOnlyDictionary<string, string> _conversationNamesById =
        new Dictionary<string, string>();
    public string?            ProjectPath => _projectPath;
    private string             _currentGameDirectory = string.Empty;
    private string             _activeGameId         = string.Empty;

    /// Set by the UI layer to provide a name-input dialog for new conversations.
    public Func<Task<string?>>? RequestConversationName { get; set; }

    /// Set by the UI layer to ask for a conversation name pre-filled with a suggestion.
    /// Returns the confirmed name, or null if cancelled.
    public Func<string, Task<string?>>? RequestConversationNameWithSuggestion { get; set; }

    /// Set by the UI layer to surface a patch conflict and ask whether to force-apply.
    /// Returns true if the user chooses Force Apply, false to cancel.
    public Func<PatchConflictException, Task<bool>>? RequestConflictResolution { get; set; }

    /// Set by the UI layer. Asked before Test Patch writes files that mods installed with the
    /// Patch Manager / dialog-patcher also manage (argument: how many). True = continue.
    public Func<int, Task<bool>>? ConfirmTestOverPatcherMods { get; set; }

    /// Set by the UI layer. Asked when Restore (F6) cannot put some game files back because
    /// their temp backups are gone (argument: those game files). True = leave test mode
    /// anyway; false, or no UI, keeps test mode so F6 can be retried (issue 125).
    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmLeaveTestModeUnrestored { get; set; }

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

    /// Set by the UI layer: shows a one-button message (title, text). Used for files saved by a
    /// newer editor (GitHub issue 62). That's not a bug, so it must not go through ReportError's
    /// crash report.
    public Func<string, string, Task>? ShowUnsupportedFormat { get; set; }

    private async Task RefuseNewerFormatAsync(UnsupportedSchemaVersionException ex, string path)
    {
        AppLog.Warn($"Refused '{path}': {ex.Message}");
        StatusText = SchemaVersionMessages.TooNew(ex, Path.GetFileName(path));
        if (ShowUnsupportedFormat is not null)
            await ShowUnsupportedFormat(SchemaVersionMessages.Title, StatusText);
    }

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

    [ObservableProperty] private string? _currentConversationName;

    /// The shell-level docking layout root (an IRootDock, kept as object so this
    /// Dock-free VM project never references Dock.*). Built by MainWindow's code-behind
    /// once a game is loaded and bound to <c>DockControl.Layout</c>.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWelcome))]
    private object? _dockLayout;

    /// The welcome panel fills the work area until the dock layout exists, which is once a
    /// game folder is open (GitHub issue 252).
    public bool ShowWelcome => DockLayout is null;

    /// The Condition/script-search dock tool's VM. Owned at the shell level (rather than by
    /// Canvas) so EditorDockFactory can host it alongside Browser/Canvas/Detail as its own
    /// dock tool. Null until a game is loaded (built in LoadDirectory/RebuildConditionSearch).
    public ConditionSearchViewModel? ConditionSearch { get; private set; }

    // ── Window title reflects dirty state ─────────────────────────────────
    /// True when there are unsaved changes worth signalling and guarding. A dirty
    /// project is unsaved whether the dirt came from canvas edits (a conversation is
    /// open) OR a project-level metadata edit with no conversation open — e.g. adding
    /// a duplicate to the ignore allowlist. The open-project half is what covers that
    /// second case; without it, metadata-only dirt was invisible and unguarded.
    private bool HasUnsavedChanges =>
        IsModified && (CurrentConversationName is not null || IsProjectOpen);

    public string WindowTitle
    {
        get
        {
            var project = CurrentProjectName is not null ? $" [{CurrentProjectName}]" : string.Empty;
            var dirty   = HasUnsavedChanges ? "● " : string.Empty;
            return CurrentConversationName is not null
                ? $"{dirty}{CurrentConversationName}{project}"
                : $"{dirty}{Loc.Get("App_Title")}{project}";
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
        if (HasUnsavedChanges)
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
        _guardDiscarded = false;
        if (!HasUnsavedChanges)
            return Task.FromResult(true);

        // Defensive: resolve any stale pending decision and drop a stale pending-file
        // load so this flow never inherits another guard's continuation (the UI is
        // modal so this should not happen, but the shared state makes it worth guarding).
        _unsavedDecision?.TrySetResult(false);
        _pendingFile = null;

        // Held locally: a handler that answers synchronously (Save/Discard/Cancel) clears the field
        // before Invoke returns.
        var decision = _unsavedDecision = new TaskCompletionSource<bool>();
        _pendingAction = () => { _unsavedDecision?.TrySetResult(true); _unsavedDecision = null; };
        UnsavedChangesRequested?.Invoke();
        return decision.Task;
    }

    public void SaveAndProceed()
    {
        SaveCommand.Execute(null);
        Proceed();
    }

    public void DiscardAndProceed()
    {
        // Put the open conversation back to its saved state first (issue 234). The continuation
        // may not replace the canvas (a cancelled picker, Later, a cancelled merge), and a canvas
        // still holding the discarded edits would fold them into the next Save. A conversation
        // switch loads another conversation anyway, so it is not reverted first (and is reopened
        // as saved if that load fails, issue 328).
        if (_pendingFile is null && !ReloadCurrentConversationView())
        {
            // The load reported its own error. Nothing was discarded, so nothing proceeds: the
            // edits stay on screen and unsaved, and the autosave copy is kept.
            AppLog.Warn("Discard: the open conversation could not be reverted to its saved state; the action was not carried out");
            CancelPendingNavigation();
            return;
        }
        // The user consciously discarded these edits — the next launch must not
        // resurrect them from the autosave sidecar (spec 2026-07-12 §3).
        if (_projectPath is not null) AutosaveRecovery.TryDelete(_projectPath);
        // Left dirty, a project swapped in by the continuation skipped its canvas reload
        // (FinishLoad). Project-level edits set only the window's flag, so it is cleared here;
        // the canvas flag is kept in step, or the next edit would not mark the window dirty.
        _guardDiscarded = true;
        Canvas.IsModified = false;
        IsModified = false;
        Proceed();
    }

    /// Set by Discard for the awaiting caller of EnsureNoUnsavedEditsAsync, which can no longer
    /// tell Discard from Save by IsModified. Reset by each EnsureNoUnsavedEditsAsync call.
    private bool _guardDiscarded;

    public void CancelPendingNavigation()
    {
        _pendingFile   = null;
        _pendingAction = null;
        // A cancelled Find-in-Project navigation must not leave a stale node id
        // that a later, unrelated conversation load would inherit.
        _pendingSelectNodeId = null;
        _unsavedDecision?.TrySetResult(false);
        _unsavedDecision = null;
    }

    private void Proceed()
    {
        var file   = _pendingFile;
        var action = _pendingAction;
        _pendingFile   = null;
        _pendingAction = null;
        if (file   is not null) OpenPendingConversation(file);
        action?.Invoke();
    }

    /// Opens the conversation a switch was waiting on the way a plain selection does, so one the
    /// project created opens too (issue 328). If it fails to load, the one that was open is
    /// reopened as saved: Discard did not revert it, and the failed load has already pointed
    /// _currentFile at the target, so a later Save would have written its canvas there.
    private void OpenPendingConversation(ConversationFile file)
    {
        var previous = _currentFile;
        if (OpenConversationView(file) || previous is null) return;
        _currentFile = previous;
        if (!ReloadCurrentConversationView())
            AppLog.Warn($"Switch to '{file.Name}' failed and '{previous.Name}' could not be reopened either");
    }

    // ── Project open state ────────────────────────────────────────────────
    public bool IsProjectOpen => _project is not null;

    /// The active game id ("poe1"/"poe2"/""), for consumers that validate against
    /// the tag vocabulary (Flow Analytics token validation).
    public string ActiveGameId => _activeGameId;

    /// (Re)builds the shell-level condition/script-search VM for the given game so it offers
    /// that game's catalogue, and wires its snapshot/highlight callbacks to Canvas. Called from
    /// LoadDirectory once a provider is detected; empty gameId nulls the dock (no game loaded).
    private void RebuildConditionSearch(string gameId)
    {
        ConditionSearch = string.IsNullOrEmpty(gameId)
            ? null
            : new ConditionSearchViewModel(
                gameId,
                () => Canvas.Nodes.Count > 0 ? Canvas.BuildSnapshot() : null,
                matches => Canvas.ApplyConditionHighlight(matches),
                () => Canvas.ClearConditionHighlight());
        OnPropertyChanged(nameof(ConditionSearch));
    }

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
        IsEditingMode
        && _project is not null
        && _provider is not null
        && _projectPath is not null
        && !string.IsNullOrEmpty(_currentGameDirectory)
        && string.Equals(_activeGameId, "poe2", StringComparison.OrdinalIgnoreCase);

    /// True when a saved project is open — gates the "Export Mod Bundle…" menu
    /// item. The pack includes vo/ exactly when _vo/ exists (see VoPackExporter),
    /// so export is meaningful for any saved project, voiced or text-only.
    public bool CanExportModBundle => ProjectPath is not null;

    /// Find in Project needs a project open, a game-data provider, and a game folder
    /// (it loads each patched conversation's effective text). Unlike batch VO it is
    /// game-agnostic and needs no saved project path.
    public bool CanFindInProject =>
        IsEditingMode
        && _project is not null
        && _provider is not null
        && !string.IsNullOrEmpty(_currentGameDirectory);

    /// A loaded provider and a game folder (the "on disk" scope enumerates every conversation).
    /// No project needed: a Reader browsing a game folder can use it (issue 234). Read-only;
    /// never writes, so it stays available in both modes.
    public bool CanShowRepDispositionBalance =>
        _provider is not null
        && !string.IsNullOrEmpty(_currentGameDirectory);

    /// The open project's spelling words (issue 9); null when none or no project is open.
    public IReadOnlyList<string>? ProjectWords => _project?.ProjectWords;

    // Metadata-only edit like the ignore-duplicate closures: no SetProject, no undo.
    private void AddProjectWord(string word)
    {
        if (_project is null) return;
        var next = _project.WithProjectWord(word);
        if (ReferenceEquals(next, _project)) return;
        _project = next;
        IsModified = true;
    }

    private void SetProject(DialogProject? project)
    {
        var prevNew  = _project?.NewConversations;
        var nextNew  = project?.NewConversations;
        var hadProject = _project is not null;
        _project = project;
        // Project spelling words (issue 9): only touch Detail when open/closed flips, so the
        // spelling rows aren't refreshed on every patch save.
        if (hadProject != (project is not null))
            Detail.AddWordToProjectDictionary = project is null ? null : AddProjectWord;
        UpdateEditable();
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
        FindInProjectCommand.NotifyCanExecuteChanged();
        BatchReplaceCommand.NotifyCanExecuteChanged();
        ShowRepDispositionBalanceWindowCommand.NotifyCanExecuteChanged();
        BrowseSpeakerLinesCommand.NotifyCanExecuteChanged();
        // Only re-scan the game folder when NewConversations actually changes —
        // not on every patch save, which would re-enumerate all conversations.
        if (_provider is not null && !ReferenceEquals(prevNew, nextNew))
            Browser.Load(_provider, nextNew);
        // Every project change comes through here, so the dock's "changed in this project" marks
        // follow it (issue 85, part C). An empty patch (opened, not changed) isn't an edit.
        Browser.SetProjectConversations(project is null ? null
            : project.Patches.Where(kv => !kv.Value.IsEmpty).Select(kv => kv.Key).ToHashSet());
    }

    // ── Editor mode (Reading / Editing, issue 234) ─────────────────────────────
    /// Never-chosen (null) behaves as Editing here; the first-run prompt that asks is a later task.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadingMode))]
    [NotifyPropertyChangedFor(nameof(IsEditingMode))]
    private EditorMode _mode = AppSettings.Mode ?? EditorMode.Editing;

    public bool IsReadingMode => Mode == EditorMode.Reading;
    public bool IsEditingMode => Mode == EditorMode.Editing;

    // The canvas is editable only with a project open AND in Editing mode; Detail mirrors the mode.
    private void UpdateEditable()
    {
        Canvas.IsEditable = _project is not null && IsEditingMode;
        Canvas.IsReadingMode = IsReadingMode;
        Detail.IsReadingMode = IsReadingMode;
    }

    /// Switches mode, saving it for the next launch. Entering Reading goes through the unsaved-edits
    /// guard (Reading is read-only, so dirty edits must be saved or discarded first); returns false
    /// when the user picks Stay Here or a discard cannot be reverted. The open conversation and selected node are kept.
    public async Task<bool> SwitchModeAsync(EditorMode target)
    {
        if (target == Mode) return true;
        if (target == EditorMode.Reading)
        {
            if (!await EnsureNoUnsavedEditsAsync())
                return false;
            if (_guardDiscarded)
            {
                // Discard has already put an open conversation back to its saved state (and
                // reselected its node). With none open the edits were project-level only, and
                // reverting them means re-reading the project; if that fails, stay in Editing
                // with them marked unsaved rather than show lost edits.
                if (_currentFile is null && !await RevertProjectToSavedAsync())
                {
                    IsModified = true;
                    StatusText = Loc.Get("Status_ModeSwitchRefused");
                    return false;
                }
            }
            // Still dirty after Save means the save failed (its error is already shown): stay
            // in Editing with the edits rather than carry them into read-only Reading.
            else if (HasUnsavedChanges)
                return false;
        }

        Mode = target;
        AppSettings.Mode = target;
        // A running tour describes the mode just left; drop it, then offer the new mode's own
        // tour if this is the first time it is shown.
        if (Tour.IsVisible) Tour.DismissCommand.Execute(null);
        if (target == EditorMode.Editing)
        {
            // The deferred prompts and the canvas reload come first; the tour would otherwise sit
            // under the autosave and conflict modals, pointing at a canvas about to be replaced.
            await OnEnteredEditingModeAsync();
            if (!GuidedTourViewModel.HasSeen(target)) Tour.Start(target);
        }
        else if (!GuidedTourViewModel.HasSeen(target))
            Tour.Start(target);
        return true;
    }

    /// Puts project-level edits (no conversation open, so no view to lose) back to their saved
    /// state after Discard by re-reading the project from its file. An open conversation is
    /// reverted by DiscardAndProceed itself. False when there is nothing to re-read or the load
    /// did not complete.
    private async Task<bool> RevertProjectToSavedAsync()
    {
        var path = _projectPath;
        if (path is null || !File.Exists(path))
        {
            AppLog.Warn("Reading mode: discard could not be reverted, the project has no saved file to re-read");
            return false;
        }
        var before = _project;
        await LoadProjectAsync(path, offerDeferred: false);
        // A completed load swaps in a freshly read project (it does not reset IsModified).
        if (!IsProjectOpen || ReferenceEquals(before, _project))
        {
            AppLog.Warn($"Reading mode: reloading '{path}' to discard its edits did not complete");
            return false;
        }
        IsModified = false;
        return true;
    }

    /// Opens a conversation on the canvas from the current project: a conversation the project
    /// created and that has no game file yet starts from the project's patch alone.
    /// False when the load failed (already logged and reported).
    private bool OpenConversationView(ConversationFile file)
    {
        if (_project?.IsNewConversation(file.Name) == true && !File.Exists(file.ConversationPath))
        {
            LoadNewConversation(file);
            return true;
        }
        return LoadConversationFile(file);
    }

    /// Re-opens the open conversation from the current project and reselects the node that was
    /// selected. Needed whenever the project is swapped under an open canvas (issue 234): the
    /// canvas and its BaseSnapshot would otherwise keep the old project's text, and the next Save
    /// would re-diff that stale canvas over the swapped-in work. A node the new project no longer
    /// has is simply not selected; no "node gone" message. False when the load failed.
    private bool ReloadCurrentConversationView()
    {
        if (_currentFile is not { } file) return true;
        var selectedId = Canvas.SelectedNode?.NodeId;
        if (!OpenConversationView(file)) return false;
        var selected = Canvas.Nodes.FirstOrDefault(n => n.NodeId == selectedId);
        if (selected is not null) Canvas.SelectedNode = selected;
        return true;
    }

    // Work that opening in reading mode put off because it would have written (issue 234).
    private string? _deferredOpenPath;     // project with a newer autosave copy to offer
    private string? _deferredBackupPath;   // game folder whose backup offer / top-up was skipped

    /// Runs what reading mode deferred, project prompts first: the autosave offer (which also
    /// covers a conflict, as the reload re-checks it), else the pending conflict, then the backup.
    private async Task OnEnteredEditingModeAsync()
    {
        if (_deferredOpenPath is { } openPath)
        {
            _deferredOpenPath = null;
            // Only if that project is still the open one; a different or closed project means the
            // deferral is stale and reloading would clobber what is open now.
            if (_projectPath == openPath)
            {
                var before = _project;
                await LoadProjectAsync(openPath, offerDeferred: false);
                // Later (issue 236) leaves the project that was opened for reading in place, now
                // in Editing, where Save, autosave rotation and Discard would delete or rotate the
                // copies Later promised to keep. Close it instead, as Later does outside reading mode.
                if (ReferenceEquals(before, _project) && IsProjectOpen
                    && AutosaveRecovery.Check(openPath).State == AutosaveState.Newer)
                {
                    CloseProjectCore(Loc.Format("Status_AutosaveKeptNotOpened", Path.GetFileName(openPath)));
                    ClearCanvasAfterClose();
                }
                // Discarding the copies re-read the file and offered the merge; cancelled, the
                // reading-mode "mine" view is still open over the conflicted file.
                else if (ReferenceEquals(before, _project))
                    CloseIfConflictStillPending();
            }
        }
        else if (HasPendingConflictResolution)
        {
            var before = _project;
            await ResolveConflicts();
            if (ReferenceEquals(before, _project))
                CloseIfConflictStillPending();
        }

        if (_deferredBackupPath is { } backupPath)
        {
            _deferredBackupPath = null;
            if (_provider is not null && backupPath == _currentGameDirectory)
                RunBackupStep(backupPath, _provider);
        }
    }

    /// True while the open project's own file still holds an unresolved git conflict. Only
    /// reading mode opens such a file (the "mine" side, issue 234); in Editing any save would
    /// write that side over the markers and lose the other branch's version.
    private bool ConflictPendingOnOpenProject =>
        HasPendingConflictResolution && _projectPath is not null && _projectPath == _pendingConflictPath;

    /// True, logged and reported, when the open project's file still holds an unresolved git
    /// conflict, so the caller must not write it. Shared by SaveProject and the writers that
    /// bypass it (Apply from diff, Undo apply, Merge Projects; issue 329), which call it before
    /// they change anything.
    private bool RefuseWriteWhileConflictPending(string action)
    {
        if (!ConflictPendingOnOpenProject) return false;
        AppLog.Warn($"{action} refused: '{_projectPath}' has an unresolved git conflict");
        StatusText = Loc.Format("Status_SaveRefusedConflictPending", Path.GetFileName(_projectPath));
        return true;
    }

    /// Editing never keeps a conflicted project open (issue 234), as outside reading mode a
    /// cancelled merge never opens it. LastProjectPath is kept, so the next launch offers the
    /// resolution again.
    private void CloseIfConflictStillPending()
    {
        if (!IsProjectOpen || !ConflictPendingOnOpenProject) return;
        var path = _projectPath!;
        AppLog.Info($"Closed '{path}' on entering editing: its git conflict is unresolved");
        CloseProjectCore(Loc.Format("Status_ConflictUnresolvedNotOpened", Path.GetFileName(path)));
        ClearCanvasAfterClose();
    }

    [RelayCommand]
    private Task ToggleReadingMode()
        => SwitchModeAsync(IsReadingMode ? EditorMode.Editing : EditorMode.Reading);

    // ── Partial hooks ─────────────────────────────────────────────────────
    partial void OnModeChanged(EditorMode value)
    {
        UpdateEditable();
        // The status bar mirrors the focused control's HelpText. The mode badge keeps focus after
        // its click, so its previous-mode tooltip would linger until focus moved (issue 234).
        FocusHintText = string.Empty;
        // Every editing command's gate includes IsEditingMode (issue 234), so re-evaluate them all;
        // OpenProject, CloseProject, OpenFolder, recents, tour, changelog and About stay on.
        NewProjectCommand.NotifyCanExecuteChanged();
        NewConversationCommand.NotifyCanExecuteChanged();
        ImportConversationCommand.NotifyCanExecuteChanged();
        ExportConversationsCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        UndoApplyCommand.NotifyCanExecuteChanged();
        MergeProjectsCommand.NotifyCanExecuteChanged();
        ExportForTranslationCommand.NotifyCanExecuteChanged();
        ImportTranslationCommand.NotifyCanExecuteChanged();
        CreateSampleProjectCommand.NotifyCanExecuteChanged();
        OpenWalkthroughCommand.NotifyCanExecuteChanged();
        TagReferenceCommand.NotifyCanExecuteChanged();
        RestoreBackupCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        TestPatchCommand.NotifyCanExecuteChanged();
        FindInProjectCommand.NotifyCanExecuteChanged();
        BatchReplaceCommand.NotifyCanExecuteChanged();
        BatchImportVoAllCommand.NotifyCanExecuteChanged();
        ResolveConflictsCommand.NotifyCanExecuteChanged();
    }

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
        Canvas  = new ConversationViewModel(dispatcher) { ShowMinimap = AppSettings.ShowMinimap };

        Detail.IsReadingMode = IsReadingMode;
        Canvas.IsReadingMode = IsReadingMode;

        GameState = new GameStateViewModel(new DialogEditor.Core.Conditions.GameState(), LookupGameDataName);
        Detail.AssumedState = GameState.State;
        WireGameStateSources();

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

        // No PropertyChanged hook on the connections themselves (issue 142). Link edits
        // (display, weight, conditions) go through the shared undo stack, whose
        // CommandExecuted already marks the canvas modified. A blanket hook also fired for
        // IsHighlighted, which selecting a node sets, so merely viewing a linked node
        // marked the conversation modified.
        Canvas.Connections.CollectionChanged += (_, _) => RefreshDetailLinks();

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

        Canvas.StartNodeAttempted += (_, e) => StatusText = e.Outcome switch
        {
            StartNodeOutcome.Made => StartNodeReferences(e) is var refs and > 0
                ? Loc.FormatCount("Status_StartNodeMadeWithReferences", refs, e.OldNodeId)
                : Loc.Format("Status_StartNodeMade", e.OldNodeId),
            StartNodeOutcome.StartNodeExists => Loc.Get("Status_StartNodeExists"),
            StartNodeOutcome.VoTargetExists  => Loc.Format("Status_StartNodeVoTargetExists", e.OldNodeId, e.Detail),
            StartNodeOutcome.VoRenameFailed  => Loc.Format("Status_StartNodeVoRenameFailed", e.OldNodeId, e.Detail),
            _                                => Loc.Get("Status_StartNodeNotAllowed"),
        };

        // Canvas context action: "Show all lines by this speaker" forwards the node's
        // speaker GUID up here (we own the project/provider and the dirty guard).
        Canvas.RequestBrowseSpeakerLines += guid => _ = OpenSpeakerLineBrowserAsync(guid);

        var last = AppSettings.LastGameDirectory;
        if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
            LoadDirectory(last);

        // Crash recovery: if a test was in progress when the app closed, re-enter test mode
        if (AppSettings.GetPendingRestores() is not null)
            TestModeEntered?.Invoke();

        // Re-opening the last project is deferred to ReopenLastProjectOnStartup(),
        // invoked by the View once the window is shown and callbacks are wired — so a
        // conflicted project can offer resolution rather than only showing guidance.

        // Must run last: OpenRecentProjectCommand/ClearRecentProjectsCommand (source-
        // generated RelayCommands) are only available once the constructor body runs.
        RefreshRecentProjects();

        // Privacy mode (issue 270) changes how paths are displayed: re-mask the recent-
        // project tooltips and refresh the View-menu toggle.
        PrivacyMode.WeakSubscribe(this, vm =>
        {
            vm.OnPropertyChanged(nameof(IsPrivacyModeEnabled));
            vm.RefreshRecentProjects();
        });
    }

    /// The View ▸ Show Minimap toggle (GitHub issue 264): at large text sizes the minimap can
    /// cover cards, so it can be hidden, and the choice is remembered.
    public bool ShowMinimap
    {
        get => Canvas.ShowMinimap;
        set
        {
            if (Canvas.ShowMinimap == value) return;
            Canvas.ShowMinimap = value;
            AppSettings.ShowMinimap = value;
            OnPropertyChanged();
        }
    }

    /// The View ▸ "Hide private paths" toggle. Reads the effective state (so it shows
    /// checked when --privacy-mode forced it on for this session); writing persists the
    /// choice. Re-raised from the PrivacyMode.Changed subscription in the constructor.
    public bool IsPrivacyModeEnabled
    {
        get => PrivacyMode.IsOn;
        set => PrivacyMode.SetPersisted(value);
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

    private bool CanResolveConflicts() => IsEditingMode && HasPendingConflictResolution;

    [RelayCommand(CanExecute = nameof(CanResolveConflicts))]
    private async Task ResolveConflicts()
    {
        var path = _pendingConflictPath;
        if (path is null || !File.Exists(path))
        {
            ClearPendingConflict();
            return;
        }
        // The merge replaces the open project (issue 234), so edits made while the conflict was
        // pending would be saved over the merged choices. The usual Save / Discard / Stay Here
        // prompt can't be used: Save writes this side over the conflicted file, which removes
        // the markers and loses the other branch's version. Refuse instead, and say why; the
        // edits and the pending conflict are left as they are.
        if (HasUnsavedChanges)
        {
            StatusText = Loc.Get("Status_ResolveConflictsUnsavedEdits");
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
    // ── Game State panel feed ─────────────────────────────────────────────
    private readonly HashSet<INotifyPropertyChanged> _conditionSources = [];

    /// Display name for a game-data id (companion, quest, conversation), without the
    /// " — id" suffix the pickers append; null when the game data has no such entry.
    private static string? LookupGameDataName(string kind, string id)
    {
        var entry = GameDataNameService.Get(kind)
            .FirstOrDefault(e => string.Equals(e.StoredValue, id, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        var suffix = " — " + entry.StoredValue;
        return entry.DisplayName.EndsWith(suffix, StringComparison.Ordinal)
            ? entry.DisplayName[..^suffix.Length]
            : entry.DisplayName;
    }

    /// Reloads the Game State rows whenever the open conversation, or any condition list on its
    /// canvas, changes. Lists come from the canvas (the edited state), not the game file.
    private void WireGameStateSources()
    {
        Canvas.ConversationLoaded += (_, _) => ReloadGameState();
        Canvas.Nodes.CollectionChanged += (_, e) => TrackConditionSources(e, Canvas.Nodes.Cast<INotifyPropertyChanged>());
        Canvas.Connections.CollectionChanged += (_, e) => TrackConditionSources(e, Canvas.Connections.Cast<INotifyPropertyChanged>());
    }

    private void TrackConditionSources(System.Collections.Specialized.NotifyCollectionChangedEventArgs e,
                                       IEnumerable<INotifyPropertyChanged> current)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            // Clear() resets with no OldItems: drop every subscription, then re-add what remains.
            foreach (var s in _conditionSources) s.PropertyChanged -= OnConditionSourceChanged;
            _conditionSources.Clear();
            foreach (var s in current) if (_conditionSources.Add(s)) s.PropertyChanged += OnConditionSourceChanged;
            ReloadGameState();
            return;
        }
        if (e.OldItems is not null)
            foreach (INotifyPropertyChanged s in e.OldItems)
                if (_conditionSources.Remove(s)) s.PropertyChanged -= OnConditionSourceChanged;
        if (e.NewItems is not null)
            foreach (INotifyPropertyChanged s in e.NewItems)
                if (_conditionSources.Add(s)) s.PropertyChanged += OnConditionSourceChanged;
        // A removed node or link takes its conditions with it; additions arrive empty, and a
        // whole load is picked up once by ConversationLoaded.
        if (e.OldItems is not null) ReloadGameState();
    }

    private void OnConditionSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.Conditions)) ReloadGameState();
    }

    private void ReloadGameState()
    {
        var lists = new List<IReadOnlyList<ConditionNode>>();
        foreach (var n in Canvas.Nodes) lists.Add(n.Conditions);
        foreach (var c in Canvas.Connections) lists.Add(c.Conditions);
        GameState.Load(lists, _activeGameId);
    }

    private void RefreshDetailLinks()
    {
        if (Canvas.SelectedNode is null) return;
        Detail.RefreshLinks(
            Canvas.Connections
                .Where(c => c.Source.Owner == Canvas.SelectedNode));
    }

    // ── Settings ──────────────────────────────────────────────────────────
    public SettingsViewModel CreateSettingsViewModel(IFontScaleApplier? fontScaleApplier = null,
                                                     IAutosaveScheduler? autosaveScheduler = null)
        => new(_currentGameDirectory, _folderPicker, fontScaleApplier,
               SpellStoreFactory?.Invoke(), autosaveScheduler);

    /// The Patch Manager opened from File → Patch Manager starts on the editor's game
    /// folder, so the writer doesn't browse to it a second time (issue 183). Set through
    /// the property, not a field, so the backup status refreshes for that folder.
    public PatchManagerViewModel CreatePatchManagerViewModel(IFolderPicker folderPicker, IFilePicker filePicker)
        => new(folderPicker, filePicker) { GameFolder = _currentGameDirectory };

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

        Func<bool, IReadOnlyList<StaleDataRow>> staleScan = includeLikely =>
        {
            if (_project is null) return [];
            var eff = includeLikely && _provider is not null ? BuildEffectiveNodeIds() : null;
            return ProjectStaleDataScanner.Scan(_project, eff);
        };

        Action<IReadOnlyList<StaleDataRow>> prune = staleRows =>
        {
            if (_project is null) return;
            SetProject(StaleDataPruner.Prune(_project, staleRows));
            SaveProject();
        };

        // Scan scope and threshold come from the window (issue #14), so the writer can
        // widen or tighten the sweep and see the report change without leaving it.
        Func<DuplicateScanOptions, IReadOnlyList<VanillaLine>?, DuplicateLineReport> dupScan = (options, vanilla) =>
            _project is null
                ? new DuplicateLineReport([], [])
                : DuplicateLineScanner.Scan(_project, _provider?.Language ?? "", options, vanilla);

        Func<IReadOnlyList<IgnoredDuplicate>> ignoredList = () =>
            _project?.IgnoredDuplicates ?? [];

        // Ignore/un-ignore are metadata-only edits: mutate _project directly and mark
        // dirty (persists on next save, like annotations) — no SetProject side effects.
        Action<IgnoredDuplicate> ignore = e =>
        {
            if (_project is null) return;
            _project = _project.WithIgnoredDuplicate(e);
            IsModified = true;
        };
        Action<IgnoredDuplicate> unignore = e =>
        {
            if (_project is null) return;
            _project = _project.WithoutIgnoredDuplicate(e);
            IsModified = true;
        };

        // Base-game comparison (issue #14): only offered with a game loaded. The provider
        // is captured now, so a game switch while the window is open cannot change what an
        // in-flight load reads.
        Func<CancellationToken, Task<IReadOnlyList<VanillaLine>>>? loadVanilla = null;
        if (_provider is { } provider)
            loadVanilla = ct => Task.Run(() => VanillaLineCorpus.Load(provider, ct), ct);

        // The closure reads the current fields, so Refresh in the open window picks
        // up saves made later in the session.
        return new TextTagValidationViewModel(
            () => _project is null
                ? []
                : ProjectTextTagScanner.Scan(
                    _project, _activeGameId, _provider?.Language ?? "", spell: spell),
            addWord: store is null ? null : store.AddWord,
            staleScan: staleScan,
            prune: prune,
            canCheckGameFiles: _provider is not null,
            primaryLanguage: _provider?.Language ?? "",
            dupScan: dupScan,
            ignoredList: ignoredList,
            ignore: ignore,
            unignore: unignore,
            navigate: NavigateToFoundNode,
            duplicateOptions: new DuplicateScanOptions(
                AppSettings.NearDuplicateThreshold,
                AppSettings.DuplicateIncludeFemaleText,
                AppSettings.DuplicateIncludeOtherLanguages,
                AppSettings.DuplicateIncludeBaseGame),
            persistDuplicateOptions: o =>
            {
                AppSettings.NearDuplicateThreshold         = o.NearThreshold;
                AppSettings.DuplicateIncludeFemaleText     = o.IncludeFemaleText;
                AppSettings.DuplicateIncludeOtherLanguages = o.IncludeOtherLanguages;
                AppSettings.DuplicateIncludeBaseGame       = o.IncludeBaseGame;
            },
            loadVanilla: loadVanilla);
    }

    /// Builds a conversation-name → live-node-ID-set resolver for the likely-stale
    /// pass. The open conversation uses the live canvas snapshot; others are
    /// reconstructed vanilla + patch (conflicts ignored — display semantics),
    /// returning null (skip) when a conversation can't be loaded. Mirrors
    /// VoOrphanScanner's per-conversation resolution.
    private Func<string, IReadOnlySet<int>?> BuildEffectiveNodeIds()
    {
        var provider = _provider!;
        var openConv = Canvas.ConversationName;
        var openIds  = Canvas.BuildSnapshot().Nodes.Select(n => n.NodeId).ToHashSet();

        return convName =>
        {
            if (!string.IsNullOrEmpty(openConv) && convName == openConv)
                return openIds;
            if (_project is null || !_project.Patches.TryGetValue(convName, out var patch))
                return null;
            try
            {
                var file     = provider.FindConversation(convName);
                var baseSnap = file is not null
                    ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                    : new ConversationEditSnapshot([]);
                var applied  = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                return applied.Nodes.Select(n => n.NodeId).ToHashSet();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Stale-data scan: could not load '{convName}': {ex.Message}");
                return null;
            }
        };
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

    /// MRU list of recently opened/created/saved-as projects (newest first), read
    /// through to AppSettings. The File ▸ Recent Projects submenu binds to the derived
    /// RecentProjectsMenuItems collection (below); both are refreshed together by
    /// RefreshRecentProjects() after every mutation.
    public IReadOnlyList<string> RecentProjects => AppSettings.RecentProjects;

    /// Data-bound source for the File ▸ Recent Projects submenu. NEVER empty — an
    /// empty-state placeholder stands in when there are no recents, which is what
    /// keeps the parent MenuItem a real (openable) submenu rather than a leaf.
    /// (An empty ItemsSource would make the submenu never open — the bug that
    /// replaced the earlier SubmenuOpened code-behind approach.)
    public ObservableCollection<RecentProjectMenuItem> RecentProjectsMenuItems { get; } = new();

    // Avalonia treats '_' in a Header as an access-key mnemonic and eats it, so a
    // file like "my_project" would display as "myproject". Double the underscores.
    private static string EscapeMenuMnemonic(string s) => s.Replace("_", "__");

    private void RefreshRecentProjects()
    {
        OnPropertyChanged(nameof(RecentProjects));
        RecentProjectsMenuItems.Clear();
        var recents = AppSettings.RecentProjects;
        if (recents.Count == 0)
        {
            RecentProjectsMenuItems.Add(new RecentProjectMenuItem
            {
                Header = Loc.Get("Menu_RecentProjectsEmpty"),
                IsEnabled = false,
            });
            return;
        }
        foreach (var path in recents)
        {
            RecentProjectsMenuItems.Add(new RecentProjectMenuItem
            {
                Header = EscapeMenuMnemonic(Path.GetFileNameWithoutExtension(path)),
                ToolTip = PathDisplay.ForDisplay(path),   // display only; CommandParameter keeps the real path
                Command = OpenRecentProjectCommand,
                CommandParameter = path,
            });
        }
        RecentProjectsMenuItems.Add(new RecentProjectMenuItem
        {
            Header = Loc.Get("Menu_ClearRecentProjects"),
            ToolTip = Loc.Get("ToolTip_ClearRecentProjects"),
            Command = ClearRecentProjectsCommand,
        });
    }

    [RelayCommand(CanExecute = nameof(IsEditingMode))]
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
        FindInProjectCommand.NotifyCanExecuteChanged();
        BatchReplaceCommand.NotifyCanExecuteChanged();
        ShowRepDispositionBalanceWindowCommand.NotifyCanExecuteChanged();
        BrowseSpeakerLinesCommand.NotifyCanExecuteChanged();
        DialogProjectSerializer.SaveToFile(path, _project!);
        AppSettings.LastProjectPath = path;
        AppSettings.AddRecentProject(path);
        RefreshRecentProjects();
        CurrentProjectName = name;
        AppLog.Info($"New project: {path}");
        StatusText = Loc.Format("Status_ProjectNew", name);
        ReloadOpenConversationForNewProject();
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
        StatusText = Loc.Format("Status_RecentProjectMissing", PathDisplay.ForDisplay(path));
        if (ConfirmRemoveMissingProject is null) return;
        if (await ConfirmRemoveMissingProject(path))
        {
            AppSettings.RemoveRecentProject(path);
            RefreshRecentProjects();
        }
    }

    [RelayCommand]
    private void ClearRecentProjects()
    {
        AppSettings.ClearRecentProjects();
        RefreshRecentProjects();
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
        ClearCanvasAfterClose();
        AppSettings.LastProjectPath = null;
    }

    private void ClearCanvasAfterClose()
    {
        Canvas.Clear();
        Detail.Clear();
        CurrentConversationName = null;
        _currentFile = null;
        OnPropertyChanged(nameof(CanValidateVO));
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
            CloseProjectCore(Loc.Format("Status_ProjectNotOnBranch", PathDisplay.ForDisplay(path)));
            return;
        }

        InvalidateAttribution();       // HEAD moved → stale blame
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
        _deferredOpenPath = null;   // reading-mode deferrals belonged to the project just closed
        ClearPendingConflict();
        Detail.ProjectPath  = null;
        Canvas.ProjectPath  = null;
        OnPropertyChanged(nameof(CanExportModBundle));
        BatchImportVoAllCommand.NotifyCanExecuteChanged();   // gate depends on _projectPath
        FindInProjectCommand.NotifyCanExecuteChanged();
        BatchReplaceCommand.NotifyCanExecuteChanged();
        ShowRepDispositionBalanceWindowCommand.NotifyCanExecuteChanged();
        BrowseSpeakerLinesCommand.NotifyCanExecuteChanged();
        CurrentProjectName = null;
        IsModified = false;        // nothing open → not dirty
        InvalidateAttribution();   // force attribution rebuild next time
        StatusText = statusText;
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Drops the cached blame so the next lookup rebuilds. Call whenever HEAD moves under
    /// an open project — the cache assumes HEAD is stable for the life of the project,
    /// which stops being true the moment the app commits (#12).
    /// </summary>
    public void InvalidateAttribution() => _attributionPath = null;

    /// <summary>
    /// A Branches window VM for the open project, with the host callbacks that tie it to
    /// this project already attached; null when no project is open. The View only adds
    /// its dialogs. The wiring lives here rather than in MainWindow's code-behind so it is
    /// testable: the HeadMoved line was once a view-only one-liner that no test reached,
    /// and dropping it would have silently brought back #12 (see #21).
    /// </summary>
    public BranchesViewModel? CreateBranchesViewModel(GitBranchService service) =>
        _projectPath is null ? null : new BranchesViewModel(service, _projectPath)
        {
            EnsureNoUnsavedEdits  = EnsureNoUnsavedEditsAsync,
            ReloadProjectFromDisk = ReloadCurrentProjectFromDisk,
            // A commit moves HEAD without touching the working tree, so there is nothing
            // to reload — only the HEAD-derived blame cache to drop (#12).
            HeadMoved             = InvalidateAttribution,
        };

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

    /// Seam for the autosave restore offer. Gets every autosave copy, newest first (issue 36;
    /// each says whether it is the per-user fallback copy, issue 37), and returns the user's
    /// decision: restore one copy, discard them all, or decide later, which leaves the project
    /// closed and every copy in place (issue 236). Null in unit tests that don't exercise it — a newer
    /// sidecar is then left untouched (never destroy recovery data because no dialog was
    /// wired) and the saved file loads normally. The View wires AutosaveRestoreDialog.
    public Func<IReadOnlyList<AutosaveGeneration>, Task<AutosaveDecision>>? ConfirmRestoreAutosave { get; set; }

    private async Task LoadProjectAsync(string path, bool offerDeferred)
    {
        // Crash recovery (spec 2026-07-12 §4): a sidecar newer than the project file
        // holds work lost to a crash/kill — offer to restore it before loading.
        _deferredOpenPath = null;   // this open supersedes any earlier deferral
        var recovery = AutosaveRecovery.Check(path);
        if (IsReadingMode)
        {
            // Reading mode writes nothing (issue 234): no prompt, and no deleting a stale copy.
            // A newer copy is offered when the user switches to editing.
            if (recovery.State == AutosaveState.Newer)
                _deferredOpenPath = path;
        }
        else if (recovery.State == AutosaveState.Stale)
        {
            AutosaveRecovery.TryDelete(path); // save happened through another route
        }
        else if (recovery.State == AutosaveState.Newer && ConfirmRestoreAutosave is not null)
        {
            // Check found a copy newer than the saved project; offer every copy, newest first, so
            // an earlier one is still reachable if the newest is damaged (issue 36).
            var generations = AutosaveRecovery.ListGenerations(path);
            if (generations.Count == 0)
                generations = [new AutosaveGeneration(recovery.SidecarPath, recovery.SidecarTimeUtc!.Value, recovery.IsFallback)];
            var decision = await ConfirmRestoreAutosave(generations);
            if (decision == AutosaveDecision.Later)
            {
                // Issue 236: leave the project closed. Opening the saved file instead would put
                // the copies at risk: saving deletes them and each autosave tick rotates them.
                // Nothing is touched, so the next open (or launch) offers them again.
                StatusText = Loc.Format("Status_AutosaveKeptNotOpened", Path.GetFileName(path));
                return;
            }
            if (decision is AutosaveDecision.RestoreChoice { Copy: var chosen })
            {
                try
                {
                    var recovered = DialogProjectSerializer.LoadFromFile(chosen.Path);
                    FinishLoad(recovered, path);
                    IsModified = true;   // recovered state is unsaved until an explicit save
                    StatusText = Loc.Get("Status_AutosaveRestored");
                    return;              // sidecar is KEPT until that save (double-crash protection)
                }
                catch (UnsupportedSchemaVersionException ex)
                {
                    // A newer editor's unsaved work: keep the sidecar (it isn't corrupt) and
                    // load the saved file, which may be refused in its own right.
                    AppLog.Warn($"Autosave sidecar for '{path}' is from a newer version, kept: {ex.Message}");
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Autosave restore failed for '{path}': {ex.Message} — loading saved file");
                    AutosaveRecovery.TryDelete(path); // corrupt sidecar: don't re-offer forever
                }
            }
            else
            {
                AutosaveRecovery.TryDelete(path);    // confirmed Discard — respect it
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
            if (_deferredOpenPath is not null)
                StatusText = Loc.Format("Status_ProjectOpenedReadOnlyDeferred", loaded.Name);
        }
        catch (UnsupportedSchemaVersionException ex)
        {
            await RefuseNewerFormatAsync(ex, path);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to open project '{path}'", ex);
            StatusText = Loc.Format("Status_ProjectOpenError", PathDisplay.ForDisplay(path), ex.Message);
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
        catch (UnsupportedSchemaVersionException ex)
        {
            await RefuseNewerFormatAsync(ex, path);
            return;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Git-conflicted project '{path}' has unparseable sides: {ex.Message}");
            StatusText = Loc.Format("Status_ProjectGitConflictUnparseable", PathDisplay.ForDisplay(path));
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
            StatusText = Loc.FormatCount("Status_ProjectGitConflictDetected", conflicts.Count, PathDisplay.ForDisplay(path));
            return;
        }

        if (IsReadingMode)
        {
            // Reading mode: the resolved project would be saved back over the file, so open the
            // local side as it is and offer the modal once the user switches to editing.
            FinishLoad(mine, path);
            _pendingConflictPath        = path;
            HasPendingConflictResolution = true;
            StatusText = Loc.Format("Status_ProjectOpenedReadOnlyDeferred", mine.Name);
            return;
        }

        if (offerDeferred)   // startup: don't auto-show the modal — offer a Resolve… action
        {
            _pendingConflictPath        = path;
            HasPendingConflictResolution = true;
            StatusText = Loc.FormatCount("Status_ProjectGitConflictPending", conflicts.Count, PathDisplay.ForDisplay(path));
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
        FindInProjectCommand.NotifyCanExecuteChanged();
        BatchReplaceCommand.NotifyCanExecuteChanged();
        ShowRepDispositionBalanceWindowCommand.NotifyCanExecuteChanged();
        BrowseSpeakerLinesCommand.NotifyCanExecuteChanged();
        AppSettings.LastProjectPath = path;
        AppSettings.AddRecentProject(path);
        RefreshRecentProjects();
        CurrentProjectName = loaded.Name;
        AppLog.Info($"Opened project: {path}");
        // Before the reload, so a warning it raises (the patch no longer matches the game files,
        // the text file is damaged) is what stays on screen; it is the only place it is shown.
        StatusText = Loc.FormatCount("Status_ProjectOpened", loaded.Patches.Count, loaded.Name);
        ReloadOpenConversationForNewProject();
    }

    /// A conversation already on the canvas shows the previous project's text (issue 234: opening
    /// a project, a deferred autosave Restore and a deferred merge all swap the project under
    /// it; issue 327: so do New Project and Merge Projects). Discard clears IsModified, so after
    /// the unsaved-edits guard this is dirty only when the user chose Save and the save failed
    /// (SaveAndProceed proceeds regardless), or when Create Sample Project, which has no guard,
    /// swaps a project in: those edits are wanted and the reload would drop them, so they are
    /// left on the canvas.
    private void ReloadOpenConversationForNewProject()
    {
        if (_currentFile is { } open && _provider is not null && !IsModified)
        {
            // A conversation the previous project created, which this one does not have, has
            // nothing left to show: clear the canvas as closing does, instead of a load error.
            if (ConversationExistsFor(open))
                ReloadCurrentConversationView();
            else
                ClearCanvasAfterClose();
        }
    }

    /// True when the open project created the conversation, or the game has it (on disk, or
    /// known to the game data, which the cheap file check misses for some providers).
    private bool ConversationExistsFor(ConversationFile file)
    {
        if (_project?.IsNewConversation(file.Name) == true || File.Exists(file.ConversationPath))
            return true;
        try
        {
            return _provider?.FindConversation(file.Name) is not null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not look up conversation '{file.Name}' in the game data: {ex.Message}");
            return false;
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
        // Cards are laid out by their estimated height (issue 82), from the text each one shows.
        var importedText = imported.Texts
            .GroupBy(t => t.NodeId).ToDictionary(g => g.Key, g => g.First().DefaultText);
        AutoLayoutService.Apply(layoutNodes,
            id => CardMetrics.EstimateHeight(importedText.GetValueOrDefault(id)),
            (id, x, y) => layout[id] = new LayoutPoint(x, y));

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

    private bool CanCreateConversation() => IsEditingMode && _provider is not null && _project is not null;

    [RelayCommand(CanExecute = nameof(IsProjectLoadedForEditing))]
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

        // Issue 172: only a conversation the project created can be renumbered; in a game
        // conversation the patch would see a delete plus an add.
        Canvas.CanChooseStartNode = true;

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

        // Drain a Find-in-Project navigation target, same as LoadConversationFile:
        // a match in a new conversation routes here, and its node lives in the
        // reconstructed-from-patch canvas above.
        if (_pendingSelectNodeId is int pending)
        {
            _pendingSelectNodeId = null;
            SelectNodeById(pending);
        }
    }

    /// Periodic autosave (wired to a DispatcherTimer in MainWindow, cadence from
    /// AppSettings.AutosaveIntervalSeconds). Writes a sidecar next to the project file
    /// while there are unsaved changes; never touches the real file, never clears
    /// IsModified, never throws. Older sidecars rotate down to
    /// AppSettings.AutosaveGenerations, so a corrupt newest autosave is not the only
    /// copy of the work (issue #11).
    /// Spec: docs/superpowers/specs/2026-07-12-autosave-design.md.
    public void AutosaveTick()
    {
        if (_project is null || _projectPath is null || !IsModified) return;
        DialogProject? folded = null;   // kept for the fallback write, so the canvas is not folded twice
        try
        {
            folded = BuildFoldedProject(consumeRemaps: false) ?? _project;
            WriteAutosave(folded, fallback: false);
            _autosaveFallbackAnnouncedFor = null;   // the project's folder works (again)
        }
        catch (Exception ex) when (AutosaveRecovery.IsLocationFailure(ex))
        {
            // Issue 37: the project's folder can't be written (read-only media, a locked share,
            // no permission). Without a fallback every tick failed silently and a crash lost
            // everything since the last save. Write to the per-user folder instead, and say so
            // once in the status bar, so the user knows their recovery copy lives elsewhere.
            AppLog.Warn($"Autosave next to the project failed ({ex.Message}); using the fallback folder");
            try
            {
                WriteAutosave(folded ?? BuildFoldedProject(consumeRemaps: false) ?? _project, fallback: true);
                if (_autosaveFallbackAnnouncedFor != _projectPath)
                {
                    _autosaveFallbackAnnouncedFor = _projectPath;
                    StatusText = Loc.Format("Status_AutosaveFallback", PathDisplay.ForDisplay(AutosaveRecovery.FallbackRoot));
                }
            }
            catch (Exception fallbackEx)
            {
                AppLog.Warn($"Autosave to the fallback folder failed too: {fallbackEx.Message}");
            }
        }
        catch (Exception ex)
        {
            // Autosave must never interrupt writing — log and carry on.
            AppLog.Warn($"Autosave failed: {ex.Message}");
        }
    }

    /// The project the fallback status message was last shown for, so it is said once, not
    /// on every tick (issue 37).
    private string? _autosaveFallbackAnnouncedFor;

    private void WriteAutosave(DialogProject project, bool fallback)
    {
        // Rotate first: this frees generation 1 for the write below.
        AutosaveRecovery.Rotate(_projectPath!, AppSettings.AutosaveGenerations, fallback);
        var sidecar = AutosaveRecovery.GenerationPath(_projectPath!, 1, fallback);
        if (fallback) Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
        DialogProjectSerializer.SaveToFile(sidecar, project);
        AppLog.Info($"Autosaved to {sidecar}");
    }

    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private void SaveProject()
    {
        if (_project is null || _projectPath is null) return;
        // Defence in depth (issue 234): the open file still holds an unresolved git conflict, so
        // writing this project over it would lose the other branch's version. Every save path
        // (Ctrl+S, Save in an unsaved-changes prompt, Save As to the same file) comes through
        // here, and the edits stay unsaved, so a prompt treats it as a failed save.
        if (RefuseWriteWhileConflictPending(nameof(SaveProject))) return;
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
        IsEditingMode && _project is not null && _projectPath is not null && IsModified;

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
        FindInProjectCommand.NotifyCanExecuteChanged();
        BatchReplaceCommand.NotifyCanExecuteChanged();
        ShowRepDispositionBalanceWindowCommand.NotifyCanExecuteChanged();
        BrowseSpeakerLinesCommand.NotifyCanExecuteChanged();
        AppSettings.LastProjectPath = path;
        AppSettings.AddRecentProject(path);
        RefreshRecentProjects();
        CurrentProjectName = _project!.Name;
        AppLog.Info($"Project saved as: {path}");
        StatusText = voCopyError is null
            ? Loc.Format("Status_ProjectSavedAs", _project!.Name)
            : Loc.Format("Status_SaveAsVoCopyFailed", _project!.Name, voCopyError.Message);
        ConversationSaved?.Invoke();
    }

    private bool CanSaveProjectAs() =>
        IsEditingMode && _project is not null && _projectPath is not null;

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

    /// References elsewhere in the project that still name the renumbered node's old ID
    /// (issue 172); counted for the status bar, never rewritten.
    private int StartNodeReferences(StartNodeChangedEventArgs e) =>
        _project is null ? 0
        : StartNodeReferenceScanner.Count(_project, Canvas.ConversationName, e.OldNodeId, e.OldVoPath);

    /// With a conversation open, folds the canvas edits into its patch on _project.
    /// With no conversation open (e.g. a freshly conflict-merged project), the
    /// in-memory _project is already complete — no-op.
    private void FoldCanvasIntoProject()
    {
        if (BuildFoldedProject(consumeRemaps: true) is { } folded) SetProject(folded);
    }

    /// The project with the canvas edits folded in, without touching _project. Null when there
    /// is no conversation to fold. Autosave serialises this copy and leaves _project alone
    /// (issue 234): _project must stay the last *saved* state, or Discard (which reverts by
    /// reloading the conversation from _project's patch) would re-apply an autosaved edit and
    /// a later Save would persist it. Only Save/Save As commit the fold into _project.
    private DialogProject? BuildFoldedProject(bool consumeRemaps)
    {
        if (_currentFile is null || Canvas.BaseSnapshot is null) return null;

        var current = Canvas.BuildSnapshot();
        var patch   = DiffEngine.Diff(_currentFile.Name, Canvas.BaseSnapshot, current, _provider!.Language);
        patch = patch with { NodeComments = Canvas.NodeComments };

        // WithPatch replaces the stored patch wholesale, but the diff only knows
        // the canvas language — carry over imported translations for every other
        // language, or they would be silently erased on each save.
        // A start-node renumber (issue 172) moved nodes the stored translations still key by
        // their old IDs; re-key them before carrying them over, or the stale-data filter below
        // drops them.
        var prior  = _project!.Patches.GetValueOrDefault(_currentFile.Name);
        // A peek for autosave: the remaps stay pending for the save that really commits them.
        var remaps = consumeRemaps ? Canvas.TakePendingRemaps() : Canvas.PeekPendingRemaps();
        if (prior is not null && remaps.Count > 0)
            prior = prior with { Translations = NodeIdRemap.Apply(prior.Translations, remaps) };
        patch = patch.CarryingTranslationsFrom(prior, _provider.Language);

        // Prevention: the canvas holds the full effective conversation, so its live
        // node-ID set is authoritative (game-folder-free). Drop comment/translation
        // entries for nodes that no longer exist, so deleting a node cannot leave
        // stale patch data behind (see the Stale Patch Data Hygiene spec).
        var liveIds = current.Nodes.Select(n => n.NodeId).ToHashSet();
        // A language whose only surviving entries were on the deleted node must
        // lose the key entirely, not persist as an empty list — mirrors
        // StaleDataPruner's (the reactive half's) "kept.Count > 0" behaviour, so
        // both halves of the feature agree and empty keys don't get carried
        // forward forever by the translation-merge block above.
        var filteredTranslations = new Dictionary<string, IReadOnlyList<NodeTranslation>>();
        foreach (var (lang, entries) in patch.Translations)
        {
            var kept = entries.Where(t => liveIds.Contains(t.NodeId)).ToList();
            if (kept.Count > 0)
                filteredTranslations[lang] = kept;
        }
        patch = patch with
        {
            NodeComments = patch.NodeComments
                .Where(kv => liveIds.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            Translations = filteredTranslations,
        };

        var layout      = Canvas.GetCurrentLayout();
        var annotations = Canvas.GetCurrentAnnotations();
        return _project!.WithPatch(patch).WithLayout(_currentFile.Name, layout)
            .WithAnnotations(_currentFile.Name, annotations);
    }

    // ── Apply from diff ───────────────────────────────────────────────────

    /// Brings an applied project (from the compare window) into the live editor:
    /// guards on unsaved changes, snapshots the prior state, swaps + saves.
    public async Task ApplyFromDiff(DialogProject applied)
    {
        if (_projectPath is null) return;
        // Before the save prompt: its Save would be refused too, and the write below would not.
        if (RefuseWriteWhileConflictPending(nameof(ApplyFromDiff))) return;

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
        // Before the undo is used up, so it is still there once the conflict is resolved.
        if (RefuseWriteWhileConflictPending(nameof(UndoApply))) return;
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

    private bool CanUndoApply() => IsEditingMode && _preApplyProject is not null;

    // ── Merge projects ────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanMergeProjects))]
    private async Task MergeProjects()
    {
        if (_project is null) return;
        // Before the unsaved-changes prompt and the picker, so neither is shown for nothing.
        if (RefuseWriteWhileConflictPending(nameof(MergeProjects))) return;

        // Save or Discard runs first, so the canvas holds no edits the reload below would drop.
        if (!await EnsureNoUnsavedEditsAsync()) return;

        var paths = await _filePicker.PickOpenFilesAsync(
            Loc.Get("Dialog_MergeProjects"),
            [".dialogproject"],
            Loc.Get("FileType_DialogProject"));
        if (paths.Count == 0) return;

        string? loading = null;
        try
        {
            var merged = _project;
            foreach (var path in paths)
            {
                loading = path;
                var other = DialogProjectSerializer.LoadFromFile(path);
                merged = merged.MergeWith(other);
            }
            SetProject(merged);
            // Save immediately — the merged patches are already computed, no canvas diff needed.
            // The canvas is reloaded below (issue 327), or the next Save would re-diff the
            // pre-merge canvas over the merged patch.
            DialogProjectSerializer.SaveToFile(_projectPath!, merged);
            AppLog.Info($"Merged {paths.Count} project(s) into '{merged.Name}'");
            StatusText = Loc.FormatCount("Status_MergeComplete", paths.Count, merged.Name);
            ReloadOpenConversationForNewProject();
        }
        catch (UnsupportedSchemaVersionException ex)
        {
            // Every file is loaded before SetProject / SaveToFile, so the open project is untouched.
            await RefuseNewerFormatAsync(ex, loading!);
        }
        catch (Exception ex)
        {
            AppLog.Error("Merge projects failed", ex);
            StatusText = Loc.Format("Status_MergeError", ex.Message);
            ReportError?.Invoke(ex);
        }
    }

    private bool CanMergeProjects() => IsEditingMode && _project is not null && _projectPath is not null;

    // ── Export / Import for translation ──────────────────────────────────

    private bool IsProjectLoaded() => _project is not null;

    private bool IsProjectLoadedForEditing() => IsProjectLoaded() && IsEditingMode;

    [RelayCommand(CanExecute = nameof(IsProjectLoadedForEditing))]
    private async Task ExportForTranslation()
    {
        if (_project is null) return;
        // The export reads the project as last saved, so ask about the canvas's unsaved edits
        // first, or it would silently leave them out (issue 326).
        if (!await EnsureNoUnsavedEditsAsync() || _project is null) return;
        var fmt = ParseFormat(AppSettings.DefaultLocalizationFormat);
        var ext = FormatExtension(fmt);
        var allTypes = new (string, string)[] { (".csv", "CSV"), (".json", "JSON"), (".xlf", "XLIFF") };
        var ordered = allTypes.OrderByDescending(t => t.Item1 == ext).ToArray();
        var path = await _filePicker.PickSaveFileAsync(
            Loc.Get("Localization_ExportPickerTitle"),
            "export" + ext,
            ordered);
        if (path is null) return;
        var lang = await (RequestLanguageCode?.Invoke(Loc.Get("Localization_SourceLanguagePrompt"), _provider?.Language)
                          ?? Task.FromResult<string?>(_provider?.Language));
        if (lang is null) return;
        var count = _project.Patches.Values
            .Where(p => p.Translations.ContainsKey(lang))
            .Sum(p => p.Translations[lang].Count);
        LocalizationExportService.Export(_project, path, fmt, lang);
        StatusText = Loc.Format("Localization_StatusExported", count, Path.GetFileName(path));
    }

    [RelayCommand(CanExecute = nameof(IsProjectLoadedForEditing))]
    private async Task ImportTranslation()
    {
        if (_project is null) return;
        var path = await _filePicker.PickOpenFileAsync(
            Loc.Get("Localization_ImportPickerTitle"),
            new[] { (".csv", "CSV"), (".json", "JSON"), (".xlf", "XLIFF") });
        if (path is null) return;
        var fmt          = DetectFormat(path);
        var suggestedLang = LocalizationImportService.DetectLanguage(path, fmt,
            ex => AppLog.Warn($"Language auto-detect failed for '{path}': {ex.Message}"));
        var lang = await (RequestLanguageCode?.Invoke(Loc.Get("Localization_TargetLanguagePrompt"), suggestedLang)
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

            _conversationNamesById = new Dictionary<string, string>();
            foreach (var (kind, entries) in provider.LoadGameDataNames())
            {
                if (kind == "Conversation")
                    _conversationNamesById = entries
                        .Where(e => !string.IsNullOrEmpty(e.Id))
                        .GroupBy(e => e.Id)
                        .ToDictionary(g => g.Key, g => g.First().Name);

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
            Canvas.ActiveGameId = provider.GameId;
            RebuildConditionSearch(provider.GameId);   // builds the shell-level condition-search dock
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
            // PoE1 voice preview (issue 4): index resources.assets in the background.
            Poe1VoiceService.Clear();
            if (string.Equals(provider.GameId, "poe1", StringComparison.OrdinalIgnoreCase))
                _ = Poe1VoiceService.Rebuild(path);   // never throws; failures are logged and leave it not ready
            OnPropertyChanged(nameof(CanValidateVO));
            BatchImportVoAllCommand.NotifyCanExecuteChanged();   // gate depends on game folder + id
            FindInProjectCommand.NotifyCanExecuteChanged();
            BatchReplaceCommand.NotifyCanExecuteChanged();
            ShowRepDispositionBalanceWindowCommand.NotifyCanExecuteChanged();
            BrowseSpeakerLinesCommand.NotifyCanExecuteChanged();
            AvailableLanguages = provider.AvailableLanguages;
            SelectedLanguage   = AppSettings.PickLanguage(AvailableLanguages, AppSettings.LastLanguage);
            Browser.Load(provider, _project?.NewConversations);
            AppLog.Info($"Loaded {provider.GameName} from {path}");
            StatusText = Loc.Format("Status_FolderLoaded", provider.GameName, PathDisplay.ForDisplay(path));
            CreateSampleProjectCommand.NotifyCanExecuteChanged();

            // Reading mode writes nothing (issue 234): the backup offer and top-up write to the
            // backup folder, so they wait for the switch to editing.
            if (IsReadingMode)
                _deferredBackupPath = path;
            else
            {
                _deferredBackupPath = null;
                RunBackupStep(path, provider);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to initialise game data from {path}", ex);
            StatusText = Loc.Format("Status_LoadError", PathDisplay.ForDisplay(path), ex.Message);
            ReportError?.Invoke(ex);
        }
    }

    // ── Create Sample Project (Help menu) ─────────────────────────────────
    private bool CanCreateSample() => IsEditingMode && _provider is not null;

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
            build = service.BuildSample(_provider, SampleTextFactory.FromResources());
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

    [RelayCommand(CanExecute = nameof(IsEditingMode))]
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
    private void StartGuidedTour() => Tour.Start(Mode);

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

    [RelayCommand(CanExecute = nameof(IsEditingMode))]
    private void TagReference()
        => ShowTagReference?.Invoke(new TagReferenceViewModel(_activeGameId));

    // ── Backup offer (first time per game folder) ─────────────────────────
    private void RunBackupStep(string path, IGameDataProvider provider)
    {
        if (!AppSettings.IsKnownGameDirectory(path))
            _ = OfferBackupAsync(path);
        else
            _ = TopUpBackupAsync(path, provider);
        AppSettings.MarkGameDirectoryKnown(path);
    }

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
            StatusText = Loc.Format("Status_BackupComplete", PathDisplay.ForDisplay(backupRoot));
        }
        catch (Exception ex)
        {
            AppLog.Error("Backup failed", ex);
            StatusText = Loc.Format("Status_BackupError", ex.Message);
            ReportError?.Invoke(ex);
        }
    }

    private static Task RunProviderBackupAsync(string backupRoot, IGameDataProvider provider)
        => FullBackup.TakeAsync(provider, backupRoot, default);

    // A backup taken before issue 198 has none of PoE2's DLC data folders; adding them the next
    // time the game folder is opened still captures the originals (nothing wrote them before).
    private static async Task TopUpBackupAsync(string gameDirectory, IGameDataProvider provider)
    {
        // A Test Patch waiting for F6 means the game folder holds the test's writes.
        if (AppSettings.GetPendingRestores() is { Count: > 0 }) return;
        if (FullBackup.Latest(AppSettings.GetBackupPath(gameDirectory) ?? string.Empty) is not { } latest) return;
        try
        {
            var added = await FullBackup.TopUpAsync(provider, latest, default);
            if (added > 0) AppLog.Info($"Backup {latest} topped up with {added} DLC folder(s)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Could not add the DLC folders to the backup {latest}: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Fire-and-forget: anything else would vanish with the discarded task.
            AppLog.Warn($"Adding the DLC folders to the backup {latest} failed: {ex}");
        }
    }

    // Appends this test's writes to the journal next to the full backup (issue 118), so
    // Restore Full Backup can later tell them from a game update. No backup, no journal:
    // there is nothing for it to protect. A failure here must not fail the test itself.
    private void RecordEditorWrites(IReadOnlyDictionary<string, string?> hashBefore)
    {
        var backupPick = AppSettings.GetBackupPath(_currentGameDirectory);
        if (backupPick is null) return;
        try
        {
            var writes = hashBefore
                .Select(kv => (kv.Key, kv.Value, After: FileHash.Of(kv.Key)))
                .Where(x => x.After is not null && x.After != x.Value)
                .Select(x => new EditorWrite(x.Key, x.Value, x.After!))
                .ToList();
            EditorWriteJournal.Append(Path.Combine(backupPick, EditorWriteJournal.FileName), writes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Could not record Test Patch writes for Restore Full Backup: {ex.Message}");
        }
    }

    // The live counterparts of the snapshot's files that the patcher manages. An unreadable
    // patcher manifest counts as managing all of them (PatcherBackupInfo), so they're skipped.
    private static IReadOnlySet<string> PatcherManaged(string gameDir, string snapshotRoot, string liveRoot)
    {
        var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (PatcherBackupInfo.TryOpen(gameDir) is not { } patcher || !Directory.Exists(snapshotRoot)) return none;
        var livePaths = Directory.EnumerateFiles(snapshotRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetFullPath(Path.Combine(liveRoot, Path.GetRelativePath(snapshotRoot, f))));
        return patcher.CoveredPaths(livePaths).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ── Restore backup ────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(IsEditingMode))]
    private async Task RestoreBackup()
    {
        var backupPick = AppSettings.GetBackupPath(_currentGameDirectory);
        if (backupPick is null)
        {
            StatusText = Loc.Get("Status_NoBackupFound");
            return;
        }

        // An active test has its own exact undo; restoring under it would leave F6 to put
        // the test's files back on top afterwards.
        if (AppSettings.GetPendingRestores() is { Count: > 0 })
        {
            StatusText = Loc.Get("Status_FullRestoreTestActive");
            return;
        }

        var backupRoot = FullBackup.Latest(backupPick);
        if (backupRoot is null) { StatusText = Loc.Get("Status_NoBackupFound"); return; }

        StatusText     = Loc.Get("Status_RestoreInProgress");
        try
        {
            var writes   = EditorWriteJournal.Load(Path.Combine(backupPick, EditorWriteJournal.FileName));
            var gameDir  = _currentGameDirectory;
            // Each snapshot folder goes back into its own language's folder, never the one
            // currently selected (issue 123).
            var plan     = FullBackup.PlanRestore(_provider!, backupRoot,
                string.Equals(_activeGameId, "poe2", StringComparison.OrdinalIgnoreCase) ? VoPathResolver.VoicesRoot(gameDir) : null);
            var pairs    = plan.Pairs;

            var results = await Task.Run(() => pairs.Select(p =>
                FullBackupRestore.Restore(p.Snapshot, p.Live, writes, PatcherManaged(gameDir, p.Snapshot, p.Live)))
                .ToList());

            var restored = results.Sum(r => r.Restored.Count);
            var skipped  = results.SelectMany(r => r.Skipped).ToList();
            foreach (var s in skipped)
                AppLog.Warn($"Restore Full Backup left {s.Path} as is ({s.Reason}): it differs from the " +
                            $"backup of {Path.GetFileName(backupRoot)} and the editor didn't write it");
            AppLog.Info($"Backup {Path.GetFileName(backupRoot)} restored: {restored} file(s), {skipped.Count} left as is");
            StatusText = skipped.Count == 0
                ? Loc.FormatCount("Status_FullRestoreComplete", restored)
                : Loc.FormatCount("Status_FullRestoreComplete", restored) + " "
                  + Loc.FormatCount("Status_FullRestoreSkipped", skipped.Count);
            if (plan.TextLanguageUnknown)
            {
                AppLog.Warn($"Backup {Path.GetFileName(backupRoot)} predates per-language backups and its text " +
                            "matches no installed language; only conversations were restored");
                StatusText += " " + Loc.Get("Status_FullRestoreLanguageUnknown");
            }
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

    private bool CanSave() => IsEditingMode && _provider is not null && _currentFile is not null && IsModified;

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

        // Already in test mode (issue #66): a second F5 would back up the *patched* files
        // as the new "originals" and overwrite the manifest, so F6 would then restore the
        // test state permanently. The player must restore (F6) before testing again.
        if (AppSettings.GetPendingRestores() is { Count: > 0 })
        {
            AppLog.Warn("Test Patch refused: a previous test has not been restored yet");
            StatusText = Loc.Get("Status_TestPatchAlreadyActive");
            return;
        }

        // Mods installed with the Patch Manager / dialog-patcher manage some of these files
        // (issue #76): testing now would patch the modded versions, and the patcher would see
        // this test's writes as outside changes. Ask once; the forced re-run doesn't ask again.
        if (!ignoreConflicts && PatcherBackupInfo.TryOpen(_currentGameDirectory) is { } patcher)
        {
            var paths = _project.Patches.Keys
                .Select(name => _provider.FindConversation(name))
                .Where(f => f is not null)
                .SelectMany(f => new[] { f!.ConversationPath, _provider.GetStringTablePath(f) })
                .ToList();
            var covered = patcher.CoveredPaths(paths).Count;
            if (covered > 0 && ConfirmTestOverPatcherMods is not null
                && !await ConfirmTestOverPatcherMods(covered))
            {
                AppLog.Warn($"Test Patch cancelled: {covered} file(s) are managed by the patcher");
                StatusText = Loc.Get("Status_TestPatchCancelledPatcherMods");
                return;
            }
        }

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var restoreEntries = new List<PendingRestoreEntry>();
        var tracked        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Content hash of every tracked file before this test wrote it (null = absent), for
        // the editor-write journal that Restore Full Backup relies on (issue 118).
        var hashBefore     = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Records one game file the apply loop may write (issue #66). An existing file is
        // copied to tempDir and restored byte-for-byte; a missing one gets an empty backup
        // path, which restore reads as "didn't exist — delete it". New conversations and
        // their stringtables are covered by the second case, so they live in the persisted
        // manifest and survive a crash or restart in test mode. Every write also has a
        // ".bak" sidecar (the serializers copy the old file there first — issue 121),
        // tracked the same way so a pre-existing .bak comes back unclobbered.
        void Track(string path)
        {
            foreach (var p in new[] { path, path + ".bak" })
            {
                if (!tracked.Add(p)) continue;
                hashBefore[p] = FileHash.Of(p);
                var backup = Path.Combine(tempDir, $"{restoreEntries.Count:D4}_{Path.GetFileName(p)}");
                if (File.Exists(p)) File.Copy(p, backup);
                else backup = string.Empty;
                // A missing file may also need folders created for it (a new language's
                // text folder, say), which restore must take away again (issue 125).
                restoreEntries.Add(new PendingRestoreEntry(backup, string.Empty, p, string.Empty,
                    CreatedFolders.MissingAncestors(p)));
            }
        }

        try
        {
            foreach (var (convName, patch) in _project.Patches)
            {
                var file = _provider.FindConversation(convName)
                        ?? (_project.IsNewConversation(convName)
                               ? _provider.BuildNewConversationFile(convName)
                               : null);

                if (file is null)
                {
                    AppLog.Warn($"Conversation not found for patch: {convName}");
                    continue;
                }

                Track(file.ConversationPath);
                Track(_provider.GetStringTablePath(file));

                // A conversation created in the editor is also registered with the game
                // (issue 162), keyed on the project rather than on the file being missing:
                // the patcher may already have installed it.
                if (_project.IsNewConversation(convName) && _provider.ConversationManifestPath(convName) is { } manifest)
                    Track(manifest);

                // The apply loop below also writes stringtables for every *other*
                // installed language carried by the patch (TranslationApplier).
                var installed = _provider.AvailableLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var lang in patch.Translations.Keys.Where(installed.Contains))
                    Track(_provider.GetStringTablePath(file, lang));
            }

            var unreadable = new List<string>();

            // Persist restore info before writing game files (crash safety)
            AppSettings.SetPendingRestores(restoreEntries);

            // Copy _vo/ files to the game's VO directory (PoE2 only) and append their
            // backup entries so that F6 can restore or remove them.
            if (string.Equals(_activeGameId, "poe2", StringComparison.OrdinalIgnoreCase))
            {
                SyncVoToGame(restoreEntries, hashBefore);
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

                // Create blank template if file doesn't exist yet (tracked above as
                // "didn't exist", so restore deletes it)
                if (!File.Exists(file.ConversationPath))
                    _provider.InitializeConversationFile(file);
                if (_project.IsNewConversation(convName))
                    _provider.WriteConversationManifest(convName);

                var conversation = _provider.LoadConversation(file);
                var baseSnap     = ConversationSnapshotBuilder.Build(conversation);
                var merged       = PatchApplier.Apply(baseSnap, patch, ignoreConflicts);
                _provider.SaveConversation(file, merged);
                // The bundle/XML above only carries structure — node text lives in the
                // per-language stringtables. Without this, added or edited lines are
                // invisible in-game (B-005). Mirrors the dialog-patcher CLI.
                unreadable.AddRange(TranslationApplier.WriteTranslations(file, patch, _provider));
            }

            RecordEditorWrites(hashBefore);
            AppLog.Info($"Test: applied {_project.Patches.Count} patch(es) from project '{_project.Name}'");
            if (unreadable.Count > 0)
                // Damaged stringtables are left untouched (issue 119); that language shows no new text in-game.
                StatusText = Loc.FormatCount("Status_TestPatchSkippedUnreadable", unreadable.Count, PathDisplay.ForDisplay(unreadable[0]));
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
                RollBackFailedTest(restoreEntries, tempDir);

                var force = await RequestConflictResolution(ex);
                if (force)
                    await DoTestPatch(ignoreConflicts: true);
                else
                    StatusText = Loc.Format("Status_PatchConflictCancelled", ex.NodeId, ex.FieldName);
            }
            else
            {
                RollBackFailedTest(restoreEntries, tempDir);
                StatusText = Loc.Format("Status_PatchConflict",
                    ex.NodeId, ex.FieldName, ex.ExpectedFrom, ex.ActualValue);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to test project '{_project?.Name}'", ex);
            RollBackFailedTest(restoreEntries, tempDir);
            StatusText = Loc.Format("Status_TestApplyError", _project!.Name, ex.Message);
            ReportError?.Invoke(ex);
        }
    }

    /// Undoes a test that failed part-way: puts back whatever it already wrote, then drops
    /// the manifest and backups. Clearing the manifest *without* restoring (the old
    /// behaviour) stranded partial writes in the game folder with no way back via F6 (#66).
    /// If the rollback itself fails, the manifest is kept so F6 can finish the job.
    private void RollBackFailedTest(IReadOnlyList<PendingRestoreEntry> entries, string tempDir)
    {
        try
        {
            // The backups were made moments ago, so one missing here is a real fault:
            // keep the manifest, exactly as for a failed copy.
            var unrestored = RestoreFilesFromBackup(entries);
            if (unrestored.Count > 0)
                throw new FileNotFoundException(
                    $"Test backup missing for {unrestored.Count} file(s), first '{unrestored[0]}'");
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not roll back a failed test; press Restore (F6) to retry", ex);
            ReportError?.Invoke(ex);
            return;
        }
        AppSettings.ClearPendingRestores();
        try { Directory.Delete(tempDir, recursive: true); }
        catch (Exception ex) { AppLog.Warn($"Could not delete temp backup folder: {ex.Message}"); }
    }

    /// Puts back every file it can and returns the game files it could not, because their
    /// temp backup is gone (issue 125) — those still hold the test's version.
    private static IReadOnlyList<string> RestoreFilesFromBackup(IReadOnlyList<PendingRestoreEntry> entries)
    {
        var unrestored = new List<string>();
        foreach (var r in entries)
        {
            Restore(r.BackupConvPath, r.OriginalConvPath);
            Restore(r.BackupStPath, r.OriginalStPath);
        }
        // Last, once every file the test created is gone, so the folders can be empty.
        CreatedFolders.RemoveIfEmpty(entries.SelectMany(r => r.CreatedFolders ?? []));
        return unrestored;

        void Restore(string backup, string original)
        {
            if (string.IsNullOrEmpty(original)) return;
            // No backup path = the test created this file (a new conversation, an added VO
            // .wem, a .bak sidecar): remove it.
            if (string.IsNullOrEmpty(backup))
            {
                if (File.Exists(original)) File.Delete(original);
            }
            else if (File.Exists(backup))
                File.Copy(backup, original, overwrite: true);
            else
                unrestored.Add(original);
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
    private void SyncVoToGame(IList<PendingRestoreEntry> restoreEntries, IDictionary<string, string?> hashBefore)
    {
        if (ProjectPath is null) return;

        var voFolder = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo");
        if (!Directory.Exists(voFolder)) return;

        var gameVoRoot = VoPathResolver.VoicesRoot(_currentGameDirectory);
        // Where the originals of overwritten shipped files are kept (issue 123); none without a backup.
        var backupPick   = AppSettings.GetBackupPath(_currentGameDirectory);
        var latestBackup = backupPick is null ? null : FullBackup.Latest(backupPick);

        foreach (var localFile in Directory.EnumerateFiles(voFolder, "*.wem", SearchOption.AllDirectories))
        {
            try
            {
                var relative  = Path.GetRelativePath(voFolder, localFile).Replace('\\', '/');
                var gameDest  = Path.Combine(gameVoRoot, relative);
                var backupDir = Path.Combine(Path.GetTempPath(), "PillarsDialogEditor",
                    "vobackup", Guid.NewGuid().ToString("N")[..8]);

                // Before CreateDirectory, so F6 knows which folders this sync made (issue 125).
                var createdFolders = CreatedFolders.MissingAncestors(gameDest);
                Directory.CreateDirectory(Path.GetDirectoryName(gameDest)!);

                // Journalled like the conversation files, so Restore Full Backup's editor-writes guard (issue 118)
                // recognises this write as the editor's.
                hashBefore[gameDest] = FileHash.Of(gameDest);
                // First overwrite only: PreserveOriginal keeps the earliest copy. A failure is
                // already logged there and must not stop the test.
                if (latestBackup is not null && File.Exists(gameDest))
                    FullBackup.PreserveOriginal(latestBackup, gameVoRoot, gameDest);

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
                    string.Empty,
                    createdFolders));

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

    private bool CanTestPatch() => IsEditingMode && _provider is not null && _project is not null;

    // ── Restore Conversations (all entries in project) ────────────────────
    // Deliberately not gated on IsEditingMode (issue 234): test mode can outlive a switch to Reading,
    // and then the test overlay's "End Test and Restore" must still work while the game files are
    // patched. Restoring the originals is the opposite of the risk reading mode guards against. The
    // Test menu item stays hidden and Shift+F5 stays blocked in Reading; only this command is open.
    [RelayCommand]
    private async Task RestoreConversation()
    {
        var entries = AppSettings.GetPendingRestores();
        if (entries is null || entries.Count == 0) return;
        try
        {
            // Created files (new conversations, their stringtables, .bak sidecars, added
            // VO) are manifest entries with an empty backup path, so this also deletes them.
            var unrestored = RestoreFilesFromBackup(entries);
            var leftUnrestored = false;
            if (unrestored.Count > 0)
            {
                // The temp backups were deleted under us (OS or cleanup tool). Never report
                // success over files still in their test state (issue 125): only the user
                // can decide to leave test mode with them, e.g. to then Restore Full Backup.
                AppLog.Error($"Restore: test backups missing for {unrestored.Count} file(s): "
                             + string.Join(", ", unrestored));
                if (ConfirmLeaveTestModeUnrestored is null
                    || !await ConfirmLeaveTestModeUnrestored(unrestored))
                {
                    StatusText = Loc.FormatCount("Status_RestoreBackupsMissing", unrestored.Count, unrestored[0]);
                    return;
                }
                AppLog.Warn($"Restore: leaving test mode with {unrestored.Count} file(s) not restored, at the user's request");
                leftUnrestored = true;
            }

            var tempDirsToDelete = entries
                .SelectMany(r => new[] { r.BackupConvPath, r.BackupStPath })
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => Path.GetDirectoryName(p)!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in tempDirsToDelete)
                try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
                catch (Exception ex) { AppLog.Warn($"Could not delete temp backup folder: {ex.Message}"); }

            AppSettings.ClearPendingRestores();
            AppLog.Info($"Restored {entries.Count} conversation(s)");
            StatusText = leftUnrestored
                ? Loc.FormatCount("Status_RestoreLeftUnrestored", unrestored.Count, unrestored[0])
                : Loc.Get("Status_RestoreComplete2");

            // For new conversations this reloads as an empty canvas (file no longer exists).
            ReloadCurrentConversationView();
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

        OpenConversationView(file);
    }

    /// False when nothing was loaded (no game data, or the load failed and was reported).
    private bool LoadConversationFile(ConversationFile file)
    {
        if (_provider is null) return false;
        try
        {
            _currentFile = file;
            var conversation = _provider.LoadConversation(file);
            var baselineMismatch = false;

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
                    AppLog.Warn($"Patch for '{file.Name}' no longer matches game data (node {conflict.NodeId}, field '{conflict.FieldName}'); forcing apply for display");
                    patchedSnap = PatchApplier.Apply(vanillaSnap, storedPatch, ignoreConflicts: true);
                    baselineMismatch = true;
                }
                var translations = storedPatch.Translations.GetValueOrDefault(_provider.Language);
                var patched = ConversationSnapshotBuilder.ToConversation(file.Name, patchedSnap, translations);
                Canvas.Load(patched, vanillaSnap, conversation.Strings.IsMissing);
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
            if (conversation.Strings.IsUnreadable)
                // The structure loaded, but this language's text file is damaged (issue 119):
                // name it, so the text placeholders aren't a mystery. Logged by the parser.
                StatusText = Loc.Format("Status_StringTableUnreadable", file.Name, PathDisplay.ForDisplay(conversation.Strings.UnreadablePath));
            else if (baselineMismatch)
                // Set here, not where the conflict is caught: the "loaded" message below used to
                // replace it at once, so the mismatch was never seen (issue 234).
                StatusText = Loc.Format("Status_PatchBaselineMismatch", file.Name);
            else if (_project is null && !IsReadingMode)
                StatusText = Loc.Get("Status_NoProjectReadOnly");
            else
                // With a project: the canvas, with its added nodes, not the game file alone (GitHub
                // issue 256). With none in Reading mode: File > New Project is hidden there, so the
                // editing hint would point at nothing (issue 234); report the load like a project would.
                StatusText = Loc.Format("Status_ConversationLoaded", file.Name, Canvas.Nodes.Count);

            // A Find-in-Project navigation may have requested a node in this conversation
            // before it was loaded; select it now that Canvas.Nodes is populated.
            if (_pendingSelectNodeId is int pending)
            {
                _pendingSelectNodeId = null;
                SelectNodeById(pending);
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load conversation '{file.Name}'", ex);
            StatusText = Loc.Format("Status_LoadError", file.Name, ex.Message);
            ReportError?.Invoke(ex);
            return false;
        }
    }

    /// Opens the project-wide Find window. Game-agnostic; unlike batch VO it doesn't
    /// require a saved project path since it never writes anything.
    public Func<ProjectFindViewModel, Task>? ShowFindInProject { get; set; }

    [RelayCommand(CanExecute = nameof(CanFindInProject))]
    private async Task FindInProject()
    {
        if (_project is null || _provider is null) return;
        var vm = new ProjectFindViewModel(
            _project, _provider, _provider.Language,
            () => Canvas.Nodes.Count > 0
                ? (Canvas.ConversationName, Canvas.BuildSnapshot())
                : (null, (ConversationEditSnapshot?)null));
        vm.RequestNavigate += NavigateToFoundNode;
        if (ShowFindInProject is not null)
            await ShowFindInProject(vm);
    }

    /// Batch Replace adds its changes to the open project, never to the game folder
    /// (issue 124), so it needs a project as well as the game data it searches.
    public bool CanBatchReplace => IsEditingMode && _project is not null && _provider is not null;

    /// The View shows the (non-modal) Batch Replace window.
    public Func<BatchReplaceViewModel, Task>? ShowBatchReplace { get; set; }

    [RelayCommand(CanExecute = nameof(CanBatchReplace))]
    private async Task BatchReplace()
    {
        if (_project is null || _provider is null) return;
        var vm = new BatchReplaceViewModel(
            _provider, _provider.EnumerateConversations(),
            // The open conversation's canvas holds edits the project doesn't have yet;
            // folding over them would lose one or the other.
            f => f.Name == CurrentConversationName,
            () => _project,
            CommitBatchReplace,
            () => AppSettings.GetPendingRestores() is { Count: > 0 });
        if (ShowBatchReplace is not null)
            await ShowBatchReplace(vm);
    }

    // Unsaved like any other edit: Ctrl+S writes it, autosave covers it meanwhile.
    private void CommitBatchReplace(DialogProject updated)
    {
        SetProject(updated);
        IsModified = true;
    }

    /// Opens the read-only Reputation & Disposition Balance window. Same gate as Find in
    /// Project; the window never writes.
    public Func<RepDispositionBalanceViewModel, Task>? ShowRepDispositionBalance { get; set; }

    [RelayCommand(CanExecute = nameof(CanShowRepDispositionBalance))]
    private async Task ShowRepDispositionBalanceWindow()
    {
        if (_provider is null) return;
        var vm = new RepDispositionBalanceViewModel(
            _project ?? DialogProject.Empty(string.Empty), _provider,
            () => Canvas.Nodes.Count > 0
                ? (Canvas.ConversationName, (ConversationEditSnapshot?)Canvas.BuildSnapshot())
                : (null, (ConversationEditSnapshot?)null),
            hasProject: _project is not null);
        if (ShowRepDispositionBalance is not null)
            await ShowRepDispositionBalance(vm);
    }

    /// A loaded provider and a game folder (the browser scans every vanilla conversation on
    /// disk). No project needed: Readers use it too (issue 234).
    public bool CanBrowseSpeakerLines =>
        _provider is not null
        && !string.IsNullOrEmpty(_currentGameDirectory);

    /// The View constructs and shows the browser window (and kicks off ScanAsync).
    public Func<SpeakerLineBrowserViewModel, Task>? ShowSpeakerLineBrowser { get; set; }

    /// Browser-specific three-way dirty guard (sibling of ConfirmScanWithUnsavedChanges;
    /// browser copy differs — the scan still includes the open conversation's live text).
    /// Null in unit tests → a dirty project is treated as Cancel, never a silent open.
    public Func<Task<ScanDirtyChoice>>? ConfirmBrowseWithUnsavedChanges { get; set; }

    [RelayCommand(CanExecute = nameof(CanBrowseSpeakerLines))]
    private Task BrowseSpeakerLines() => OpenSpeakerLineBrowserAsync(null);

    /// Opens the Speaker Line Browser, pre-selecting <paramref name="initialSpeakerGuid"/>
    /// when supplied (the canvas context action). Runs the dirty guard first; declining to
    /// save still opens, cancelling does not. Reuses NavigateToFoundNode for jump-to-node.
    public async Task OpenSpeakerLineBrowserAsync(string? initialSpeakerGuid = null)
    {
        if (_provider is null) return;

        if (IsModified)
        {
            var choice = ConfirmBrowseWithUnsavedChanges is null
                ? ScanDirtyChoice.Cancel
                : await ConfirmBrowseWithUnsavedChanges();
            if (choice == ScanDirtyChoice.Cancel) return;
            if (choice == ScanDirtyChoice.SaveAndScan) SaveProject();
        }

        var vm = new SpeakerLineBrowserViewModel(
            _project ?? DialogProject.Empty(string.Empty), _provider, _provider.Language,
            () => Canvas.Nodes.Count > 0
                ? (Canvas.ConversationName, Canvas.BuildSnapshot())
                : (null, (ConversationEditSnapshot?)null),
            initialSpeakerGuid, hasProject: _project is not null);
        vm.RequestNavigate += NavigateToFoundNode;

        if (ShowSpeakerLineBrowser is not null)
            await ShowSpeakerLineBrowser(vm);
    }

    /// Navigate from a find result to its node: switch conversation if needed
    /// (reusing the unsaved-changes guard), then select the node by id.
    /// Builds the cross-conversation graph for Flow Analytics' Playthrough stats (#14).
    ///
    /// Lives here rather than in the View because the project, the provider and the cached
    /// conversation-GUID map are all private state — the View just asks for a graph. Returns
    /// null when there is nothing to resolve against, which the panel treats as "analyse the
    /// open conversation only".
    public MultiConversationGraph? ResolveConversationJumpGraph()
    {
        if (_project is null || _provider is null) return null;
        return ConversationJumpResolver.Resolve(
            _project, _provider, _provider.Language,
            Canvas.ConversationName ?? "", Canvas.BuildSnapshot(),
            _conversationNamesById);
    }

    public void NavigateToFoundNode(string conversationName, int nodeId)
    {
        if (Canvas.ConversationName == conversationName)
        {
            SelectNodeById(nodeId);
            return;
        }
        // Resolve the target file. A new (not-yet-on-disk) conversation the project
        // added lives only in Patches, so FindConversation returns null for it — fall
        // back to a synthetic new-conversation file, the same pattern every other call
        // site uses (e.g. the F5 apply loop). Without this, double-clicking a match in
        // a new conversation while a different one is open would silently do nothing.
        var file = _provider?.FindConversation(conversationName)
                ?? (_project?.IsNewConversation(conversationName) == true
                       ? _provider?.BuildNewConversationFile(conversationName)
                       : null);
        if (file is null)
        {
            StatusText = Loc.Get("Status_FindInProject_NodeGone");
            return;
        }
        _pendingSelectNodeId = nodeId;
        OnConversationSelected(file);   // honours the dirty guard; loads the conversation
    }

    private int? _pendingSelectNodeId;

    private void SelectNodeById(int nodeId)
    {
        var node = Canvas.Nodes.FirstOrDefault(n => n.NodeId == nodeId);
        if (node is not null) Canvas.SelectedNode = node;
        else StatusText = Loc.Get("Status_FindInProject_NodeGone");
    }
}
