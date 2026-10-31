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
            links ?? [], [], []);

    private static ConversationEditSnapshot Snap(params NodeEditSnapshot[] nodes) =>
        new(nodes);

    // ── Empty diff ────────────────────────────────────────────────────────

    [Fact]
    public void Diff_IdenticalSnapshots_ProducesEmptyPatch()
    {
        var snap = Snap(MakeNode(1));
        var patch = DiffEngine.Diff("conv", snap, snap, "en");
        Assert.True(patch.IsEmpty);
    }

    // ── Added / deleted nodes ─────────────────────────────────────────────

    [Fact]
    public void Diff_AddedNode_AppearsInAddedNodes()
    {
        var baseSnap    = Snap(MakeNode(1));
        var currentSnap = Snap(MakeNode(1), MakeNode(2));
        var patch = DiffEngine.Diff("conv", baseSnap, currentSnap, "en");
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
        var patch = DiffEngine.Diff("conv", baseSnap, currentSnap, "en");
        Assert.Single(patch.DeletedNodeIds);
        Assert.Equal(2, patch.DeletedNodeIds[0]);
        Assert.Empty(patch.AddedNodes);
        Assert.Empty(patch.ModifiedNodes);
    }

    // ── Field changes ─────────────────────────────────────────────────────

    [Fact]
    public void Diff_ChangedDefaultText_GoesToTranslationsNotFieldChanges()
    {
        var baseSnap    = Snap(MakeNode(1, defaultText: "old"));
        var currentSnap = Snap(MakeNode(1, defaultText: "new"));
        var patch = DiffEngine.Diff("conv", baseSnap, currentSnap, "en");
        Assert.DoesNotContain(patch.ModifiedNodes, m => m.FieldChanges.ContainsKey("DefaultText"));
        Assert.True(patch.Translations.ContainsKey("en"));
        Assert.Equal("new", patch.Translations["en"].Single(x => x.NodeId == 1).DefaultText);
    }

    [Fact]
    public void Diff_UnchangedNode_ProducesNoModification()
    {
        var node = MakeNode(1, defaultText: "same");
        var patch = DiffEngine.Diff("conv", Snap(node), Snap(node), "en");
        Assert.Empty(patch.ModifiedNodes);
    }

    // ── Link changes ──────────────────────────────────────────────────────

    [Fact]
    public void Diff_AddedLink_AppearsInNodeModificationAddedLinks()
    {
        var link = new LinkEditSnapshot(1, 5, 1, "", false);
        var baseSnap    = Snap(MakeNode(1, links: []));
        var currentSnap = Snap(MakeNode(1, links: [link]));
        var patch = DiffEngine.Diff("conv", baseSnap, currentSnap, "en");
        Assert.Single(patch.ModifiedNodes);
        Assert.Single(patch.ModifiedNodes[0].AddedLinks);
        Assert.Equal(5, patch.ModifiedNodes[0].AddedLinks[0].ToNodeId);
    }

    [Fact]
    public void Diff_DeletedLink_AppearsInNodeModificationDeletedLinks()
    {
        var link = new LinkEditSnapshot(1, 5, 1, "", false);
        var baseSnap    = Snap(MakeNode(1, links: [link]));
        var currentSnap = Snap(MakeNode(1, links: []));
        var patch = DiffEngine.Diff("conv", baseSnap, currentSnap, "en");
        Assert.Single(patch.ModifiedNodes);
        Assert.Single(patch.ModifiedNodes[0].DeletedLinks);
        Assert.Equal(5, patch.ModifiedNodes[0].DeletedLinks[0].ToNodeId);
    }

    // ── Modified links ────────────────────────────────────────────────────

    [Fact]
    public void Diff_ChangedQuestionNodeTextDisplay_AppearsInModifiedLinks()
    {
        var baseLink    = new LinkEditSnapshot(1, 5, 1, "ShowOnce", false);
        var currentLink = new LinkEditSnapshot(1, 5, 1, "Always",   false);
        var patch = DiffEngine.Diff("conv",
            Snap(MakeNode(1, links: [baseLink])),
            Snap(MakeNode(1, links: [currentLink])),
            "en");
        Assert.Single(patch.ModifiedNodes);
        Assert.Single(patch.ModifiedNodes[0].ModifiedLinks);
        Assert.Equal(5,        patch.ModifiedNodes[0].ModifiedLinks[0].ToNodeId);
        Assert.Equal("Always", patch.ModifiedNodes[0].ModifiedLinks[0].QuestionNodeTextDisplay);
    }

    [Fact]
    public void Diff_ChangedRandomWeight_AppearsInModifiedLinks()
    {
        var baseLink    = new LinkEditSnapshot(1, 5, 1,  "ShowOnce", false);
        var currentLink = new LinkEditSnapshot(1, 5, 3, "ShowOnce", false);
        var patch = DiffEngine.Diff("conv",
            Snap(MakeNode(1, links: [baseLink])),
            Snap(MakeNode(1, links: [currentLink])),
            "en");
        Assert.Single(patch.ModifiedNodes[0].ModifiedLinks);
        Assert.Equal(3, patch.ModifiedNodes[0].ModifiedLinks[0].RandomWeight);
    }

    [Fact]
    public void Diff_UnchangedLink_ProducesNoModifiedLink()
    {
        var link = new LinkEditSnapshot(1, 5, 1, "ShowOnce", false);
        var patch = DiffEngine.Diff("conv",
            Snap(MakeNode(1, links: [link])),
            Snap(MakeNode(1, links: [link])),
            "en");
        Assert.True(patch.IsEmpty);
    }

    // ── Metadata ──────────────────────────────────────────────────────────

    [Fact]
    public void Diff_SetsConversationNameAndSchemaVersion()
    {
        var snap  = Snap(MakeNode(1));
        var patch = DiffEngine.Diff("myConversation", snap, snap, "en");
        Assert.Equal("myConversation", patch.ConversationName);
        Assert.Equal(ConversationPatch.CurrentSchemaVersion, patch.SchemaVersion);
    }

    // ── Text → Translations ───────────────────────────────────────────────

    [Fact]
    public void Diff_AddedNode_TextGoesToTranslations()
    {
        var baseSnap = Snap();
        var added    = MakeNode(5, defaultText: "Hi", femaleText: "Hello");
        var patch    = DiffEngine.Diff("c", baseSnap, Snap(added), "en");
        Assert.Empty(patch.AddedNodes[0].DefaultText); // not in node
        Assert.True(patch.Translations.ContainsKey("en"));
        var t = patch.Translations["en"].Single(x => x.NodeId == 5);
        Assert.Equal("Hi",    t.DefaultText);
        Assert.Equal("Hello", t.FemaleText);
    }

    [Fact]
    public void Diff_TextChange_GoesToTranslationsNotFieldChanges()
    {
        var base_  = MakeNode(1, defaultText: "Old");
        var curr   = MakeNode(1, defaultText: "New");
        var patch  = DiffEngine.Diff("c", Snap(base_), Snap(curr), "fr");
        Assert.DoesNotContain(patch.ModifiedNodes, m => m.FieldChanges.ContainsKey("DefaultText"));
        Assert.True(patch.Translations.ContainsKey("fr"));
        Assert.Equal("New", patch.Translations["fr"].Single(x => x.NodeId == 1).DefaultText);
    }

    [Fact]
    public void Diff_NoTextChange_NoTranslationEntry()
    {
        var node  = MakeNode(1, defaultText: "Same");
        var patch = DiffEngine.Diff("c", Snap(node), Snap(node), "en");
        Assert.Empty(patch.Translations);
    }

    [Fact]
    public void Diff_StructuralChangeOnly_NoTranslationEntry()
    {
        var base_ = MakeNode(1, defaultText: "Text");
        var curr  = base_ with { IsPlayerChoice = true };
        var patch = DiffEngine.Diff("c", Snap(base_), Snap(curr), "en");
        Assert.Empty(patch.Translations);
    }

    // ── Node type (#160) ──────────────────────────────────────────────────
    // The serializers write the game type from SpeakerCategory, so the patch must carry it.
    // By name, not number: the patch file must not change meaning if the enum is reordered.

    [Fact]
    public void Diff_CategoryChange_IsRecordedByName()
    {
        var base_ = MakeNode(3) with { IsPlayerChoice = true, SpeakerCategory = SpeakerCategory.Player };
        var curr  = MakeNode(3);
        var mod   = Assert.Single(DiffEngine.Diff("c", Snap(base_), Snap(curr), "en").ModifiedNodes);

        Assert.Equal(new FieldChange("\"Player\"", "\"Npc\""), mod.FieldChanges["SpeakerCategory"]);
    }

    // ── Link order (issue 359) ────────────────────────────────────────────

    private static NodeEditSnapshot WithLinks(int id, params int[] targets) =>
        MakeNode(id, links: [.. targets.Select(t => new LinkEditSnapshot(id, t, 1, "", false))]);

    [Fact]
    public void Diff_ReorderOnly_WritesUpdatedLinkOrder()
    {
        var patch = DiffEngine.Diff("conv", Snap(WithLinks(0, 1, 2, 3)), Snap(WithLinks(0, 3, 1, 2)), "en");

        var mod = Assert.Single(patch.ModifiedNodes);
        Assert.Equal(0, mod.NodeId);
        Assert.Equal([3, 1, 2], mod.UpdatedLinkOrder);
        Assert.Empty(mod.FieldChanges);
        Assert.Empty(mod.AddedLinks);
        Assert.Empty(mod.DeletedLinks);
        Assert.Empty(mod.ModifiedLinks);
    }

    [Fact]
    public void Diff_SameOrder_NoLinkOrder()
        => Assert.Empty(DiffEngine.Diff("conv", Snap(WithLinks(0, 1, 2, 3)), Snap(WithLinks(0, 1, 2, 3)), "en").ModifiedNodes);

    [Fact]
    public void Diff_AddedLinkAppended_NoLinkOrder()
    {
        var mod = Assert.Single(DiffEngine.Diff("conv", Snap(WithLinks(0, 1, 2)), Snap(WithLinks(0, 1, 2, 4)), "en").ModifiedNodes);
        Assert.Equal([4], mod.AddedLinks.Select(l => l.ToNodeId));
        Assert.Null(mod.UpdatedLinkOrder);
    }

    [Fact]
    public void Diff_AddedLinkFirst_WritesOrder()
    {
        var mod = Assert.Single(DiffEngine.Diff("conv", Snap(WithLinks(0, 1, 2)), Snap(WithLinks(0, 4, 1, 2)), "en").ModifiedNodes);
        Assert.Equal([4, 1, 2], mod.UpdatedLinkOrder);
    }

    [Fact]
    public void Diff_DeletedLink_OrderOfRestUnchanged_NoLinkOrder()
    {
        var mod = Assert.Single(DiffEngine.Diff("conv", Snap(WithLinks(0, 1, 2, 3)), Snap(WithLinks(0, 1, 3)), "en").ModifiedNodes);
        Assert.Equal([2], mod.DeletedLinks.Select(l => l.ToNodeId));
        Assert.Null(mod.UpdatedLinkOrder);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3 }, new[] { 3, 1, 2 })]
    [InlineData(new[] { 1, 2, 3 }, new[] { 3, 2, 1 })]
    [InlineData(new[] { 1, 2 },    new[] { 4, 1, 2 })]
    [InlineData(new[] { 1, 2 },    new[] { 1, 2, 4 })]
    [InlineData(new[] { 1, 2, 3 }, new[] { 3, 1 })]
    [InlineData(new[] { 1, 2, 3 }, new[] { 3, 4, 1 })]
    public void Diff_ThenApply_RoundTripsOrder(int[] baseOrder, int[] currentOrder)
    {
        var baseSnap    = Snap(WithLinks(0, baseOrder));
        var currentSnap = Snap(WithLinks(0, currentOrder));

        var patch  = DiffEngine.Diff("conv", baseSnap, currentSnap, "en");
        var result = PatchApplier.Apply(baseSnap, patch);

        Assert.Equal(currentOrder, result.Nodes.Single().Links.Select(l => l.ToNodeId));
    }
}
