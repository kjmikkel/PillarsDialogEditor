using System;
using Avalonia;
using Avalonia.Automation;
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

    // Tracks the ConversationView instance whose FocusDetailRequested we've already
    // hooked, so re-running BuildDock (e.g. opening a second game folder in the same
    // session) rewires the new Dock-realised instance without leaving the old one's
    // now-orphaned subscription behind or double-subscribing the same instance twice.
    private ConversationView? _wiredCanvasView;

    // Bounds TryWireCanvasFocusHop's retry loop below — a handful of dispatcher passes
    // is normally more than enough for Dock to realise the tool content; giving up
    // afterwards avoids spinning forever if the Canvas tool is never shown (e.g. closed
    // by the user before a game folder ever loads it, or a future layout omits it).
    private const int MaxCanvasWireAttempts = 20;

    /// Builds the default docking layout (Conversations/Canvas/Node Details/Condition
    /// search) once the shell-level ConditionSearchViewModel exists — i.e. once a game
    /// folder has loaded (see MainWindowViewModel.RebuildConditionSearch). Also re-wires
    /// the canvas → detail-pane focus hop, which now targets the Dock-hosted views instead
    /// of named XAML controls (the fixed 5-column grid is gone).
    private void BuildDock(MainWindowViewModel vm)
    {
        if (vm.ConditionSearch is null) return;   // needs a loaded game

        var factory = new EditorDockFactory(vm.Browser, vm.Canvas, vm.Detail, vm.ConditionSearch);
        var layout  = factory.CreateLayout();
        factory.InitLayout(layout);
        vm.DockLayout = layout;
        _factory = factory;

        // The document/tool content is realised by Application.DataTemplates once Dock
        // renders the new layout, not synchronously here — retry across dispatcher
        // passes (bounded) until the live ConversationView instance exists to hook.
        TryWireCanvasFocusHop(MaxCanvasWireAttempts);
    }

    /// Finds the live ConversationView and, once found, hooks its FocusDetailRequested
    /// event — resolving the target NodeDetailView lazily, at fire time, so a
    /// not-yet-realised/closed/floated Detail tool degrades to a no-op instead of an
    /// NRE. If the view isn't realised yet, reposts itself for the next dispatcher pass
    /// (up to <paramref name="attemptsLeft"/> times) instead of giving up silently.
    private void TryWireCanvasFocusHop(int attemptsLeft)
    {
        if (FindCanvasView() is not { } canvasView)
        {
            if (attemptsLeft <= 1)
            {
                AppLog.Warn("MainWindow: gave up waiting for ConversationView to realise; " +
                            "canvas -> detail focus hop (Enter on a node) will not work this session.");
                return;
            }
            Dispatcher.UIThread.Post(() => TryWireCanvasFocusHop(attemptsLeft - 1), DispatcherPriority.Loaded);
            return;
        }

        if (ReferenceEquals(canvasView, _wiredCanvasView)) return;   // already hooked
        _wiredCanvasView = canvasView;
        canvasView.FocusDetailRequested += (_, _) => FindDetailView()?.FocusFirstField();
    }

    /// Locates the live ConversationView hosted by the Dock canvas document. Dock
    /// instantiates it lazily from Application.DataTemplates, so this is a lookup
    /// (not a cached field) — safe to call any time after BuildDock has run.
    private ConversationView? FindCanvasView() =>
        this.GetVisualDescendants().OfType<ConversationView>().FirstOrDefault();

    /// Locates the live NodeDetailView hosted by the Dock details tool (same caveat as
    /// <see cref="FindCanvasView"/> — it may be null if the tool is closed/floated away).
    private NodeDetailView? FindDetailView() =>
        this.GetVisualDescendants().OfType<NodeDetailView>().FirstOrDefault();

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
                vm.ActiveGameId);

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
    private void OnTourStepChanged()
    {
        RemoveTourAdorner();

        var vm = (MainWindowViewModel)DataContext!;
        if (!vm.Tour.IsVisible) return;

        var step   = vm.Tour.CurrentStep;
        var target = this.FindControl<Control>(step.TargetName);
        if (target is null) return;

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

    // NOTE (Docking Shell Phase 1 orphan — see task-4-report.md): guided-tour steps that
    // targeted the old fixed "BrowserPanel"/"DetailPanel" grid names no longer resolve —
    // those controls were replaced by Dock-hosted tool content with no compile-time
    // x:Name. OnTourStepChanged's FindControl(...) already no-ops gracefully (returns
    // null, step highlight silently skipped) rather than throwing, but those tour steps
    // need re-targeting at dockable tools in a follow-up. Tracked for Task 6+ /Gaps.md.
}
