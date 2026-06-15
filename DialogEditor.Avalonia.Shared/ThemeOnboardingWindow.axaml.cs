using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Shared;

public partial class ThemeOnboardingWindow : Window
{
    public ThemeOnboardingWindow()
    {
        InitializeComponent();
        ThemePicker.DataContext = new ThemePickerViewModel(new ThemeApplier());
        HintBar.AttachTo(this);
        ContinueButton.Click += OnContinueClick;
    }

    private void OnContinueClick(object? sender, RoutedEventArgs e) => Close();
}
