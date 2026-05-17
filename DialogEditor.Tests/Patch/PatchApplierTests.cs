using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class PatchApplierTests
{
    private static NodeEditSnapshot MakeNode(
        int id,
        string defaultText = "Hello",
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, false, SpeakerCategory.Npc, "", "", defaultText, "",
            "Conversation", "None", "", "", "", false, false,
            links ?? []);

    private static ConversationEditSnapshot Snap(params NodeEditSnapshot[] nodes) =>
        new(nodes);

    private static ConversationPatch EmptyPatch(string name = "conv") =>
        new(name, ConversationPatch.CurrentSchemaVersion, [], [], []);

    // ── Empty patch ───────────────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyPatch_ReturnsSameNodeSet()
    {
        var snap   = Snap(MakeNode(1), MakeNode(2));
        var result = PatchApplier.Apply(snap, EmptyPatch());
        Assert.Equal(2, result.Nodes.Count);
    }

    // ── Added / deleted nodes ─────────────────────────────────────────────

    [Fact]
    public void Apply_AddedNode_AppearsInResult()
    {
        var snap  = Snap(MakeNode(1));
        var patch = new ConversationPatch("conv", 1, [MakeNode(2)], [], []);
        var result = PatchApplier.Apply(snap, patch);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Contains(result.Nodes, n => n.NodeId == 2);
    }

    [Fact]
    public void Apply_DeletedNode_RemovedFromResult()
    {
        var snap  = Snap(MakeNode(1), MakeNode(2));
        var patch = new ConversationPatch("conv", 1, [], [2], []);
        var result = PatchApplier.Apply(snap, patch);
        Assert.Single(result.Nodes);
        Assert.Equal(1, result.Nodes[0].NodeId);
    }

    // ── Field changes ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_FieldChange_UpdatesValue()
    {
        var snap = Snap(MakeNode(1, defaultText: "old"));
        var mod  = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["DefaultText"] = new("\"old\"", "\"new\"") },
            [], []);
        var patch  = new ConversationPatch("conv", 1, [], [], [mod]);
        var result = PatchApplier.Apply(snap, patch);
        Assert.Equal("new", result.Nodes[0].DefaultText);
    }

    [Fact]
    public void Apply_FieldChange_ConflictingFrom_ThrowsPatchConflictException()
    {
        var snap = Snap(MakeNode(1, defaultText: "current"));
        var mod  = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["DefaultText"] = new("\"expected\"", "\"new\"") },
            [], []);
        var patch = new ConversationPatch("conv", 1, [], [], [mod]);
        Assert.Throws<PatchConflictException>(() => PatchApplier.Apply(snap, patch));
    }

    // ── Link changes ──────────────────────────────────────────────────────

    [Fact]
    public void Apply_AddedLink_AppearsInNodeLinks()
    {
        var snap = Snap(MakeNode(1, links: []));
        var newLink = new LinkEditSnapshot(1, 5, 1f, "", false);
        var mod  = new NodeModification(1, new Dictionary<string, FieldChange>(), [newLink], []);
        var patch  = new ConversationPatch("conv", 1, [], [], [mod]);
        var result = PatchApplier.Apply(snap, patch);
        Assert.Single(result.Nodes[0].Links);
        Assert.Equal(5, result.Nodes[0].Links[0].ToNodeId);
    }

    [Fact]
    public void Apply_DeletedLink_RemovedFromNodeLinks()
    {
        var link = new LinkEditSnapshot(1, 5, 1f, "", false);
        var snap = Snap(MakeNode(1, links: [link]));
        var mod  = new NodeModification(1, new Dictionary<string, FieldChange>(),
            [], [new DeletedLink(5, false)]);
        var patch  = new ConversationPatch("conv", 1, [], [], [mod]);
        var result = PatchApplier.Apply(snap, patch);
        Assert.Empty(result.Nodes[0].Links);
    }

    // ── Multi-patch stacking ──────────────────────────────────────────────

    [Fact]
    public void ApplyAll_TwoNonConflictingPatches_BothApplied()
    {
        var snap   = Snap(MakeNode(1, defaultText: "original"));
        var patchA = new ConversationPatch("conv", 1, [MakeNode(2)], [], []);
        var modB   = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["DefaultText"] = new("\"original\"", "\"updated\"") },
            [], []);
        var patchB = new ConversationPatch("conv", 1, [], [], [modB]);

        var result = PatchApplier.ApplyAll(snap, [patchA, patchB]);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Equal("updated", result.Nodes.First(n => n.NodeId == 1).DefaultText);
    }

    [Fact]
    public void ApplyAll_ConflictingPatches_ThrowsPatchConflictException()
    {
        var snap   = Snap(MakeNode(1, defaultText: "original"));
        var modA   = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["DefaultText"] = new("\"original\"", "\"versionA\"") },
            [], []);
        var modB   = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["DefaultText"] = new("\"original\"", "\"versionB\"") },
            [], []);
        var patchA = new ConversationPatch("conv", 1, [], [], [modA]);
        var patchB = new ConversationPatch("conv", 1, [], [], [modB]);

        Assert.Throws<PatchConflictException>(() => PatchApplier.ApplyAll(snap, [patchA, patchB]));
    }
}
