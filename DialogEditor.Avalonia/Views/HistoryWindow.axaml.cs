using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class HistoryWindow : Window
{
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public HistoryWindow() => InitializeComponent();

    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
}
