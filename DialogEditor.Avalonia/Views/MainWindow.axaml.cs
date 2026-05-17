using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DialogEditor.Avalonia.Services;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class MainWindow : Window
{
    private double _browserExpandedWidth = 220;
    private double _detailExpandedWidth  = 240;
    private LegendWindow? _legendWindow;

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

    private async Task ShowUnsavedChangesDialogAsync(MainWindowViewModel vm)
    {
        var tcs = new TaskCompletionSource<string>();

        var dialog = new Window
        {
            Title  = "Unsaved Changes",
            Width  = 380,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#2d2d2d")),
            SystemDecorations = SystemDecorations.BorderOnly
        };

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock
        {
            Text = "You have unsaved changes. Save before switching conversations?",
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };

        void MakeBtn(string label, string result)
        {
            var btn = new Button
            {
                Content = label,
                Margin  = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 4),
                Background = new SolidColorBrush(Color.Parse("#444")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btn.Click += (_, _) => { tcs.TrySetResult(result); dialog.Close(); };
            buttons.Children.Add(btn);
        }

        MakeBtn("Save",    "save");
        MakeBtn("Discard", "discard");
        MakeBtn("Cancel",  "cancel");

        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(this);

        var choice = await tcs.Task;
        switch (choice)
        {
            case "save":    vm.SaveAndProceed();          break;
            case "discard": vm.DiscardAndProceed();       break;
            default:        vm.CancelPendingNavigation(); break;
        }
    }
}
