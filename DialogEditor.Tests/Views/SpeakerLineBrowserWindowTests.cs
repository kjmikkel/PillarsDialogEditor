using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Views;

public class SpeakerLineBrowserWindowTests
{
    public SpeakerLineBrowserWindowTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Window_Constructs_WithVm()
    {
        var provider = new FakeGameDataProvider("poe2", "en");
        var rows = new[]
        {
            new SpeakerLineRow("g1", "c1", 1, LineVariant.Default, "hello", LineOrigin.Vanilla),
        };
        var vm = new SpeakerLineBrowserViewModel(
            DialogProject.Empty("P"), provider, "en", () => (null, null), null,
            scanner: (_, _, _) => rows);

        var window = new SpeakerLineBrowserWindow(vm);

        Assert.NotNull(window);
        Assert.Same(vm, window.DataContext);
    }
}
