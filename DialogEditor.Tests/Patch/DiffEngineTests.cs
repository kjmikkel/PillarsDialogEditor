using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class DiffEngineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static NodeEditSnapshot MakeNode(
        int id,
        string defaultText = "Hello",
        string femaleText  = "",
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, false, SpeakerCategory.Npc, "", "", defaultText, femaleText,
            "Conversation", "None", "", "", "", false, false,
            links ?? []);

    private static ConversationEditSnapshot Snap(params NodeEditSnapshot[] nodes) =>
        new(nodes);

    // ── Empty diff ────────────────────────────────────────────────────────

    [Fact]
    public void Diff_IdenticalSnapshots_ProducesEmptyPatch()
    {
        var snap = Snap(MakeNode(1));
        var patch = DiffEngine.Diff("conv", snap, snap);
        Assert.True(patch.IsEmpty);
    }

    // ── Added / deleted nodes ─────────────────────────────────────────────

    [Fact]
    public void Diff_AddedNode_AppearsInAddedNodes()
    {
        var baseSnap    = Snap(MakeNode(1));
        var currentSnap = Snap(MakeNode(1), MakeNode(2));
        var patch = DiffEngine.Diff("conv", baseSnap, currentSnap);
        Assert.Single(patch.AddedNodes);
        Assert.Equal(2, patch.AddedNodes[0].NodeId);
        Assert.Empty(patch.DeletedNodeIds);
        Assert.Empty(patch.ModifiedNodes);
    }

    [Fact]
    public void Diff_DeletedNode_AppearsInDeletedNodeIds()
    {
        var baseSnap    = Snap(MakeNode(1), MakeNode(2));
        var currentSnap = Snap(MakeNode(1));
        var patch = DiffEngine.Diff("conv", baseSnap, currentSnap);
        Assert.Single(patch.DeletedNodeIds);
        Assert.Equal(2, patch.DeletedNodeIds[0]);
        Assert.Empty(patch.AddedNodes);
        Assert.Empty(patch.ModifiedNodes);
    }

    // ── Field changes ─────────────────────────────────────────────────────

    [Fact]
    public void Diff_ChangedDefaultText_CorrectFromAndTo()
    {
        var baseSnap    = Snap(MakeNode(1, defaultText: "old"));
        var currentSnap = Snap(MakeNode(1, defaultText: "new"));
        var patch = DiffEngine.Diff("conv", baseSnap, currentSnap);
        Assert.Single(patch.ModifiedNodes);
        var mod = patch.ModifiedNodes[0];
        Assert.Equal(1, mod.NodeId);
        Assert.True(mod.FieldChanges.ContainsKey("DefaultText"));
        Assert.Equal("\"old\"", mod.FieldChanges["DefaultText"].From);
        Assert.Equal("\"new\"", mod.FieldChanges["DefaultText"].To);
    }

    [Fact]
    public void Diff_UnchangedNode_ProducesNoModification()
    {
        var node = MakeNode(1, defaultText: "same");
        var patch = DiffEngine.Diff("conv", Snap(node), Snap(node));
        Assert.Empty(patch.ModifiedNodes);
    }

    // ── Link changes ──────────────────────────────────────────────────────

    [Fact]
    public void Diff_AddedLink_AppearsInNodeModificationAddedLinks()
    {
        var link = new LinkEditSnapshot(1, 5, 1f, "", false);
        var baseSnap    = Snap(MakeNode(1, links: []));
        var currentSnap = Snap(MakeNode(1, links: [link]));
        var patch = DiffEngine.Diff("conv", baseSnap, currentSnap);
        Assert.Single(patch.ModifiedNodes);
        Assert.Single(patch.ModifiedNodes[0].AddedLinks);
        Assert.Equal(5, patch.ModifiedNodes[0].AddedLinks[0].ToNodeId);
    }

    [Fact]
    public void Diff_DeletedLink_AppearsInNodeModificationDeletedLinks()
    {
        var link = new LinkEditSnapshot(1, 5, 1f, "", false);
        var baseSnap    = Snap(MakeNode(1, links: [link]));
        var currentSnap = Snap(MakeNode(1, links: []));
        var patch = DiffEngine.Diff("conv", baseSnap, currentSnap);
        Assert.Single(patch.ModifiedNodes);
        Assert.Single(patch.ModifiedNodes[0].DeletedLinks);
        Assert.Equal(5, patch.ModifiedNodes[0].DeletedLinks[0].ToNodeId);
    }

    // ── Modified links ────────────────────────────────────────────────────

    [Fact]
    public void Diff_ChangedQuestionNodeTextDisplay_AppearsInModifiedLinks()
    {
        var baseLink    = new LinkEditSnapshot(1, 5, 1f, "ShowOnce", false);
        var currentLink = new LinkEditSnapshot(1, 5, 1f, "Always",   false);
        var patch = DiffEngine.Diff("conv",
            Snap(MakeNode(1, links: [baseLink])),
            Snap(MakeNode(1, links: [currentLink])));
        Assert.Single(patch.ModifiedNodes);
        Assert.Single(patch.ModifiedNodes[0].ModifiedLinks);
        Assert.Equal(5,        patch.ModifiedNodes[0].ModifiedLinks[0].ToNodeId);
        Assert.Equal("Always", patch.ModifiedNodes[0].ModifiedLinks[0].QuestionNodeTextDisplay);
    }

    [Fact]
    public void Diff_ChangedRandomWeight_AppearsInModifiedLinks()
    {
        var baseLink    = new LinkEditSnapshot(1, 5, 1f,  "ShowOnce", false);
        var currentLink = new LinkEditSnapshot(1, 5, 2.5f,"ShowOnce", false);
        var patch = DiffEngine.Diff("conv",
            Snap(MakeNode(1, links: [baseLink])),
            Snap(MakeNode(1, links: [currentLink])));
        Assert.Single(patch.ModifiedNodes[0].ModifiedLinks);
        Assert.Equal(2.5f, patch.ModifiedNodes[0].ModifiedLinks[0].RandomWeight);
    }

    [Fact]
    public void Diff_UnchangedLink_ProducesNoModifiedLink()
    {
        var link = new LinkEditSnapshot(1, 5, 1f, "ShowOnce", false);
        var patch = DiffEngine.Diff("conv",
            Snap(MakeNode(1, links: [link])),
            Snap(MakeNode(1, links: [link])));
        Assert.True(patch.IsEmpty);
    }

    // ── Metadata ──────────────────────────────────────────────────────────

    [Fact]
    public void Diff_SetsConversationNameAndSchemaVersion()
    {
        var snap  = Snap(MakeNode(1));
        var patch = DiffEngine.Diff("myConversation", snap, snap);
        Assert.Equal("myConversation", patch.ConversationName);
        Assert.Equal(ConversationPatch.CurrentSchemaVersion, patch.SchemaVersion);
    }
}
