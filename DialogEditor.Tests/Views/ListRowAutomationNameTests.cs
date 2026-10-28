using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Theming;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

/// Issue 325: list rows and the Node Details expander headers exposed a .NET type name as their
/// UI Automation name ("DisplayRow { Row = SpeakerLineRow { … } }",
/// "DialogEditor.ViewModels.BalanceRowViewModel", "Avalonia.Controls.Grid"), which a screen
/// reader reads aloud instead of the content.
public class ListRowAutomationNameTests
{
    public ListRowAutomationNameTests() => Loc.Configure(new AvaloniaStringProvider());

    private static string NameOf(Control c) => ControlAutomationPeer.CreatePeerForElement(c).GetName();

    private static List<string> RowNames(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var names = window.GetVisualDescendants().OfType<ListBoxItem>().Select(NameOf).ToList();
        window.Close();
        return names;
    }

    private static void AssertNoTypeName(string name)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.DoesNotContain("DialogEditor.", name);
        Assert.DoesNotContain("Avalonia.", name);
        Assert.DoesNotContain(" { ", name);
    }

    [AvaloniaFact]
    public void SpeakerLines_RowsAreNamedByTheirLineAndConversation()
    {
        var names = RowNames(TableWindowFixtures.All["SpeakerLines"]());

        var name = Assert.Single(names);
        AssertNoTypeName(name);
        Assert.Contains("A line of dialogue long enough", name);
        Assert.Contains("a_very_long_conversation_name", name);
    }

    [AvaloniaFact]
    public void RepDispositionBalance_RowsAreNamedByTheirValue()
    {
        var names = RowNames(TableWindowFixtures.All["RepDisposition"]());

        Assert.NotEmpty(names);
        foreach (var name in names) AssertNoTypeName(name);
        Assert.Contains(names, n => n.Contains("Benevolent"));
    }

    [AvaloniaFact]
    public void NodeDetails_ExpanderHeadersAreNamedBySectionTitle()
    {
        var vm = new NodeDetailViewModel();
        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None", ActorDirection: "", Comments: "",
            ExternalVO: "", HasVO: false, HideSpeaker: false);
        vm.Load(new NodeViewModel(node, new StringEntry(1, "Test line", "")));
        var window = new Window { Width = 420, Height = 900, Content = new NodeDetailView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var expanders = window.GetVisualDescendants().OfType<Expander>()
            .Where(e => e.Classes.Contains("detail-group")).ToList();
        Assert.NotEmpty(expanders);
        foreach (var expander in expanders)
        {
            var header = expander.GetVisualDescendants().OfType<ToggleButton>().First();
            var name = NameOf(header);
            AssertNoTypeName(name);
            Assert.Equal(NameOf(expander), name);   // the section title the Expander already carries
        }
        window.Close();
    }
}
