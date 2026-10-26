using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.LogicalTree;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Controls;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Views;

/// GitHub issue 262: F6 / Shift+F6 move focus between the panes (Conversations → Canvas →
/// Node Details), the platform's pane key. Ending a test moved to Shift+F5, beside F5.
public class MainWindowPaneCycleTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowPaneCycleTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwpc_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    [Theory]
    [InlineData(-1, false, 0)]   // nothing focused: F6 starts at the first pane
    [InlineData(-1, true, 2)]    // Shift+F6 starts at the last
    [InlineData(0, false, 1)]
    [InlineData(2, false, 0)]    // wraps
    [InlineData(0, true, 2)]
    public void Next_StepsAndWraps(int current, bool backwards, int expected)
        => Assert.Equal(expected, PaneCycle.Next(current, step: 1, count: 3, backwards));

    private static MainWindow Open()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        typeof(MainWindowViewModel).GetField("_activeGameId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, "poe2");
        typeof(MainWindowViewModel).GetMethod("RebuildConditionSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, ["poe2"]);
        typeof(MainWindow).GetField("_dockRestored", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
        typeof(MainWindow).GetMethod("BuildDock", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [vm]);
        var node = new ConversationNode(1, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None");
        vm.Detail.Load(new NodeViewModel(node, new StringEntry(1, "A line.", "")));
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Type? FocusedPane(Window w)
    {
        var focused = w.FocusManager?.GetFocusedElement() as Visual;
        Type[] panes = [typeof(GameBrowserView), typeof(ConversationView), typeof(NodeDetailView)];
        return panes.FirstOrDefault(t => focused?.GetSelfAndVisualAncestors().Any(a => a.GetType() == t) == true);
    }

    [AvaloniaFact]
    public void F6_CyclesConversationsCanvasDetails_AndShiftF6_GoesBack()
    {
        var w = Open();
        try
        {
            void Press(RawInputModifiers m) { w.KeyPress(Key.F6, m, PhysicalKey.F6, null); Dispatcher.UIThread.RunJobs(); }

            Press(RawInputModifiers.None);  Assert.Equal(typeof(GameBrowserView),  FocusedPane(w));
            Press(RawInputModifiers.None);  Assert.Equal(typeof(ConversationView), FocusedPane(w));
            Press(RawInputModifiers.None);  Assert.Equal(typeof(NodeDetailView),   FocusedPane(w));
            Press(RawInputModifiers.None);  Assert.Equal(typeof(GameBrowserView),  FocusedPane(w));
            Press(RawInputModifiers.Shift); Assert.Equal(typeof(NodeDetailView),   FocusedPane(w));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void EndTestMenuItem_ShowsShiftF5()
    {
        var w = Open();
        try
        {
            // Submenu items aren't in the visual tree until opened; the logical tree has them.
            var item = w.GetLogicalDescendants().OfType<MenuItem>()
                .First(m => AutomationProperties.GetAutomationId(m) == "MenuTest_RestoreConversation");
            Assert.Equal(new KeyGesture(Key.F5, KeyModifiers.Shift), item.InputGesture);
        }
        finally { w.Close(); }
    }
}
