using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.PatchManager;
using DialogEditor.Patch.Schema;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// Renders the standalone Patch Manager's About window (GitHub issue 79) so a broken
/// SupportedFormats binding fails here rather than showing an empty list to players.
public class PatchManagerAboutWindowTests
{
    public PatchManagerAboutWindowTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Shows_OneRowPerSupportedFormat()
    {
        var window = new PatchManagerAboutWindow(new PatchManagerAboutViewModel("1.2.3"));
        window.Show();

        Assert.True(window.IsVisible);
        Assert.Equal(SchemaFormats.Supported.Count, window.FindControl<ItemsControl>("FormatList")!.ItemCount);
        window.Close();
    }
}
