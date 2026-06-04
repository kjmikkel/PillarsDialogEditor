using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class BlameWindow : Window
{
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public BlameWindow() => InitializeComponent();

    public BlameWindow(BlameViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
}
