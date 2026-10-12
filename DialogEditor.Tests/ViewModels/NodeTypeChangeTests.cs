using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Issue 160: the serializers write a node's game type from its SpeakerCategory, so the
/// Node Type dropdown must change the category, not just IsPlayerChoice, and the two must
/// move together as one undoable edit.
public class NodeTypeChangeTests
{
    private readonly UndoRedoStack _undo = new();

    public NodeTypeChangeTests() => Loc.Configure(new StubStringProvider());

    private NodeViewModel Node(SpeakerCategory category)
    {
        var node = new NodeViewModel(
            new ConversationNode(3, category == SpeakerCategory.Player, category, "", "", [], [], [],
                                 "Conversation", "None"),
            new StringEntry(3, "line", ""));
        node.UndoStack = _undo;
        return node;
    }

    private static NodeDetailViewModel Pane(NodeViewModel node)
    {
        var vm = new NodeDetailViewModel();
        vm.Load(node);
        return vm;
    }

    [Fact]
    public void NodeType_PlayerChoiceToNpcLine_MakesItAnNpcNode()
    {
        var node = Node(SpeakerCategory.Player);
        Pane(node).NodeTypeString = Loc.Get("Option_NpcLine");

        Assert.Equal(SpeakerCategory.Npc, node.SpeakerCategory);
        Assert.False(node.IsPlayerChoice);
    }

    [Theory]
    [InlineData(SpeakerCategory.Npc)]
    [InlineData(SpeakerCategory.Narrator)]
    [InlineData(SpeakerCategory.Script)]
    public void NodeType_ToPlayerChoice_MakesItAPlayerNode(SpeakerCategory from)
    {
        var node = Node(from);
        Pane(node).NodeTypeString = Loc.Get("Option_PlayerChoice");

        Assert.Equal(SpeakerCategory.Player, node.SpeakerCategory);
        Assert.True(node.IsPlayerChoice);
    }

    // "NPC line" is what the dropdown already shows for these, so picking it is no change.
    // In particular a script, bank or trigger node must keep its type (#113).
    [Theory]
    [InlineData(SpeakerCategory.Npc)]
    [InlineData(SpeakerCategory.Narrator)]
    [InlineData(SpeakerCategory.Script)]
    public void NodeType_NpcLineOnANonPlayerNode_ChangesNothing(SpeakerCategory category)
    {
        var node = Node(category);
        Pane(node).NodeTypeString = Loc.Get("Option_NpcLine");

        Assert.Equal(category, node.SpeakerCategory);
        Assert.False(_undo.CanUndo);
    }

    [Fact]
    public void NodeType_Change_IsOneUndoStep_ThatRestoresBoth()
    {
        var node = Node(SpeakerCategory.Player);
        Pane(node).NodeTypeString = Loc.Get("Option_NpcLine");

        _undo.Undo();

        Assert.Equal(SpeakerCategory.Player, node.SpeakerCategory);
        Assert.True(node.IsPlayerChoice);
        Assert.False(_undo.CanUndo);
    }

    [Fact]
    public void SpeakerCategory_Change_IsOneUndoStep_ThatRestoresBoth()
    {
        var node = Node(SpeakerCategory.Npc);
        Pane(node).SpeakerCategoryString = Loc.Get("Speaker_Player");

        Assert.True(node.IsPlayerChoice);
        _undo.Undo();

        Assert.Equal(SpeakerCategory.Npc, node.SpeakerCategory);
        Assert.False(node.IsPlayerChoice);
        Assert.False(_undo.CanUndo);
    }
}
