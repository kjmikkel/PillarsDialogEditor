using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class NodeDetailView : UserControl
{
    public NodeDetailView() => InitializeComponent();

    private async void EditConditions_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NodeDetailViewModel detailVm) return;
        if (detailVm.Node is null) return;

        var editorVm = new ConditionEditorViewModel(detailVm.Node);
        var window   = new ConditionEditorWindow(editorVm);

        // Show as modal dialog parented to the owning window
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is not null)
            await window.ShowDialog(owner);
        else
            window.Show();

        // Update the summary after the dialog closes
        detailVm.NotifyConditionSummary();
    }
}
