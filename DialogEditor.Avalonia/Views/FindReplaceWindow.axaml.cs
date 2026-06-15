using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class FindReplaceWindow : Window
{
    public FindReplaceWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public FindReplaceWindow(FindReplaceViewModel vm) : this()
    {
        DataContext = vm;
        Opened += (_, _) => SearchBox.Focus();
    }
}
