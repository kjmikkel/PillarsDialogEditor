using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

/// <summary>
/// GitHub issue 264 (F-18): the minimap had a fixed 160×110 size and couldn't be hidden, so at
/// large text sizes it covered canvas content. It is now sized from the canvas viewport (a fixed
/// share of it, within limits) and View ▸ Show Minimap hides it, remembered across sessions.
/// </summary>
public class MinimapTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"minimap_{Guid.NewGuid():N}.json");

    public MinimapTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    [Theory]
    [InlineData(1000, 700, 180, 124)]   // a share of the viewport width, at the canvas aspect
    [InlineData(400, 300, 120, 83)]     // never smaller than legible
    [InlineData(3000, 2000, 240, 165)]  // never larger than useful
    public void Size_FollowsTheViewport(double w, double h, double expectedW, double expectedH)
    {
        var size = ConversationView.MinimapSize(new Size(w, h));
        Assert.Equal(expectedW, size.Width, 0.5);
        Assert.Equal(expectedH, size.Height, 0.5);
    }

    [AvaloniaFact]
    public void Size_DoesNotGrowWithTheFont()
    {
        var vm = new ConversationViewModel(new StubDispatcher());
        var view = new ConversationView { DataContext = vm };
        var w = new Window { Width = 1000, Height = 700, Content = view };
        try
        {
            new FontScaleApplier().Apply(2.0);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var minimap = view.FindControl<Control>("Minimap")!;
            var editor  = view.FindControl<Control>("Editor")!;
            Assert.Equal(ConversationView.MinimapSize(editor.Bounds.Size).Width, minimap.Width, 0.5);
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
        }
    }

    [AvaloniaFact]
    public void Hidden_WhenShowMinimapIsOff()
    {
        var vm = new ConversationViewModel(new StubDispatcher()) { ShowMinimap = false };
        var view = new ConversationView { DataContext = vm };
        var w = new Window { Content = view };
        w.Show();
        try { Assert.False(view.FindControl<Control>("Minimap")!.IsVisible); }
        finally { w.Close(); }
    }

    [Fact]
    public void Toggle_IsRememberedAndReachesTheCanvas()
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Assert.True(vm.ShowMinimap);
        Assert.True(vm.Canvas.ShowMinimap);

        vm.ShowMinimap = false;

        Assert.False(vm.Canvas.ShowMinimap);
        Assert.False(AppSettings.ShowMinimap);
        Assert.False(new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker()).Canvas.ShowMinimap);
    }

    [Fact]
    public void ViewMenu_HasTheToggle()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx"))) dir = dir.Parent;
        var doc = XDocument.Load(Path.Combine(dir!.FullName, "DialogEditor.Avalonia", "Views", "MainWindow.axaml"));
        var item = doc.Descendants().SingleOrDefault(e => e.Attribute("IsChecked")?.Value == "{Binding ShowMinimap, Mode=TwoWay}");
        Assert.True(item is not null, "no menu item bound to ShowMinimap");
        Assert.Equal("CheckBox", item!.Attribute("ToggleType")?.Value);
        Assert.Equal("{DynamicResource Menu_View_ShowMinimap}", item.Attribute("Header")?.Value);
        Assert.Equal("{DynamicResource ToolTip_View_ShowMinimap}", item.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("{DynamicResource ToolTip_View_ShowMinimap}", item.Attribute("AutomationProperties.HelpText")?.Value);
    }
}
