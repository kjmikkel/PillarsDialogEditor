using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class GitConflictResolutionWindow : Window
{
    // Parameterless constructor required so the XAML compiler embeds this type
    // (avoids AVLN3000 wiping precompiled resources on a clean build).
    public GitConflictResolutionWindow() => InitializeComponent();

    public GitConflictResolutionWindow(GitConflictResolutionViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += Close;
        CancelButton.Click += (_, _) => Close();
    }
}
