using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Audio;
using DialogEditor.Avalonia.Controls;
using DialogEditor.Avalonia.Docking;
using DialogEditor.Avalonia.Services;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class MainWindow : Window
{
    private LegendWindow?          _legendWindow;
    private TagReferenceWindow?    _tagReferenceWindow;
    private PatchManagerWindow?    _patchManagerWindow;
    private FindReplaceWindow?     _findReplaceWindow;
    private BatchReplaceWindow?    _batchReplaceWindow;
    private FlowAnalyticsWindow?   _flowAnalyticsWindow;
    private VoValidationWindow?    _voValidationWindow;
    private TextTagValidationWindow? _textTagValidationWindow;

    // Tour adorner state — one adorner on one target at a time.
    private TourHighlightAdorner? _tourAdorner;
    private Control?              _tourTarget;

    // One-shot LayoutUpdated watcher used when a tour step's target is a Dock tool whose
    // content has not been realised yet (see AttachTourAdornerWhenRealised). Held so the
    // next step change can cancel a watcher that is still hunting for the previous step's
    // target — otherwise a late realisation would ring a control the user has moved past.
    private EventHandler? _tourRealiseHandler;

    // Set to true immediately before a programmatic Close() call so that
    // the re-entrant OnClosing doesn't show the dirty-close dialog again.
    private bool _closingConfirmed = false;

    // Guards the one-time startup project re-open in OnOpened.
    private bool _startupDone = false;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(
            new AvaloniaDispatcher(),
            new AvaloniaFolderPicker(this),
            new AvaloniaFilePicker(this));

        var vm = (MainWindowViewModel)DataContext;
        vm.Tour.StepChanged += OnTourStepChanged;
        vm.UnsavedChangesRequested += () => _ = ShowUnsavedChangesDialogAsync(vm);
        vm.TestModeEntered += () => TestOverlay.IsVisible = true;
        vm.TestModeExited  += () => TestOverlay.IsVisible = false;
        vm.RequestConversationName               = () => PromptConversationNameAsync();
        vm.RequestConversationNameWithSuggestion = suggested => PromptConversationNameAsync(defaultValue: suggested);
        vm.AttributionLoader = path => new ProjectBlameService(new ProcessGitRunner()).Load(path);
        vm.RequestConflictResolution    = ex => ShowConflictResolutionDialogAsync(ex);
        vm.ShowExportConversations = exportVm =>
        {
            var window = new ExportConversationsWindow(exportVm);
            window.Show();
            window.Activate();
        };
        vm.ShowChangelog = changelogVm =>
        {
            var window = new ChangelogWindow(changelogVm);
            window.Show();
            window.Activate();
        };
        // Validate Text Tags dirty guard: three-way consent when the project has
        // unsaved changes (the scan reads saved state only).
        vm.ConfirmScanWithUnsavedChanges = () =>
            new SaveBeforeScanDialog().ShowDialogAsync(this);
        // Speaker Line Browser dirty guard: same three-way mechanism, browser-specific
        // copy (the scan still includes the open conversation's unsaved text).
        vm.ConfirmBrowseWithUnsavedChanges = () =>
            new SaveBeforeScanDialog(
                messageKey:       "SaveBeforeBrowse_Message",
                saveButtonKey:    "SaveBeforeBrowse_SaveAndBrowse",
                proceedButtonKey: "SaveBeforeBrowse_BrowseAnyway").ShowDialogAsync(this);
        // Spell checking: three-layer store (user dictionaries + embedded game
        // lexicons + personal word list). Null checker would just disable spelling.
        EmbeddedLexicons.LoadInto(SpellDictionaryStore.Default);
        vm.Detail.SpellChecker = new SpellCheckService(SpellDictionaryStore.Default);
        vm.SpellStoreFactory   = () => SpellDictionaryStore.Default;
        // Crash recovery: offer to restore a newer autosave sidecar at project open.
        vm.ConfirmRestoreAutosave = t => new AutosaveRestoreDialog(t).ShowDialogAsync(this);
        // Autosave: sidecar written every 60 s while the project has unsaved changes
        // (spec 2026-07-12). The tick is a no-op on a clean session; runs on the UI
        // thread so folding the canvas into the project is safe.
        var autosaveTimer = new global::Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60),
        };
        autosaveTimer.Tick += (_, _) => vm.AutosaveTick();
        autosaveTimer.Start();
        // Launch greeting: show "what's new" once if the app version advanced.
        vm.ShowWhatsNewIfUpdated();
        vm.ShowTagReference = tagVm =>
        {
            // Cached instance: reopening the menu item focuses the open window.
            if (_tagReferenceWindow is { IsVisible: true })
            {
                _tagReferenceWindow.Activate();
                return;
            }
            _tagReferenceWindow = new TagReferenceWindow(tagVm);
            _tagReferenceWindow.Closed += (_, _) => _tagReferenceWindow = null;
            _tagReferenceWindow.Show();
            _tagReferenceWindow.Activate();
        };
        vm.ShowAbout = aboutVm =>
        {
            var window = new AboutWindow(aboutVm);
            window.Show();
            window.Activate();
        };
        vm.ShowImportWarnings = async warnings =>
        {
            var dialog = new ImportWarningsDialog(warnings);
            await dialog.ShowDialog(this);
        };
        // Post: some ReportError call sites run off the UI thread (e.g. the VO
        // alias index rebuild in Task.Run), and window creation must not.
        vm.ReportError = ex =>
            Dispatcher.UIThread.Post(() => (Application.Current as App)?.ShowExceptionReport(ex));
        vm.ShowGitConflictResolution = async resolutionVm =>
        {
            var dialog = new GitConflictResolutionWindow(resolutionVm);
            await dialog.ShowDialog(this);
            return resolutionVm.Result;   // null if the user cancelled
        };
        vm.RequestLanguageCode = async (title, defaultValue) =>
        {
            var dialog = new LanguageCodeDialog(defaultValue);
            await dialog.ShowDialog(this);
            return dialog.Result;
        };
        // Recent Projects: missing-file remove-offer dialog (Task 4/5).
        vm.ConfirmRemoveMissingProject = path =>
            new RecentProjectMissingDialog(Loc.Format("RecentMissing_Message", path)).ShowDialogAsync(this);

        var audioPlayer = new VoAudioPlayer();
        vm.Detail.Player = audioPlayer;
        Closed += (_, _) => audioPlayer.Dispose();

        var voImporter = new VoImporter();
        vm.Detail.Importer = voImporter;
        vm.Detail.ShowImportDialog = async paths =>
        {
            var dialog = new VoImportDialog(voImporter, paths, audioPlayer);
            await dialog.ShowDialog(this);
            return dialog.Result;
        };
        vm.Detail.ReportStatus = msg => vm.StatusText = msg;

        // Task 7: ExternalVO alias picker (reuse another line's recording).
        // Note: the design brief asked for a new `MainWindowViewModel.CurrentProvider`
        // property, but `Provider => _provider` already exists at MainWindowViewModel.cs:56
        // exposing the same read-only game-data provider, so we reuse it rather than add
        // a duplicate public property with identical semantics.
        vm.Detail.ShowAliasPicker = async currentAlias =>
        {
            if (vm.Provider is null || vm.Detail.GameRoot is not { Length: > 0 } root)
                return null;
            var picker = new VoAliasPickerWindow(
                new VoAliasPickerViewModel(vm.Provider, root, currentAlias));
            await picker.ShowDialog(this);
            return picker.ResultAlias;
        };

        // Task 9: confirm before importing over a shared (aliased) recording.
        vm.Detail.ConfirmAliasedImport = async prompt =>
        {
            var dlg = new AliasImportConfirmDialog(prompt);
            await dlg.ShowDialog(this);
            return dlg.Choice;
        };

        vm.Canvas.ShowBatchVoImport = async () =>
        {
            var rows = vm.Canvas.BuildBatchVoRows(vm.Detail.GameRoot, vm.Detail.ActiveGameId);
            if (rows.Count == 0) return;
            var batchVm = new BatchVoImportViewModel(rows, voImporter, isSingleConversation: true);
            var dlg     = new BatchVoImportDialog(batchVm, audioPlayer);
            await dlg.ShowDialog(this);
            vm.Detail.Refresh();
        };

        // Project-wide variant: the VM scans and reports; this delegate only
        // hosts the dialog (multi-conversation mode shows the Conversation column).
        vm.ShowBatchVoImportAll = async rows =>
        {
            var batchVm = new BatchVoImportViewModel(rows, voImporter, isSingleConversation: false);
            var dlg     = new BatchVoImportDialog(batchVm, audioPlayer);
            await dlg.ShowDialog(this);
        };

        vm.ShowFindInProject = async findVm =>
        {
            var win = new FindInProjectWindow(findVm);
            win.Show(this);          // non-modal, owned — results stay visible while browsing
            await Task.CompletedTask;
        };

        vm.ShowRepDispositionBalance = async balanceVm =>
        {
            var win = new RepDispositionBalanceWindow(balanceVm);
            win.Show(this);          // non-modal, owned — report stays visible while browsing
            balanceVm.RefreshCommand.Execute(null);   // analyse immediately on open
            await Task.CompletedTask;
        };

        vm.ShowSpeakerLineBrowser = async browserVm =>
        {
            var win = new SpeakerLineBrowserWindow(browserVm);
            win.Show(this);          // non-modal, owned — stays open while the writer browses
            await Task.CompletedTask;
        };

        // BuildDock wires the docking shell (Browser/Canvas/Details/ConditionSearch tools)
        // once a game is loaded — see LoadDirectory's completion below and Tour's Ready hook.
        // FocusDetailRequested (canvas → detail-pane focus, e.g. Enter on a selected node)
        // is re-subscribed there too, once the live ConversationView instance exists.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ConditionSearch) && vm.ConditionSearch is not null)
                BuildDock(vm);
        };
        // The VM's constructor may already have auto-loaded the last game folder
        // (AppSettings.LastGameDirectory) synchronously, before the subscription above
        // existed to catch it — cover that case explicitly.
        if (vm.ConditionSearch is not null)
            BuildDock(vm);

        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        this.AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble);
    }

    private EditorDockFactory? _factory;

    // Persists/restores the dock layout structure across sessions (Docking Shell Phase 1,
    // Task 9). Content (live VMs) is never serialized — EditorDockFactory.RestoreLayout
    // re-attaches it by Id. Never throws; a missing/corrupt file falls back to the default.
    private readonly DockLayoutStore _store = new();

    // BuildDock runs on EVERY game-folder load, not just the first (see the PropertyChanged
    // hook above and ReopenLastProjectOnStartup). Loading the on-disk layout again on a later
    // folder-open would silently discard the user's in-session rearrangement, so the saved
    // layout is only consulted for the FIRST build of the process; subsequent folder-opens
    // rebuild the current in-memory default via CreateLayout, same as before Task 9.
    // ResetLayout_Click intentionally does NOT re-arm this guard — Reset must bypass the disk
    // file entirely (it just deleted it) and always produce the true default layout.
    private bool _dockRestored = false;

    // Tracks the ConversationView instance whose FocusDetailRequested we've already
    // hooked, so re-running BuildDock (e.g. opening a second game folder in the same
    // session) rewires the new Dock-realised instance without leaving the old one's
    // now-orphaned subscription behind or double-subscribing the same instance twice.
    private ConversationView? _wiredCanvasView;

    // The LayoutUpdated handler we attach while waiting for the canvas ConversationView to
    // realise (null when we're not currently waiting). Held so we can detach it once wired.
    private EventHandler? _canvasWireHandler;

    /// Builds the docking layout (Conversations/Canvas/Node Details/Condition search) once
    /// the shell-level ConditionSearchViewModel exists — i.e. once a game folder has loaded
    /// (see MainWindowViewModel.RebuildConditionSearch). On the FIRST build of the process,
    /// tries the saved layout from disk (_store.Load) and re-hydrates it via
    /// EditorDockFactory.RestoreLayout; falls back to (and every later build uses) the
    /// in-memory default from CreateLayout — see _dockRestored's comment for why later
    /// folder-opens don't re-consult the disk file. Also re-wires the canvas → detail-pane
    /// focus hop, which now targets the Dock-hosted views instead of named XAML controls
    /// (the fixed 5-column grid is gone).
    private void BuildDock(MainWindowViewModel vm)
    {
        if (vm.ConditionSearch is null) return;   // needs a loaded game

        var factory = new EditorDockFactory(vm.Browser, vm.Canvas, vm.Detail, vm.ConditionSearch);

        Dock.Model.Controls.IRootDock? layout = null;
        if (!_dockRestored)
        {
            _dockRestored = true;   // only the first build of the session may consult the disk file
            var restored = _store.Load(_store.DefaultPath);
            if (restored is not null)
            {
                // DockLayoutStore.Load only guards against unparseable JSON; a file that
                // deserializes to a non-null IRootDock with an unexpected shape (schema skew
                // after an upgrade, a torn-but-valid prior write, a hand-edited file) can still
                // throw inside RestoreLayout/InitLayout. Catch broadly here, log, delete the
                // offending file (so the next launch doesn't retry and crash-loop on it), and
                // fall through to the default CreateLayout path below.
                try
                {
                    factory.RestoreLayout(restored);
                    layout = restored;
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Dock layout restore failed, using default: {ex.Message}");
                    _store.Delete(_store.DefaultPath);
                    layout = null;
                }
            }
        }
        if (layout is null)
        {
            layout = factory.CreateLayout();
            factory.InitLayout(layout);
        }
        vm.DockLayout = layout;
        _factory = factory;

        // The document/tool content is realised by Application.DataTemplates once Dock
        // renders the new layout — and, because Dock presents document/tool bodies through a
        // DeferredContentControl, the ConversationView can appear several layout passes AFTER
        // BuildDock returns. A bounded burst of Dispatcher.Post(Loaded) retries raced that
        // deferral timeline and lost (all attempts ran before the view existed). Instead,
        // listen on LayoutUpdated and wire the hop the first pass the view is present.
        WireCanvasFocusHopWhenRealised();
    }

    /// Hooks the canvas ConversationView's FocusDetailRequested (Enter on a node → focus the
    /// Node Details pane) as soon as Dock realises the view. Because that realisation is
    /// deferred and its exact timing is not observable up front, we watch LayoutUpdated —
    /// which fires on every layout pass, including the one that first materialises the
    /// deferred document content — and detach ourselves once the hop is wired. The
    /// NodeDetailView target is resolved lazily at fire time, so a closed/floated Detail tool
    /// degrades to a no-op instead of an NRE.
    private void WireCanvasFocusHopWhenRealised()
    {
        if (_canvasWireHandler is not null) return;   // already waiting (a prior build's watcher is live)

        _canvasWireHandler = (_, _) =>
        {
            if (FindCanvasView() is not { } canvasView) return;   // not realised yet — keep listening

            if (!ReferenceEquals(canvasView, _wiredCanvasView))
            {
                _wiredCanvasView = canvasView;
                canvasView.FocusDetailRequested += (_, _) => FindDetailView()?.FocusFirstField();
            }

            LayoutUpdated -= _canvasWireHandler;   // wired (or already wired to this instance) — stop watching
            _canvasWireHandler = null;
        };
        LayoutUpdated += _canvasWireHandler;
    }

    /// Locates the live ConversationView hosted by the Dock canvas document. Dock
    /// instantiates it lazily from Application.DataTemplates, so this is a lookup
    /// (not a cached field) — safe to call any time after BuildDock has run.
    private ConversationView? FindCanvasView() => FindDockedView<ConversationView>();

    /// Locates the live NodeDetailView hosted by the Dock details tool (same caveat as
    /// <see cref="FindCanvasView"/> — it may be null if the tool is closed).
    private NodeDetailView? FindDetailView() => FindDockedView<NodeDetailView>();

    /// Locates the live GameBrowserView hosted by the Dock conversations tool.
    private GameBrowserView? FindBrowserView() => FindDockedView<GameBrowserView>();

    /// Finds a Dock-hosted view by TYPE rather than by name. Tool content is instantiated
    /// lazily from Application.DataTemplates and therefore carries no compile-time x:Name
    /// in MainWindow's scope, so type is the only stable handle we have on it.
    ///
    /// Searches this window first, then any floating EditorHostWindow: dragging a tool out
    /// reparents its content into a separate TopLevel, where this window's visual tree can
    /// no longer see it. Returns null when the tool is closed entirely, or when Dock has
    /// not realised the content yet — callers must treat null as "not now", not "never".
    private T? FindDockedView<T>() where T : Control
    {
        if (this.GetVisualDescendants().OfType<T>().FirstOrDefault() is { } here)
            return here;

        // Application.Current already tracks every open top-level, so floating hosts need
        // no registry of their own — Dock opens and closes them behind our back.
        if (global::Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var host in desktop.Windows.OfType<EditorHostWindow>())
                if (host.GetVisualDescendants().OfType<T>().FirstOrDefault() is { } floated)
                    return floated;
        }

        return null;
    }

    // ── View menu: show/focus a tool, re-opening it if the user closed its tab ────
    private void ShowBrowserTool_Click(object? sender, RoutedEventArgs e)         => ShowToolById(EditorDockFactory.BrowserId);
    private void ShowDetailsTool_Click(object? sender, RoutedEventArgs e)         => ShowToolById(EditorDockFactory.DetailsId);
    private void ShowConditionSearchTool_Click(object? sender, RoutedEventArgs e) => ShowToolById(EditorDockFactory.ConditionSearchId);

    /// Shows/focuses a Dock tool by its Id. If the tool is still present in the tree
    /// (open, even if its tab isn't active), this just activates it. If the user closed
    /// its tab, EditorDockFactory.HideToolsOnClose=true means Dock moved it into
    /// IRootDock.HiddenDockables instead of discarding it — FactoryBase.RestoreDockable(id)
    /// re-inserts it into its original owner dock, and we then activate it the same way.
    /// No-ops safely (with a log) if no game is loaded yet (_factory/DockLayout are null)
    /// or the id isn't found in either the live tree or the hidden set.
    private void ShowToolById(string id)
    {
        if (_factory is null || DataContext is not MainWindowViewModel { DockLayout: Dock.Model.Core.IDock root })
            return;

        try
        {
            var tool = _factory.FindDockable(root, d => d.Id == id) ?? _factory.RestoreDockable(id);
            if (tool is not null)
                _factory.SetActiveDockable(tool);
            else
                AppLog.Warn($"MainWindow: View menu could not find dock tool '{id}' to show.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"MainWindow: failed to show dock tool '{id}'.", ex);
        }
    }

    /// Rebuilds the default docking layout (Conversations/Canvas/Node Details/Condition
    /// search in their original panes), discarding any floating windows, closed tools or
    /// resized panes from the current session — AND deletes the saved layout file, so the
    /// next launch also starts from the true default rather than the just-discarded
    /// arrangement. No-ops (silently — nothing to reset) if no game is loaded, matching
    /// BuildDock's own guard.
    private void ResetLayout_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        _store.Delete(_store.DefaultPath);
        _dockRestored = true;   // bypass the disk file even if this is somehow the first build
        BuildDock(vm);
    }

    // Mirrors the focused control's AutomationProperties.HelpText (set by item 5's
    // Part A sweep) into the view model so the status bar can show it — giving
    // sighted keyboard users the same explanation screen readers announce on focus.
    private void OnAnyGotFocus(object? sender, GotFocusEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.FocusHintText = e.Source is StyledElement el
            ? AutomationProperties.GetHelpText(el) ?? string.Empty
            : string.Empty;
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;

        switch (e.Key)
        {
            case Key.F when e.KeyModifiers == KeyModifiers.Control:
                FindCanvasView()?.FocusSearch();
                e.Handled = true;
                break;

            case Key.H when e.KeyModifiers == KeyModifiers.Control:
                FindReplace_Click(null, null!);
                e.Handled = true;
                break;

            case Key.H when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                BatchReplace_Click(null, null!);
                e.Handled = true;
                break;

            case Key.F when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                // Not gated via RelayCommand.Execute — that doesn't self-check CanExecute —
                // so an unmet gate (no project/provider/game folder) silently no-ops instead
                // of throwing.
                if (vm.FindInProjectCommand.CanExecute(null))
                    vm.FindInProjectCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.N when e.KeyModifiers == KeyModifiers.Control:
                vm.NewProjectCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.O when e.KeyModifiers == KeyModifiers.Control:
                vm.OpenProjectCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.W when e.KeyModifiers == KeyModifiers.Control:
                // RelayCommand.Execute does not gate itself: with no project open,
                // Ctrl+W must not clear a remembered LastProjectPath.
                if (vm.CloseProjectCommand.CanExecute(null))
                    vm.CloseProjectCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.O when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                vm.OpenFolderCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Z when e.KeyModifiers == KeyModifiers.Control:
                vm.Canvas.UndoCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Y when e.KeyModifiers == KeyModifiers.Control:
                vm.Canvas.RedoCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F5 when e.KeyModifiers == KeyModifiers.None:
                vm.TestPatchCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F6 when e.KeyModifiers == KeyModifiers.None:
                vm.RestoreConversationCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F7 when e.KeyModifiers == KeyModifiers.None:
                FlowAnalytics_Click(null, null!);
                e.Handled = true;
                break;

            case Key.OemComma when e.KeyModifiers == KeyModifiers.Control:
                _ = OpenSettingsAsync();
                e.Handled = true;
                break;

            case Key.B when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                vm.RestoreBackupCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.S when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
                    FindCanvasView()?.FocusEditor();   // commit a focused TextBox edit first, like Ctrl+S
                vm.SaveProjectAsCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.S when e.KeyModifiers == KeyModifiers.Control:
                if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
                    FindCanvasView()?.FocusEditor();
                vm.SaveCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete when vm.Canvas.SelectedNode is not null
                             && e.Source is not TextBox
                             && vm.Canvas.IsEditable:
                vm.Canvas.DeleteNodeCmdCommand.Execute(vm.Canvas.SelectedNode);
                e.Handled = true;
                break;
        }
    }

    private void FindReplace_Click(object? sender, RoutedEventArgs e)
    {
        var vm   = (MainWindowViewModel)DataContext!;
        var frVm = new FindReplaceViewModel(vm.Canvas);

        if (_findReplaceWindow is null || !_findReplaceWindow.IsVisible)
        {
            _findReplaceWindow = new FindReplaceWindow(frVm);
            _findReplaceWindow.Closed += (_, _) => _findReplaceWindow = null;
        }
        else
        {
            // Conversation may have changed — always give the window the current canvas
            _findReplaceWindow.DataContext = frVm;
        }
        _findReplaceWindow.Show();
        _findReplaceWindow.Activate();
    }

    private void BatchReplace_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        if (vm.Provider is null) return;

        var allFiles = vm.Provider.EnumerateConversations();
        var brVm = new BatchReplaceViewModel(
            vm.Provider,
            allFiles,
            f => f.Name == vm.CurrentConversationName);

        if (_batchReplaceWindow is null || !_batchReplaceWindow.IsVisible)
        {
            _batchReplaceWindow = new BatchReplaceWindow(brVm);
            _batchReplaceWindow.Closed += (_, _) => _batchReplaceWindow = null;
        }
        else
        {
            _batchReplaceWindow.DataContext = brVm;
        }
        _batchReplaceWindow.Show();
        _batchReplaceWindow.Activate();
    }

    private void FlowAnalytics_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;

        if (_flowAnalyticsWindow is null || !_flowAnalyticsWindow.IsVisible)
        {
            var analyticsVm = new FlowAnalyticsViewModel(
                () => vm.Canvas.BuildSnapshot(),
                nodeId =>
                {
                    var node = vm.Canvas.Nodes.FirstOrDefault(n => n.NodeId == nodeId);
                    if (node is not null) FindCanvasView()?.ScrollToNode(node);
                },
                () => vm.CurrentConversationTranslations,
                vm.ActiveGameId,
                // Reading speed (issue #14): the View owns AppSettings, so the VM stays
                // settings-free and its tests never touch the real settings.json.
                wordsPerMinute: AppSettings.ReadingWordsPerMinute,
                persistWordsPerMinute: v => AppSettings.ReadingWordsPerMinute = v,
                // Conversation handoffs (issue #14): same split as the reading speed — the
                // View owns AppSettings so the VM stays settings-free, and the graph comes
                // from the main VM, which holds the project, provider and GUID cache.
                resolveGraph: () => vm.ResolveConversationJumpGraph(),
                followConversationJumps: AppSettings.FollowConversationJumps,
                persistFollowJumps: v => AppSettings.FollowConversationJumps = v,
                navigateToNodeInConversation: vm.NavigateToFoundNode);

            _flowAnalyticsWindow = new FlowAnalyticsWindow(analyticsVm);

            void OnSaved() => analyticsVm.RefreshCommand.Execute(null);
            vm.ConversationSaved += OnSaved;
            _flowAnalyticsWindow.Closed += (_, _) =>
            {
                vm.ConversationSaved -= OnSaved;
                _flowAnalyticsWindow = null;
            };
        }

        // Analyse immediately on every summon (mirrors ValidateVO_Click's RunAsync):
        // the window used to open empty until the user pressed Refresh, and the
        // ConversationSaved hook alone misses conversation switches between summons.
        // Refresh is null-snapshot-safe, so this is harmless with nothing loaded.
        ((FlowAnalyticsViewModel)_flowAnalyticsWindow.DataContext!).RefreshCommand.Execute(null);

        _flowAnalyticsWindow.Show();
        _flowAnalyticsWindow.Activate();
    }

    private void ValidateVO_Click(object? sender, RoutedEventArgs e)
    {
        if (_voValidationWindow is not null && _voValidationWindow.IsVisible)
        {
            _voValidationWindow.Activate();
            return;
        }
        var vm = ((MainWindowViewModel)DataContext!).CreateVoValidationViewModel();
        if (vm is null) return;
        _voValidationWindow = new VoValidationWindow(vm);
        _voValidationWindow.Closed += (_, _) => _voValidationWindow = null;
        _voValidationWindow.Show(this);
        _ = vm.RunAsync();
    }

    private async void ValidateTextTags_Click(object? sender, RoutedEventArgs e)
    {
        // async void event handler: exceptions would crash the process, so keep the
        // whole body guarded (per the error-handling rule).
        try
        {
            if (_textTagValidationWindow is not null && _textTagValidationWindow.IsVisible)
            {
                _textTagValidationWindow.Activate();
                return;
            }
            var vm = await ((MainWindowViewModel)DataContext!).RequestTextTagValidationAsync();
            if (vm is null) return; // no project, or the dirty guard was cancelled
            _textTagValidationWindow = new TextTagValidationWindow(vm);
            _textTagValidationWindow.Closed += (_, _) => _textTagValidationWindow = null;
            _textTagValidationWindow.Show(this);
        }
        catch (Exception ex)
        {
            AppLog.Error("Validate Text Tags failed", ex);
        }
    }

    private void CompareVersions_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        if (vm.ProjectPath is null) return;

        var diffVm = new DiffViewModel(new ProcessGitRunner(), new AvaloniaDispatcher(),
                                       vm.ProjectPath,
                                       vm.Provider, vm.Provider?.Language ?? "en");

        diffVm.CommitApply      = applied => _ = vm.ApplyFromDiff(applied);
        diffVm.RequestUndoApply = () => vm.UndoApplyCommand.Execute(null);
        vm.ConfirmSaveBeforeApply = () => ShowSaveBeforeApplyDialogAsync(vm);

        new DiffWindow(diffVm).Show();
    }

    private void History_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        if (vm.ProjectPath is null) return;

        var historyVm = new HistoryViewModel(new ProcessGitRunner(), vm.ProjectPath);
        historyVm.CompareWithCommit = sha =>
        {
            var diffVm = new DiffViewModel(new ProcessGitRunner(), new AvaloniaDispatcher(),
                                           vm.ProjectPath,
                                           vm.Provider, vm.Provider?.Language ?? "en",
                                           initialRightRef: sha);
            diffVm.CommitApply      = applied => _ = vm.ApplyFromDiff(applied);
            diffVm.RequestUndoApply = () => vm.UndoApplyCommand.Execute(null);
            vm.ConfirmSaveBeforeApply = () => ShowSaveBeforeApplyDialogAsync(vm);
            new DiffWindow(diffVm).Show();
        };

        new HistoryWindow(historyVm).Show();
    }

    private void Attribution_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        if (vm.ProjectPath is null) return;

        new BlameWindow(new BlameViewModel(new ProcessGitRunner(), vm.ProjectPath)).Show();
    }

    private void OnOpenBranches(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        var path = vm.ProjectPath;
        if (path is null) return;

        var branchesVm = new BranchesViewModel(new GitBranchService(new ProcessGitRunner()), path)
        {
            EnsureNoUnsavedEdits  = () => vm.EnsureNoUnsavedEditsAsync(),
            ReloadProjectFromDisk = () => vm.ReloadCurrentProjectFromDisk(),
            // A commit moves HEAD without touching the working tree, so there is nothing
            // to reload — only the HEAD-derived blame cache to drop (#12).
            HeadMoved             = () => vm.InvalidateAttribution(),
        };

        var window = new BranchesWindow(branchesVm);

        branchesVm.RequestCommitConfirmation = pending => new CommitConsentDialog(pending).ShowDialogAsync(window);
        branchesVm.RequestBranchName = prefill =>
        {
            var title = Loc.Get(prefill is null ? "BranchName_NewTitle" : "BranchName_RenameTitle");
            return new BranchNameDialog(title, prefill).ShowDialogAsync(window);
        };
        branchesVm.ConfirmForceDelete = name =>
            new ForceDeleteDialog(Loc.Format("ForceDelete_Message", name)).ShowDialogAsync(window);

        window.Show(this);
    }

    private void PatchManager_Click(object? sender, RoutedEventArgs e)
    {
        if (_patchManagerWindow is null || !_patchManagerWindow.IsVisible)
        {
            var vm = new PatchManagerViewModel(
                new AvaloniaFolderPicker(this),
                new AvaloniaFilePicker(this));
            _patchManagerWindow = new PatchManagerWindow(vm);
            _patchManagerWindow.Closed += (_, _) => _patchManagerWindow = null;
        }
        _patchManagerWindow.Show();
        _patchManagerWindow.Activate();
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
        => await OpenSettingsAsync();

    private async Task OpenSettingsAsync()
    {
        var vm = (MainWindowViewModel)DataContext!;
        var settingsVm = vm.CreateSettingsViewModel(new FontScaleApplier());
        // Spelling section shell-outs (folder in Explorer, source link in browser).
        settingsVm.FolderOpener = p => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true });
        settingsVm.UrlOpener = u => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(u) { UseShellExecute = true });
        var settings = new SettingsWindow { DataContext = settingsVm };
        await settings.ShowDialog(this);
    }

    private void HelpToggle_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (HelpToggle.IsChecked == true)
            GetOrCreateLegend().ShowAndRestore(this);
        else
            GetOrCreateLegend().HideAndSave();
    }

    private LegendWindow GetOrCreateLegend()
    {
        if (_legendWindow is not null) return _legendWindow;
        _legendWindow = new LegendWindow();
        _legendWindow.OnHidden = () => HelpToggle.IsChecked = false;
        _legendWindow.PositionChanged += (_, _) =>
        {
            if (_legendWindow.IsVisible)
                AppSettings.SetLegendPosition(_legendWindow.Position.X, _legendWindow.Position.Y);
        };
        return _legendWindow;
    }

    // ── Startup project re-open ───────────────────────────────────────────
    // Deferred to here (rather than the VM constructor) so the window is shown
    // and all callbacks — including ShowGitConflictResolution — are wired before
    // a conflicted last-project tries to open its resolution dialog.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_startupDone) return;
        _startupDone = true;
        var vm = (MainWindowViewModel)DataContext!;
        vm.ReopenLastProjectOnStartup();
        if (!AppSettings.GuidedTourSeen)
            vm.Tour.Start();
    }

    // ── App-close guard ───────────────────────────────────────────────────
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        if (!_closingConfirmed && vm.IsModified && vm.CurrentConversationName is not null)
        {
            e.Cancel = true;
            // The continuation must set _closingConfirmed before calling Close()
            // so the re-entrant OnClosing doesn't trigger the dialog again.
            vm.GuardDirtyThen(() => { _closingConfirmed = true; Close(); });
            _ = ShowUnsavedChangesDialogAsync(vm);
        }
        else
        {
            // Real close path (either the conversation was never dirty, or the dirty-close
            // dialog already ran and this is the re-entrant confirmed Close()) — save exactly
            // once, here, so a corrupt/interrupted write can never cancel the actual close.
            if (vm.DockLayout is Dock.Model.Controls.IRootDock root)
                _store.Save(root, _store.DefaultPath);
            base.OnClosing(e);
        }
    }

    // ── Unsaved-changes dialog ────────────────────────────────────────────
    private async Task ShowUnsavedChangesDialogAsync(MainWindowViewModel vm)
    {
        var dialog = new UnsavedChangesDialog(vm.CurrentConversationName ?? "This conversation");
        await dialog.ShowDialog(this);
        switch (dialog.Result)
        {
            case UnsavedChangesResult.Save:    vm.SaveAndProceed();    break;
            case UnsavedChangesResult.Discard: vm.DiscardAndProceed(); break;
            default:
                _closingConfirmed = false;
                vm.CancelPendingNavigation();
                break;
        }
    }

    // ── Save-before-apply guard ───────────────────────────────────────────
    // Returns true only if the user chooses Save; Discard/Cancel both abort the bring-in.
    private async Task<bool> ShowSaveBeforeApplyDialogAsync(MainWindowViewModel vm)
    {
        var dialog = new UnsavedChangesDialog(vm.CurrentConversationName ?? "This project");
        await dialog.ShowDialog(this);
        return dialog.Result == UnsavedChangesResult.Save;
    }

    // ── Patch conflict resolution dialog ─────────────────────────────────
    private async Task<bool> ShowConflictResolutionDialogAsync(DialogEditor.Patch.PatchConflictException ex)
    {
        var dialog = new ConflictResolutionDialog(ex);
        await dialog.ShowDialog(this);
        return dialog.ForceApply;
    }

    // ── UI string translation workflow ────────────────────────────────────
    private async void ExportUiStrings_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        var picker = new AvaloniaFilePicker(this);
        var path = await picker.PickSaveFileAsync(
            Loc.Get("Menu_ExportUiStrings"), "ui-strings.csv", ".csv", "CSV files");
        if (path is null) { vm.StatusText = Loc.Get("UiExport_Cancelled"); return; }

        var assetUris = new[]
        {
            ("Strings.axaml",       new Uri("avares://DialogEditor.Avalonia/Resources/Strings.axaml")),
            ("SharedStrings.axaml", new Uri("avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml")),
        };
        var streams = assetUris
            .Select(a => (a.Item1, AssetLoader.Open(a.Item2)))
            .ToList();
        try
        {
            UiStringExportService.Export(streams, path);
            vm.StatusText = Loc.Format("UiExport_Success", path);
        }
        catch (Exception ex)
        {
            AppLog.Error("UI string export failed", ex);
            vm.StatusText = ex.Message;
        }
        finally
        {
            foreach (var (_, stream) in streams) stream.Dispose();
        }
    }

    private async void ImportUiStrings_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        var filePicker = new AvaloniaFilePicker(this);
        var csvPath = await filePicker.PickOpenFileAsync(
            Loc.Get("Menu_ImportUiStrings"), ".csv", "CSV files");
        if (csvPath is null) { vm.StatusText = Loc.Get("UiImport_Cancelled"); return; }

        var lang = UiStringImportService.DetectLanguage(csvPath);
        if (lang is null)
        {
            var dialog = new LanguageCodeDialog(null);
            await dialog.ShowDialog(this);
            lang = dialog.Result;
        }
        if (lang is null) { vm.StatusText = Loc.Get("UiImport_Cancelled"); return; }

        var folderPicker = new AvaloniaFolderPicker(this);
        var outputDir = await folderPicker.PickFolderAsync(Loc.Get("UiImport_FolderTitle"));
        if (outputDir is null) { vm.StatusText = Loc.Get("UiImport_Cancelled"); return; }

        try
        {
            UiStringImportService.Import(csvPath, lang, outputDir);
            vm.StatusText = Loc.Format("UiImport_Success", lang, outputDir);
        }
        catch (Exception ex)
        {
            AppLog.Error("UI string import failed", ex);
            vm.StatusText = ex.Message;
        }
    }

    // ── Mod bundle export ─────────────────────────────────────────────────
    private async void ExportModBundle_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        if (vm.ProjectPath is null) return;

        var picker        = new AvaloniaFilePicker(this);
        var suggestedName = Path.GetFileNameWithoutExtension(vm.ProjectPath) + ".dialogpack";
        var outputPath    = await picker.PickSaveFileAsync(
            Loc.Get("Menu_ExportModBundle"), suggestedName, ".dialogpack", "Dialog Pack");
        if (outputPath is null) return;

        try
        {
            await VoPackExporter.ExportAsync(vm.ProjectPath, outputPath);
            vm.StatusText = Loc.Format("Status_ExportModBundleSuccess", outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("Export mod bundle failed", ex);
            vm.StatusText = Loc.Format("Status_ExportModBundleError", ex.Message);
        }
    }

    // ── New conversation name dialog ──────────────────────────────────────
    private async Task<string?> PromptConversationNameAsync(string? defaultValue = null)
    {
        var dialog = new ConversationNameDialog(defaultValue);
        await dialog.ShowDialog(this);
        return dialog.Result;
    }

    // ── Guided tour adorner ───────────────────────────────────────────────

    /// Maps a step's opaque TargetName to (a) the Dock tool that must be on screen before
    /// the target can exist, and (b) how to find the live Control once it does.
    ///
    /// The first three targets are Dock-hosted tool content. They have no x:Name in this
    /// window's scope, so FindControl can never see them — they are found by view type
    /// instead. Anything else falls through to the classic named-control lookup, which is
    /// still correct for chrome that lives directly in MainWindow.axaml (HelpToggle).
    private (string? DockId, Func<Control?> Resolve) ResolveTourTarget(string targetName) =>
        targetName switch
        {
            "BrowserPanel" => (EditorDockFactory.BrowserId, () => FindBrowserView()),
            "CanvasView"   => (EditorDockFactory.CanvasId,  () => FindCanvasView()),
            "DetailPanel"  => (EditorDockFactory.DetailsId, () => FindDetailView()),
            _              => (null, () => this.FindControl<Control>(targetName)),
        };

    private void OnTourStepChanged()
    {
        RemoveTourAdorner();
        CancelTourRealiseWatch();   // a watcher still hunting the previous step is now stale

        var vm = (MainWindowViewModel)DataContext!;
        if (!vm.Tour.IsVisible) return;

        var (dockId, resolve) = ResolveTourTarget(vm.Tour.CurrentStep.TargetName);

        // Reveal before highlighting. The tour is onboarding: a step describing the Node
        // Details pane is worthless if the user closed that tab, and ShowToolById already
        // handles both cases (RestoreDockable for a closed tab, SetActiveDockable for one
        // that is merely the inactive sibling in a tab group).
        if (dockId is not null) ShowToolById(dockId);

        if (resolve() is { } target)
        {
            AttachTourAdorner(target);
            return;
        }

        // Not realised yet. Dock materialises tool content lazily, so a tool revealed a
        // moment ago has no Control to adorn until the next layout pass — the same timing
        // problem WireCanvasFocusHopWhenRealised solves for the canvas→detail focus hop.
        if (dockId is not null) AttachTourAdornerWhenRealised(resolve);
    }

    /// Waits for Dock to realise a just-revealed tool's content, then rings it. Self-detaches
    /// on success; a step change detaches it via CancelTourRealiseWatch. If the content never
    /// appears (the tool was floated into a window that has since closed, say) the watcher is
    /// simply dropped at the next step — a missing ring, never a crash.
    private void AttachTourAdornerWhenRealised(Func<Control?> resolve)
    {
        _tourRealiseHandler = (_, _) =>
        {
            if (resolve() is not { } target) return;   // still deferred — keep listening
            CancelTourRealiseWatch();
            AttachTourAdorner(target);
        };
        LayoutUpdated += _tourRealiseHandler;
    }

    private void CancelTourRealiseWatch()
    {
        if (_tourRealiseHandler is null) return;
        LayoutUpdated -= _tourRealiseHandler;
        _tourRealiseHandler = null;
    }

    private void AttachTourAdorner(Control target)
    {
        // A floated tool lives in its own TopLevel, so the adorner layer must come from the
        // target's own tree rather than this window's.
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null) return;

        _tourAdorner = new TourHighlightAdorner();
        AdornerLayer.SetAdornedElement(_tourAdorner, target);
        layer.Children.Add(_tourAdorner);
        _tourTarget = target;
    }

    private void RemoveTourAdorner()
    {
        if (_tourTarget is null || _tourAdorner is null) return;
        AdornerLayer.GetAdornerLayer(_tourTarget)?.Children.Remove(_tourAdorner);
        _tourTarget  = null;
        _tourAdorner = null;
    }
}
