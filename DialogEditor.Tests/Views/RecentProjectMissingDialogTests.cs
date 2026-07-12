using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class RecentProjectMissingDialogTests
{
    public RecentProjectMissingDialogTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void MessageText_IsSetFromCtorArgument()
    {
        var dlg = new RecentProjectMissingDialog("could not be found: X");
        var msg = dlg.FindControl<TextBlock>("MessageText")!;
        Assert.Equal("could not be found: X", msg.Text);
    }

    [AvaloniaFact]
    public void DefaultResult_IsKeep()
    {
        var dlg = new RecentProjectMissingDialog("x");
        Assert.False(dlg.Result); // false = keep, until the user clicks Remove
    }
}
