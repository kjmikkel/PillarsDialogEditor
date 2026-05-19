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

    // Set to true immediately before a programmatic Close() call so that
    // the re-entrant OnClosing doesn't show the dirty-close dialog again.
    private bool _closingConfirmed = false;

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
        vm.RequestConversationName = () => PromptConversationNameAsync();

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
        var tcs = new TaskCompletionSource<string>();

        // Resolve localised strings
        var title   = (string)(this.FindResource("UnsavedChanges_Title") ?? "Unsaved Changes");
        var message = string.Format(
            (string)(this.FindResource("UnsavedChanges_Message") ?? "{0} has unsaved changes."),
            vm.CurrentConversationName ?? "This conversation");
        var lblSave    = (string)(this.FindResource("UnsavedChanges_Save")    ?? "Save to Project");
        var lblDiscard = (string)(this.FindResource("UnsavedChanges_Discard") ?? "Discard Changes");
        var lblCancel  = (string)(this.FindResource("UnsavedChanges_Cancel")  ?? "Stay Here");

        var dialog = new Window
        {
            Title  = title,
            Icon   = Icon,
            Width  = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#252525")),
        };

        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text         = message,
            Foreground   = new SolidColorBrush(Color.Parse("#e8e8e8")),
            FontSize     = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 20),
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        Button MakeBtn(string label, string result, string bg = "#333")
        {
            var btn = new Button
            {
                Content         = label,
                Padding         = new Thickness(16, 6),
                Background      = new SolidColorBrush(Color.Parse(bg)),
                Foreground      = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize        = 12,
            };
            btn.Click += (_, _) => { tcs.TrySetResult(result); dialog.Close(); };
            return btn;
        }

        buttons.Children.Add(MakeBtn(lblCancel,  "cancel"));
        buttons.Children.Add(MakeBtn(lblDiscard, "discard"));
        buttons.Children.Add(MakeBtn(lblSave,    "save", "#1a5276"));

        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(this);

        switch (await tcs.Task)
        {
            case "save":    vm.SaveAndProceed();          break;
            case "discard": vm.DiscardAndProceed();       break;
            default:
                _closingConfirmed = false;   // user chose Stay Here — reset so next close attempt works
                vm.CancelPendingNavigation();
                break;
        }
    }

    // ── New conversation name dialog ──────────────────────────────────────
    private async Task<string?> PromptConversationNameAsync()
    {
        var title       = (string)(this.FindResource("Dialog_NewConversation_Title")       ?? "New Conversation");
        var prompt      = (string)(this.FindResource("Dialog_NewConversation_Prompt")      ?? "Conversation name:");
        var placeholder = (string)(this.FindResource("Dialog_NewConversation_Placeholder") ?? "my_new_conversation");
        var lblCreate   = (string)(this.FindResource("Dialog_NewConversation_Create")      ?? "Create");

        var tcs = new TaskCompletionSource<string?>();

        var dialog = new Window
        {
            Title  = title,
            Icon   = Icon,
            Width  = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#252525")),
        };

        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text         = prompt,
            Foreground   = new SolidColorBrush(Color.Parse("#e8e8e8")),
            FontSize     = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 10),
        });

        var nameBox = new TextBox
        {
            Watermark       = placeholder,
            Background      = new SolidColorBrush(Color.Parse("#141414")),
            Foreground      = new SolidColorBrush(Color.Parse("#e8e8e8")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#444")),
            BorderThickness = new Thickness(1),
            FontSize        = 12,
            Padding         = new Thickness(6, 4),
            Margin          = new Thickness(0, 0, 0, 16),
        };
        panel.Children.Add(nameBox);

        var buttons = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing             = 8,
        };

        var okBtn = new Button
        {
            Content         = lblCreate,
            Padding         = new Thickness(16, 6),
            Background      = new SolidColorBrush(Color.Parse("#1a5276")),
            Foreground      = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize        = 12,
        };
        okBtn.Click += (_, _) => { tcs.TrySetResult(nameBox.Text); dialog.Close(); };

        var cancelBtn = new Button
        {
            Content         = "Cancel",
            Padding         = new Thickness(16, 6),
            Background      = new SolidColorBrush(Color.Parse("#333")),
            Foreground      = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize        = 12,
        };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(null); dialog.Close(); };

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(okBtn);
        panel.Children.Add(buttons);

        dialog.Content = panel;

        // Focus the TextBox and allow Enter to confirm
        dialog.Opened += (_, _) => nameBox.Focus();
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)  { tcs.TrySetResult(nameBox.Text); dialog.Close(); }
            if (e.Key == Key.Escape) { tcs.TrySetResult(null);         dialog.Close(); }
        };

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }
}
