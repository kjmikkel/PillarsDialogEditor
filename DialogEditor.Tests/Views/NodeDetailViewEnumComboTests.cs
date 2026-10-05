using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// Issue 132, through a real ComboBox: the Display Type / Persistence items now change with
/// the selected node (an unknown or absent value is appended). Selecting another node must
/// never write the previous node's value into it — found in the running app, where picking
/// the canonical BankNode (no DisplayType) right after a Hidden node stored "Hidden" on it.
public class NodeDetailViewEnumComboTests
{
    public NodeDetailViewEnumComboTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel Node(int id, string displayType, string persistence) =>
        new(new ConversationNode(
                NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
                DisplayType: displayType, Persistence: persistence, ActorDirection: "",
                Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false),
            new StringEntry(id, "", ""));

    private static NodeDetailViewModel ShowPane() => ShowPane(out _);

    private static NodeDetailViewModel ShowPane(out NodeDetailView view)
    {
        var vm = new NodeDetailViewModel { IsDisplayExpanded = true };
        view = new NodeDetailView { DataContext = vm };
        new Window { Content = view }.Show();
        return vm;
    }

    // Found by an option only it offers, not by label: labels are language-dependent.
    private static ComboBox Combo(NodeDetailView view, string uniqueOption) =>
        view.GetLogicalDescendants().OfType<ComboBox>()
            .Single(c => c.ItemsSource is IEnumerable<string> items && items.Contains(uniqueOption));

    private static ComboBox DisplayCombo(NodeDetailView view)     => Combo(view, "Overlay");
    private static ComboBox PersistenceCombo(NodeDetailView view) => Combo(view, "MarkAsRead");

    [AvaloniaFact]
    public void PickingAnOptionInTheComboBox_StoresTheEnumName()
    {
        var vm   = ShowPane(out var view);
        var node = Node(1, "Conversation", "None");
        Select(vm, node);

                DisplayCombo(view).SelectedItem = "Overlay";
        PersistenceCombo(view).SelectedItem = "MarkAsRead";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Overlay",    node.DisplayType);
        Assert.Equal("MarkAsRead", node.Persistence);
    }

    [AvaloniaFact]
    public void AfterSelectingAnotherNode_TheComboBoxShowsItsValue()
    {
        var vm = ShowPane(out var view);
        Select(vm, Node(1, "Hidden", "OnceEver"));
        Select(vm, Node(2, "Unknown(7)", "MarkAsRead"));

        // An unlisted value selects nothing and shows as the placeholder.
        Assert.Null(DisplayCombo(view).SelectedItem);
        Assert.Contains("Unknown(7)", DisplayCombo(view).PlaceholderText);
        Assert.Equal("MarkAsRead", PersistenceCombo(view).SelectedItem);

        // And back to a listed value: the selection follows (it stuck when items were swapped).
        Select(vm, Node(3, "Bark", "None"));
        Assert.Equal("Bark", DisplayCombo(view).SelectedItem);
        Assert.Equal("None", PersistenceCombo(view).SelectedItem);
    }

    private static void Select(NodeDetailViewModel vm, NodeViewModel node)
    {
        vm.Load(node);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("Hidden", "")]            // then a node without the property (BankNode)
    [InlineData("Hidden", "Unknown(7)")]  // then a value the editor doesn't know
    [InlineData("", "Bark")]              // and back from an appended value to a known one
    public void SelectingAnotherNode_LeavesItsDisplayTypeAlone(string first, string second)
    {
        var vm = ShowPane();
        Select(vm, Node(1, first, "None"));

        var next = Node(2, second, "None");
        Select(vm, next);

        Assert.Equal(second, next.DisplayType);
    }

    [AvaloniaTheory]
    [InlineData("OnceEver", "")]
    [InlineData("MarkAsRead", "Unknown(9)")]
    public void SelectingAnotherNode_LeavesItsPersistenceAlone(string first, string second)
    {
        var vm = ShowPane();
        Select(vm, Node(1, "Conversation", first));

        var next = Node(2, "Conversation", second);
        Select(vm, next);

        Assert.Equal(second, next.Persistence);
    }
}
