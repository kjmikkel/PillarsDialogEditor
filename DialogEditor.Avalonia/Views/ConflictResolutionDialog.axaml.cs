using Avalonia.Controls;
using DialogEditor.Patch;

namespace DialogEditor.Avalonia.Views;

public partial class ConflictResolutionDialog : Window
{
    public bool ForceApply { get; private set; }

    public ConflictResolutionDialog(PatchConflictException ex)
    {
        InitializeComponent();
        NodeIdBlock.Text    = ex.NodeId.ToString();
        FieldNameBlock.Text = ex.FieldName;
        ExpectedBlock.Text  = ex.ExpectedFrom;
        ActualBlock.Text    = ex.ActualValue;

        ForceButton.Click  += (_, _) => { ForceApply = true;  Close(); };
        CancelButton.Click += (_, _) => { ForceApply = false; Close(); };
    }
}
