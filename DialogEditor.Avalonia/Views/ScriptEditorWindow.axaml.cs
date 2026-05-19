using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class ScriptEditorWindow : Window
{
    public ScriptEditorWindow() => InitializeComponent();

    public ScriptEditorWindow(ScriptEditorViewModel vm) : this()
    {
        DataContext = vm;
        vm.Confirmed += Close;
        vm.Cancelled += Close;
    }
}
