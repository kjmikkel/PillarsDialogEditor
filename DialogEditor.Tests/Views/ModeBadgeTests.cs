using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Docking;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Views;

/// Issue 234 (Task 9): the mode badge at the right end of the main toolbar. It shows the mode in
/// words and a glyph (never colour alone), and clicking it switches mode.
public class ModeBadgeTests : IDisposable
{
    private readonly string _settingsPath;

    public ModeBadgeTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mbt_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static string R(string key) => (string)Application.Current!.FindResource(key)!;

    private static Color BrushColor(string key) =>
        ((ISolidColorBrush)Application.Current!.FindResource(key)!).Color;

    private static (MainWindow window, MainWindowViewModel vm) Show(EditorMode mode)
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Mode = mode;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static Button Badge(MainWindow w) => w.FindControl<Button>("ModeBadge")!;

    private static string VisibleText(Button badge) =>
        string.Join(" ", badge.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible).Select(t => t.Text));

    private static string AutoName(Control c) => ControlAutomationPeer.CreatePeerForElement(c).GetName();

    [AvaloniaFact]
    public void Badge_SaysReading_InReadingMode()
    {
        var (window, _) = Show(EditorMode.Reading);
        try
        {
            var badge = Badge(window);
            var text = VisibleText(badge);
            Assert.Contains(R("ModeBadge_Reading"), text);
            Assert.Contains(R("ModeBadge_ReadingGlyph"), text);
            Assert.DoesNotContain(R("ModeBadge_Editing"), text);
            Assert.Equal(R("AutomationName_ModeBadge_Reading"), AutoName(badge));
            Assert.Equal(R("ToolTip_ModeBadge_Reading"), ToolTip.GetTip(badge));
            Assert.Equal(R("ToolTip_ModeBadge_Reading"), AutomationProperties.GetHelpText(badge));
            Assert.Equal(BrushColor("Brush.ModeBadge.Reading.Background"), ((ISolidColorBrush)badge.Background!).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Badge_SaysEditing_InEditingMode()
    {
        var (window, _) = Show(EditorMode.Editing);
        try
        {
            var badge = Badge(window);
            var text = VisibleText(badge);
            Assert.Contains(R("ModeBadge_Editing"), text);
            Assert.Contains(R("ModeBadge_EditingGlyph"), text);
            Assert.DoesNotContain(R("ModeBadge_Reading"), text);
            Assert.Equal(R("AutomationName_ModeBadge_Editing"), AutoName(badge));
            Assert.Equal(R("ToolTip_ModeBadge_Editing"), ToolTip.GetTip(badge));
            Assert.Equal(R("ToolTip_ModeBadge_Editing"), AutomationProperties.GetHelpText(badge));
            Assert.Equal(BrushColor("Brush.ModeBadge.Editing.Background"), ((ISolidColorBrush)badge.Background!).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Badge_FollowsTheModeWhileTheWindowIsOpen()
    {
        var (window, vm) = Show(EditorMode.Editing);
        try
        {
            vm.Mode = EditorMode.Reading;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(R("AutomationName_ModeBadge_Reading"), AutoName(Badge(window)));
            vm.Mode = EditorMode.Editing;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(R("AutomationName_ModeBadge_Editing"), AutoName(Badge(window)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Badge_IsTheLastToolbarControl()
    {
        var (window, _) = Show(EditorMode.Editing);
        try
        {
            var badge = Badge(window);
            var help = window.FindControl<ToggleButton>("HelpToggle")!;
            Assert.Same(help.Parent, badge.Parent);
            var siblings = ((Panel)badge.Parent!).Children;
            Assert.Equal(siblings.Count - 1, siblings.IndexOf(badge));
            Assert.True(badge.TranslatePoint(default, window)!.Value.X > help.TranslatePoint(default, window)!.Value.X);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClickingTheBadge_Switches()
    {
        var (window, vm) = Show(EditorMode.Editing);
        try
        {
            Badge(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(EditorMode.Reading, vm.Mode);

            Badge(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(EditorMode.Editing, vm.Mode);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClickingTheBadge_CommitsAHalfTypedDetailEdit_First()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        try
        {
            var detail = new NodeDetailView { DataContext = vm.Detail };
            window.GetVisualDescendants().OfType<Panel>().First().Children.Add(detail);
            var node = new ConversationNode(1, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None");
            vm.Canvas.Load(new Conversation("c", [node], new StringTable([new StringEntry(1, "hi", "")])));
            vm.CurrentConversationName = "c";
            vm.Canvas.SelectedNode = vm.Canvas.Nodes[0];
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsModified);

            var box = detail.FindControl<TextBox>("DefaultTextBox")!;
            Assert.True(box.Focus());
            window.KeyTextInput("!");

            // Drop the window's own handler (it opens the real modal dialog) and answer Cancel instead.
            typeof(MainWindowViewModel).GetField(nameof(MainWindowViewModel.UnsavedChangesRequested),
                    BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, null);
            bool? modifiedWhenAsked = null;
            vm.UnsavedChangesRequested += () => { modifiedWhenAsked = vm.IsModified; vm.CancelPendingNavigation(); };
            Badge(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(modifiedWhenAsked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Badge_HasATooltip_AndIsAFocusableTabStop()
    {
        var (window, _) = Show(EditorMode.Editing);
        try
        {
            var badge = Badge(window);
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(badge) as string));
            Assert.True(badge.Focusable);
            Assert.True(badge.IsTabStop);
            Assert.True(badge.IsEffectivelyEnabled);
            Assert.True(badge.Focus());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TourTarget_ModeBadge_ResolvesToTheBadge()
    {
        var (window, _) = Show(EditorMode.Reading);
        try { Assert.Same(Badge(window), TourTargetResolver.Resolve("ModeBadge", window, [])); }
        finally { window.Close(); }
    }
}
