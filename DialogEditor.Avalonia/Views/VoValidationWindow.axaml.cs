using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class VoValidationWindow : Window
{
    public VoValidationWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public VoValidationWindow(VoValidationViewModel vm) : this()
    {
        DataContext = vm;
    }
}
