using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Views;

public partial class ConditionEditorWindow : Window
{
    public ConditionEditorWindow() => InitializeComponent();

    public ConditionEditorWindow(ConditionEditorViewModel vm) : this()
    {
        DataContext = vm;
        vm.Confirmed += Close;
        vm.Cancelled += Close;
    }

    private async void EditBranchGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not ConditionRowViewModel branchRow || !branchRow.IsBranch) return;
        if (DataContext is not ConditionEditorViewModel parentVm) return;

        var subVm = new ConditionEditorViewModel(
            Loc.Get("Label_BranchGroupTitle"),
            branchRow.BranchComponents,
            updated => branchRow.UpdateBranchComponents(updated),
            parentVm.GameId);

        var subWindow = new ConditionEditorWindow(subVm);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is not null)
            await subWindow.ShowDialog(owner);
        else
            subWindow.Show();
    }
}
