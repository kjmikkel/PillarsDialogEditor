using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DialogEditor.Avalonia.Audio;
using DialogEditor.Avalonia.Controls;
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
    private double _browserExpandedWidth = 220;
    private double _detailExpandedWidth  = 240;
    private LegendWindow?          _legendWindow;
    private PatchManagerWindow?    _patchManagerWindow;
    private FindReplaceWindow?     _findReplaceWindow;
    private BatchReplaceWindow?    _batchReplaceWindow;
    private FlowAnalyticsWindow?   _flowAnalyticsWindow;
    private VoValidationWindow?    _voValidationWindow;

    // Tour adorner state — one adorner on one target at a time.
    private TourHighlightAdorner? _tourAdorner;
    private Control?              _tourTarget;

    // Set to true immediately before a programmatic Close() call so that
    // the re-entrant OnClosing doesn't show the dirty-close dialog again.
    private bool _closingConfirmed = false;

    // Guards the one-time startup project re-open in OnOpened.
    private bool _startupDone = false;

    private ColumnDefinition BrowserColumn => ContentGrid.ColumnDefinitions[0];
    private ColumnDefinition DetailColumn  => ContentGrid.ColumnDefinitions[4];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(
            new AvaloniaDispatcher(),
            new AvaloniaFolderPicker(this),
            new AvaloniaFilePicker(this));

        var vm = (MainWindowViewModel)DataContext;
        vm.Tour.StepChanged += OnTourStepChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
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
        vm.ReportSaveError = ex =>
            (Application.Current as App)?.ShowExceptionReport(ex);
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

        if (!vm.IsBrowserExpanded)
        {
            BrowserColumn.MinWidth = 34;
            BrowserColumn.Width = new GridLength(34);
        }
        if (!vm.IsDetailExpanded)
        {
            DetailColumn.MinWidth = 34;
            DetailColumn.Width = new GridLength(34);
        }

        CanvasView.FocusDetailRequested += (_, _) =>
        {
            vm.IsDetailExpanded = true;        // panel may be collapsed — open it first
            DetailView.FocusFirstField();
        };

        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        this.AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.IsBrowserExpanded):
                if (vm.IsBrowserExpanded)
                {
                    BrowserColumn.MinWidth = 150;
                    BrowserColumn.Width = new GridLength(_browserExpandedWidth);
                }
                else
                {
                    _browserExpandedWidth = BrowserColumn.Width.Value;
                    BrowserColumn.MinWidth = 34;
                    BrowserColumn.Width = new GridLength(34);
                }
                break;

            case nameof(MainWindowViewModel.IsDetailExpanded):
                if (vm.IsDetailExpanded)
                {
                    DetailColumn.MinWidth = 180;
                    DetailColumn.Width = new GridLength(_detailExpandedWidth);
                }
                else
                {
                    _detailExpandedWidth = DetailColumn.Width.Value;
                    DetailColumn.MinWidth = 34;
                    DetailColumn.Width = new GridLength(34);
                }
                break;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var vm = (MainWindowViewModel)DataContext!;

        if (vm.IsBrowserFlyoutOpen)
        {
            var pos = e.GetPosition(BrowserPanel);
            bool outsidePanel = pos.X < 0 || pos.Y < 0
                             || pos.X > BrowserPanel.Bounds.Width
                             || pos.Y > BrowserPanel.Bounds.Height;
            if (outsidePanel)
                vm.IsBrowserExpanded = false;
        }
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
                CanvasView.FocusSearch();
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

            case Key.N when e.KeyModifiers == KeyModifiers.Control:
                vm.NewProjectCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.O when e.KeyModifiers == KeyModifiers.Control:
                vm.OpenProjectCommand.Execute(null);
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
                    CanvasView.FocusEditor();   // commit a focused TextBox edit first, like Ctrl+S
                vm.SaveProjectAsCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.S when e.KeyModifiers == KeyModifiers.Control:
                if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
                    CanvasView.FocusEditor();
                vm.SaveCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete when vm.Canvas.SelectedNode is not null
                             && e.Source is not TextBox
                             && vm.Canvas.IsEditable:
                vm.Canvas.DeleteNodeCmdCommand.Execute(vm.Canvas.SelectedNode);
                e.Handled = true;
                break;

            case Key.Escape when vm.IsBrowserFlyoutOpen:
                vm.IsBrowserExpanded = false;
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
                    if (node is not null) CanvasView.ScrollToNode(node);
                });

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
        var settings = new SettingsWindow
        {
            DataContext = vm.CreateSettingsViewModel(new FontScaleApplier())
        };
        await settings.ShowDialog(this);
    }

    private void CollapsedBrowserTitle_Click(object? sender, RoutedEventArgs e)
        => ((MainWindowViewModel)DataContext!).IsBrowserExpanded = true;

    private void ToggleDetail_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.IsDetailExpanded = !vm.IsDetailExpanded;
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

        EnsureTourPanelVisible(step.TargetName);

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

    private void EnsureTourPanelVisible(string targetName)
    {
        var vm = (MainWindowViewModel)DataContext!;
        // Expand collapsed panels before the adorner tries to highlight them.
        // CanvasView and HelpToggle are always visible — no action needed for them.
        if (targetName == "BrowserPanel" && !vm.IsBrowserExpanded)
            vm.IsBrowserExpanded = true;
        else if (targetName == "DetailPanel" && !vm.IsDetailExpanded)
            vm.IsDetailExpanded  = true;
    }
}
