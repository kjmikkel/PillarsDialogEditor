using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DialogEditor.Avalonia.Services;
using DialogEditor.Avalonia.Shared.Services;
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
        vm.ShowImportWarnings = async warnings =>
        {
            var dialog = new ImportWarningsDialog(warnings);
            await dialog.ShowDialog(this);
        };
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

        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
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

        _flowAnalyticsWindow.Show();
        _flowAnalyticsWindow.Activate();
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
            DataContext = vm.CreateSettingsViewModel()
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
        ((MainWindowViewModel)DataContext!).ReopenLastProjectOnStartup();
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

    // ── New conversation name dialog ──────────────────────────────────────
    private async Task<string?> PromptConversationNameAsync(string? defaultValue = null)
    {
        var dialog = new ConversationNameDialog(defaultValue);
        await dialog.ShowDialog(this);
        return dialog.Result;
    }
}
