using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class AboutWindowTests
{
    public AboutWindowTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Constructs_AndShows()
    {
        var vm = new AboutViewModel("1.2.3", "https://repo", "https://docs");
        var window = new AboutWindow(vm);
        window.Show();
        Assert.True(window.IsVisible);
        window.Close();
    }
}
