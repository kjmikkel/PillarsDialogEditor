using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class ConditionEditorWindow : Window
{
    // Parameterless constructor required by Avalonia's XAML compiler.
    public ConditionEditorWindow() => InitializeComponent();

    public ConditionEditorWindow(ConditionEditorViewModel vm) : this()
    {
        DataContext = vm;
        vm.Confirmed += Close;
        vm.Cancelled += Close;
    }
}
