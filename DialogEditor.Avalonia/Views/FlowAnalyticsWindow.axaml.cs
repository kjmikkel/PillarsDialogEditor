using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class FlowAnalyticsWindow : Window
{
    public FlowAnalyticsWindow() => InitializeComponent();

    public FlowAnalyticsWindow(FlowAnalyticsViewModel vm) : this()
    {
        DataContext = vm;
    }
}
