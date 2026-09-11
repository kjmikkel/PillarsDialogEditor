using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// ConversationViewModel exposes UndoDescription/RedoDescription as observable
/// properties for the undo/redo affordances, so they are UI copy and must be
/// translated.
///
/// The important property here is LAZINESS. IEditCommand.Description is read when the
/// stack is peeked, not when the command is pushed, and the editor supports changing
/// language while a conversation is open — so resolving the text at push time would
/// leave the undo history stuck in whatever language the edit was made in. These tests
/// push an edit, swap the string provider, and require the description to follow.
/// </summary>
public class UndoDescriptionTests
{
    public UndoDescriptionTests() => Loc.Configure(new StubStringProvider());

    /// A provider that prefixes every key, standing in for "the user switched language".
    private sealed class PrefixingStringProvider : IStringProvider
    {
        public string Get(string key) => "XX:" + key;
        public bool TryGet(string key, out string value) { value = "XX:" + key; return true; }
    }

    private static NodeViewModel Node(int id = 1) =>
        new(new ConversationNode(
                NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
                DisplayType: "Conversation", Persistence: "None"),
            new StringEntry(id, "Hello", ""));

    private static UndoRedoStack StackFor(NodeViewModel node)
    {
        var stack = new UndoRedoStack();
        node.UndoStack = stack;
        return stack;
    }

    // ── SetPropertyCommand descriptions (NodeViewModel, 15 of them) ───────

    [Fact]
    public void NodeEditDescription_ComesFromResources()
    {
        var node  = Node();
        var stack = StackFor(node);
        node.DefaultText = "changed";

        Assert.Equal("Undo_EditDialogText", stack.UndoDescription);
    }

    [Fact]
    public void NodeEditDescription_IsResolvedWhenReadNotWhenPushed()
    {
        var node  = Node();
        var stack = StackFor(node);
        node.DefaultText = "changed";
        Assert.Equal("Undo_EditDialogText", stack.UndoDescription);

        Loc.Configure(new PrefixingStringProvider());

        Assert.Equal("XX:Undo_EditDialogText", stack.UndoDescription);
    }

    [Theory]
    [InlineData("Undo_EditSpeakerGuid")]
    [InlineData("Undo_EditListenerGuid")]
    [InlineData("Undo_EditFemaleText")]
    [InlineData("Undo_EditActorDirection")]
    [InlineData("Undo_EditComments")]
    [InlineData("Undo_EditExternalVo")]
    public void EachNodeStringFieldHasItsOwnKey(string expectedKey)
    {
        var node  = Node();
        var stack = StackFor(node);

        switch (expectedKey)
        {
            case "Undo_EditSpeakerGuid":     node.SpeakerGuid    = "g"; break;
            case "Undo_EditListenerGuid":    node.ListenerGuid   = "g"; break;
            case "Undo_EditFemaleText":      node.FemaleText     = "f"; break;
            case "Undo_EditActorDirection":  node.ActorDirection = "a"; break;
            case "Undo_EditComments":        node.Comments       = "c"; break;
            case "Undo_EditExternalVo":      node.ExternalVO     = "v"; break;
        }

        Assert.Equal(expectedKey, stack.UndoDescription);
    }

    // ── AnnotationViewModel (3) ──────────────────────────────────────────

    [Fact]
    public void AnnotationEditDescriptions_ComeFromResources()
    {
        var ann   = new AnnotationViewModel();
        var stack = new UndoRedoStack();
        ann.UndoStack = stack;

        ann.Title = "t";
        Assert.Equal("Undo_EditAnnotationTitle", stack.UndoDescription);
        ann.Body = "b";
        Assert.Equal("Undo_EditAnnotationBody", stack.UndoDescription);
        ann.ColorKey = "Blue";
        Assert.Equal("Undo_ChangeAnnotationColor", stack.UndoDescription);
    }

    // ── ConnectionViewModel (3) ──────────────────────────────────────────

    [Fact]
    public void ConnectionEditDescriptions_ComeFromResources()
    {
        var conn  = new ConnectionViewModel(new ConnectorViewModel(), new ConnectorViewModel());
        var stack = new UndoRedoStack();
        conn.UndoStack = stack;

        conn.QuestionNodeTextDisplay = "Always";
        Assert.Equal("Undo_EditLinkDisplay", stack.UndoDescription);
        conn.RandomWeight = 2f;
        Assert.Equal("Undo_EditLinkWeight", stack.UndoDescription);
        conn.Conditions = new List<ConditionNode>();
        Assert.Equal("Undo_EditLinkConditions", stack.UndoDescription);
    }
}

/// <summary>
/// The six structural commands build their own Description, including the two that
/// interpolate node ids. Tier two resolves the real Strings.axaml so the {0}/{1} order
/// is pinned — "Add connection 1 → 2" must not come out reversed.
/// </summary>
public class UndoDescriptionResourceEndToEndTests
{
    public UndoDescriptionResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    private static ConversationViewModel Conversation() => new(new StubDispatcher());

    private static NodeViewModel Node(int id = 1) =>
        new(new ConversationNode(
                NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
                DisplayType: "Conversation", Persistence: "None"),
            new StringEntry(id, "Hello", ""));

    [AvaloniaFact]
    public void AddAndDeleteNode_DescribeTheNode()
    {
        var conv = Conversation();
        var node = Node(7);

        conv.AddNode(node, new LayoutPoint(0, 0));
        Assert.Equal("Add node 7", conv.UndoDescription);

        conv.DeleteNode(node);
        Assert.Equal("Delete node 7", conv.UndoDescription);
    }

    [AvaloniaFact]
    public void AddAndDeleteAnnotation_HaveFixedDescriptions()
    {
        var conv = Conversation();
        var ann  = new AnnotationViewModel();

        conv.AddAnnotation(ann);
        Assert.Equal("Add annotation", conv.UndoDescription);

        conv.DeleteAnnotation(ann);
        Assert.Equal("Delete annotation", conv.UndoDescription);
    }

    [AvaloniaFact]
    public void NodeFieldEdit_UsesTheRealResource()
    {
        var node  = Node();
        var stack = new UndoRedoStack();
        node.UndoStack   = stack;
        node.DefaultText = "changed";

        Assert.Equal("Edit dialog text", stack.UndoDescription);
    }
}
