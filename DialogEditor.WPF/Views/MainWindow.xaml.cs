using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DialogEditor.WPF.ViewModels;

namespace DialogEditor.WPF.Views;

public partial class MainWindow : Window
{
    private double _browserExpandedWidth = 220;
    private double _detailExpandedWidth  = 240;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        var vm = (MainWindowViewModel)DataContext;
        vm.PropertyChanged += OnVmPropertyChanged;

        // Sync initial column widths with persisted state
        if (!vm.IsBrowserExpanded)
        {
            BrowserColumn.MinWidth = 34;
            BrowserColumn.Width = new System.Windows.GridLength(34);
        }
        if (!vm.IsDetailExpanded)
        {
            DetailColumn.MinWidth = 34;
            DetailColumn.Width = new System.Windows.GridLength(34);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext;
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.IsBrowserExpanded):
                if (vm.IsBrowserExpanded)
                {
                    BrowserColumn.MinWidth = 150;
                    BrowserColumn.Width = new System.Windows.GridLength(_browserExpandedWidth);
                }
                else
                {
                    _browserExpandedWidth = BrowserColumn.Width.Value;
                    BrowserColumn.MinWidth = 34;
                    BrowserColumn.Width = new System.Windows.GridLength(34);
                }
                break;

            case nameof(MainWindowViewModel.IsDetailExpanded):
                if (vm.IsDetailExpanded)
                {
                    DetailColumn.MinWidth = 180;
                    DetailColumn.Width = new System.Windows.GridLength(_detailExpandedWidth);
                }
                else
                {
                    _detailExpandedWidth = DetailColumn.Width.Value;
                    DetailColumn.MinWidth = 34;
                    DetailColumn.Width = new System.Windows.GridLength(34);
                }
                break;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
    }

    private void CloseHelp_Click(object sender, RoutedEventArgs e)
        => HelpToggle.IsChecked = false;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
