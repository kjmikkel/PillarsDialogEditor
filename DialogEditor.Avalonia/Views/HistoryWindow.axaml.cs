using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class HistoryWindow : Window
{
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public HistoryWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        HintBar.AttachTo(this);
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
}
