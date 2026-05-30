using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class DiffHelpWindowTests
{
    public DiffHelpWindowTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Constructs_AndShows()
    {
        var window = new DiffHelpWindow();
        window.Show();
        Assert.True(window.IsVisible);
        window.Close();
    }
}
