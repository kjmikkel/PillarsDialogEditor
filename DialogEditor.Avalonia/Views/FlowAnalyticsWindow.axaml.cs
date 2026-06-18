using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class FlowAnalyticsWindow : Window
{
    public FlowAnalyticsWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public FlowAnalyticsWindow(FlowAnalyticsViewModel vm) : this()
    {
        DataContext = vm;
    }
}
