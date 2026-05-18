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

        var editorVm = new ConditionEditorViewModel(detailVm.Node, detailVm.ActiveGameId);
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

    private async void EditScripts_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NodeDetailViewModel detailVm) return;
        if (detailVm.Node is null) return;

        var editorVm = new ScriptEditorViewModel(detailVm.Node);
        var window   = new ScriptEditorWindow(editorVm);
        var owner    = TopLevel.GetTopLevel(this) as Window;
        if (owner is not null)
            await window.ShowDialog(owner);
        else
            window.Show();

        detailVm.NotifyScriptSummary();
    }

    private async void LinkConditions_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not ConnectionViewModel conn) return;
        if (DataContext is not NodeDetailViewModel detailVm) return;

        var title    = $"Link → {conn.Target.GetNodeId()}";
        var editorVm = new ConditionEditorViewModel(
            title,
            conn.Conditions,
            conditions => conn.Conditions = conditions,
            detailVm.ActiveGameId);

        var window = new ConditionEditorWindow(editorVm);
        var owner  = TopLevel.GetTopLevel(this) as Window;
        if (owner is not null)
            await window.ShowDialog(owner);
        else
            window.Show();
    }
}
