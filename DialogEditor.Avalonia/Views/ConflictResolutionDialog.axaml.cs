using Avalonia.Controls;
using DialogEditor.Patch;

namespace DialogEditor.Avalonia.Views;

public partial class ConflictResolutionDialog : Window
{
    public bool ForceApply { get; private set; }

    // Parameterless ctor so the XAML resource is reachable via the runtime loader (avoids AVLN3001).
    public ConflictResolutionDialog()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public ConflictResolutionDialog(PatchConflictException ex)
    {
        InitializeComponent();
        HintBar.AttachTo(this);
        NodeIdBlock.Text    = ex.NodeId.ToString();
        FieldNameBlock.Text = ex.FieldName;
        ExpectedBlock.Text  = ex.ExpectedFrom;
        ActualBlock.Text    = ex.ActualValue;

        ForceButton.Click  += (_, _) => { ForceApply = true;  Close(); };
        CancelButton.Click += (_, _) => { ForceApply = false; Close(); };
    }
}
