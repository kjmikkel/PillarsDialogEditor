using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class AutosaveRestoreDialogTests
{
    public AutosaveRestoreDialogTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Constructs_WithTimestamp_DefaultResultIsDiscard()
    {
        var dialog = new AutosaveRestoreDialog(new DateTime(2026, 7, 12, 10, 30, 0));
        dialog.Show();
        Assert.True(dialog.IsVisible);
        Assert.False(dialog.Result); // closing without a choice = discard the offer
        dialog.Close();
    }
}
