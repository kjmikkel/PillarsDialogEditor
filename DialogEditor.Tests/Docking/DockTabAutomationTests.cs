using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using DialogEditor.Avalonia.Docking;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Docking;

/// GitHub issue 264 (F-10): Dock's tab-item peers return a state dump ("Selected=true;
/// Active=false; CanClose=…; DockingState=Document") as their UIA help text and never read
/// AutomationProperties.HelpText, so Narrator reads the dump aloud on every dock tab. Upstream:
/// wieslawsoltes/Dock issue 1201. The app's tabs must expose no such string, and must keep the name,
/// Invoke and SelectionItem behaviour Dock's peer gives them.
public class DockTabAutomationTests
{
    private static (Window window, List<TabStripItem> tabs) ShowLayout()
    {
        Loc.Configure(new StubStringProvider());
        var factory = new EditorDockFactory(
            new GameBrowserViewModel(new StubDispatcher()),
            new ConversationViewModel(new StubDispatcher()),
            new NodeDetailViewModel(),
            new ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        var window = new Window
        {
            Width = 1400, Height = 900,
            Content = new DockControl { Factory = factory, Layout = layout },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var tabs = window.GetVisualDescendants().OfType<TabStripItem>()
            .Where(t => t is DocumentTabStripItem or ToolTabStripItem).ToList();
        return (window, tabs);
    }

    [AvaloniaFact]
    public void EveryDockTab_HasNoStateDumpAsHelpText()
    {
        var (window, tabs) = ShowLayout();
        try
        {
            Assert.Contains(tabs, t => t is DocumentTabStripItem);
            Assert.Contains(tabs, t => t is ToolTabStripItem);
            foreach (var tab in tabs)
            {
                var help = ControlAutomationPeer.CreatePeerForElement(tab).GetHelpText();
                Assert.DoesNotContain("Selected=", help);
                Assert.DoesNotContain("DockingState=", help);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DockTab_HelpText_ComesFromAutomationProperties()
    {
        var (window, tabs) = ShowLayout();
        try
        {
            var tab = tabs.First();
            AutomationProperties.SetHelpText(tab, "Switch to this pane.");
            Assert.Equal("Switch to this pane.", ControlAutomationPeer.CreatePeerForElement(tab).GetHelpText());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DockTabs_KeepTheirTitleNameAndTheirPatterns()
    {
        var (window, tabs) = ShowLayout();
        try
        {
            foreach (var tab in tabs)
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(tab);
                var title = ((IDockable)tab.DataContext!).Title;
                Assert.Equal(title, peer.GetName());
                Assert.Equal(AutomationControlType.TabItem, peer.GetAutomationControlType());
                Assert.NotNull(peer.GetProvider<IInvokeProvider>());
                Assert.NotNull(peer.GetProvider<ISelectionItemProvider>());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DockTab_SelectThroughUia_ActivatesThatPane()
    {
        var (window, tabs) = ShowLayout();
        try
        {
            // A tool dock with two panes: select the one not showing, through its peer.
            var unselected = tabs.OfType<ToolTabStripItem>().First(t => !t.IsSelected);
            ControlAutomationPeer.CreatePeerForElement(unselected).GetProvider<ISelectionItemProvider>()!.Select();
            Dispatcher.UIThread.RunJobs();

            Assert.True(unselected.IsSelected);
        }
        finally { window.Close(); }
    }
}
