using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class ConversationSnapshotBuilderTests
{
    private static ConversationNode MakeNode(
        int id,
        IReadOnlyList<NodeLink>? links = null) =>
        new(id, false, SpeakerCategory.Npc, "spkrGuid", "lstnrGuid",
            links ?? [],
            Conditions: [],
            Scripts: [],
            DisplayType: "Conversation",
            Persistence: "None",
            ActorDirection: "Left",
            Comments: "comment",
            ExternalVO: "vo.wav",
            HasVO: true,
            HideSpeaker: false);

    // ── Field mapping ─────────────────────────────────────────────────────

    [Fact]
    public void Build_MapsNodeFields()
    {
        var node  = MakeNode(7);
        var entry = new StringEntry(7, "Hello world", "Female line");
        var snap  = ConversationSnapshotBuilder.Build(
            new Conversation("c", [node], new StringTable([entry])));

        Assert.Single(snap.Nodes);
        var n = snap.Nodes[0];
        Assert.Equal(7,             n.NodeId);
        Assert.False(n.IsPlayerChoice);
        Assert.Equal("spkrGuid",    n.SpeakerGuid);
        Assert.Equal("lstnrGuid",   n.ListenerGuid);
        Assert.Equal("Hello world", n.DefaultText);
        Assert.Equal("Female line", n.FemaleText);
        Assert.Equal("Conversation",n.DisplayType);
        Assert.Equal("None",        n.Persistence);
        Assert.Equal("Left",        n.ActorDirection);
        Assert.Equal("comment",     n.Comments);
        Assert.Equal("vo.wav",      n.ExternalVO);
        Assert.True(n.HasVO);
        Assert.False(n.HideSpeaker);
    }

    [Fact]
    public void Build_NodeWithMissingStringEntry_UsesEmptyText()
    {
        var snap = ConversationSnapshotBuilder.Build(
            new Conversation("c", [MakeNode(1)], StringTable.Empty));

        Assert.Equal(string.Empty, snap.Nodes[0].DefaultText);
        Assert.Equal(string.Empty, snap.Nodes[0].FemaleText);
    }

    // ── Links ─────────────────────────────────────────────────────────────

    [Fact]
    public void Build_PreservesLinks()
    {
        var link = new NodeLink(1, 5, Conditions: [new ConditionLeaf("Boolean A()", [], false, "And")], RandomWeight: 2f, QuestionNodeTextDisplay: "Always");
        var snap = ConversationSnapshotBuilder.Build(
            new Conversation("c", [MakeNode(1, [link])], StringTable.Empty));

        Assert.Single(snap.Nodes[0].Links);
        var ls = snap.Nodes[0].Links[0];
        Assert.Equal(1,        ls.FromNodeId);
        Assert.Equal(5,        ls.ToNodeId);
        Assert.True(ls.HasConditions);
        Assert.Equal(2f,       ls.RandomWeight);
        Assert.Equal("Always", ls.QuestionNodeTextDisplay);
    }

    [Fact]
    public void Build_MultipleNodes_AllPresent()
    {
        var conv = new Conversation("c",
            [MakeNode(1), MakeNode(2), MakeNode(3)], StringTable.Empty);
        var snap = ConversationSnapshotBuilder.Build(conv);
        Assert.Equal(3, snap.Nodes.Count);
    }
}
