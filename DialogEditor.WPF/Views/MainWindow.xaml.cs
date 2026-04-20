using System.Windows;
using DialogEditor.WPF.ViewModels;

namespace DialogEditor.WPF.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
