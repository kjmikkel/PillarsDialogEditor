using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.ViewModels;

/// Issue 82: Load lays out cards by their estimated height, not one fixed size.
public class ConversationViewModelLayoutTests
{
    [Fact]
    public void Load_StacksSiblingsByEstimatedHeight()
    {
        static ConversationNode N(int id, params int[] to) =>
            new(id, false, SpeakerCategory.Npc, "", "", to.Select(t => new NodeLink(id, t, [])).ToList(), [], [], "Conversation", "None");
        var longText = string.Concat(Enumerable.Repeat("A long line of dialogue. ", 20));
        var conv = new Conversation("c", [N(0, 1, 2), N(1), N(2)],
            new StringTable([new StringEntry(0, "Hi", ""), new StringEntry(1, "Bye", ""), new StringEntry(2, longText, "")]));

        var vm = new ConversationViewModel(new StubDispatcher());
        vm.Load(conv);

        var n1 = vm.Nodes.Single(n => n.NodeId == 1);
        var n2 = vm.Nodes.Single(n => n.NodeId == 2);
        var (upper, lower) = n1.Location.Y < n2.Location.Y ? (n1, n2) : (n2, n1);
        Assert.Equal(upper.Location.Y + CardMetrics.EstimateHeight(upper.DefaultText) + CardMetrics.RowGap,
                     lower.Location.Y, 3);
    }
}
