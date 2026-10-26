using System.Reflection;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;
using Xunit.Abstractions;

namespace DialogEditor.Tests.Views;

public class MainWindowTabOrderTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly ITestOutputHelper _out;

    public MainWindowTabOrderTests(ITestOutputHelper output)
    {
        _out = output;
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwto_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static List<Control> Walk(Window window)
    {
        var stops = new List<Control>();
        IInputElement? current = window;
        for (var i = 0; i < 200; i++)
        {
            current = KeyboardNavigationHandler.GetNext(current!, NavigationDirection.Next);
            if (current is not Control c || stops.Contains(c)) break;
            stops.Add(c);
        }
        return stops;
    }

    private static string Describe(Control c) =>
        $"{c.GetType().Name}('{ControlAutomationPeer.CreatePeerForElement(c).GetName()}')";

    // GitHub issue 255: Tab follows the visual reading order: menu bar, its right-hand buttons,
    // Conversations, the canvas, then Node Details. The ? and ⚙ buttons were declared first in
    // the menu row's DockPanel (so they could dock right), which made them the first stops.
    [AvaloniaFact]
    public void TabOrder_FollowsTheLayout()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        typeof(MainWindowViewModel).GetField("_activeGameId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, "poe2");
        typeof(MainWindowViewModel).GetMethod("RebuildConditionSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, ["poe2"]);
        typeof(MainWindow).GetField("_dockRestored", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
        typeof(MainWindow).GetMethod("BuildDock", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [vm]);
        Dispatcher.UIThread.RunJobs();
        var stops = Walk(window);
        window.Close();
        for (var i = 0; i < stops.Count; i++) _out.WriteLine($"{i + 1}: {Describe(stops[i])}");

        var names = stops.Select(c => ControlAutomationPeer.CreatePeerForElement(c).GetName()).ToList();
        int At(string name)
        {
            var i = names.IndexOf(name);
            Assert.True(i >= 0, $"'{name}' is not a Tab stop: {string.Join(" → ", names)}");
            return i;
        }

        Assert.Equal("File", names[0]);
        var order = new[]
        {
            At("File"), At("Text language:"), At("Settings"), At("Toggle help"),
            At("Filter conversations…"), At("Dock_Doc_Canvas"), At("Dock_Tool_Details"),
        };
        Assert.True(order.SequenceEqual(order.Order()), string.Join(" → ", names));
    }
}
