using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class BranchesWindow : Window
{
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000/AVLN3001).
    public BranchesWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public BranchesWindow(BranchesViewModel vm) : this()
    {
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
}
