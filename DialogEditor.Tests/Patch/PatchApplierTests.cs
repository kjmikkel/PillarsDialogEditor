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
            links ?? [], [], []);

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
        var snap = Snap(MakeNode(1));
        var mod  = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["Comments"] = new("\"\"", "\"updated\"") },
            [], []);
        var patch  = new ConversationPatch("conv", 2, [], [], [mod]);
        var result = PatchApplier.Apply(snap, patch);
        Assert.Equal("updated", result.Nodes[0].Comments);
    }

    [Fact]
    public void Apply_FieldChange_ConflictingFrom_ThrowsPatchConflictException()
    {
        var snap = Snap(MakeNode(1));
        var mod  = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["Comments"] = new("\"expected\"", "\"new\"") },
            [], []);
        var patch = new ConversationPatch("conv", 2, [], [], [mod]);
        Assert.Throws<PatchConflictException>(() => PatchApplier.Apply(snap, patch));
    }

    [Fact]
    public void Apply_FieldChange_ConflictingFrom_WhenIgnoreConflicts_AppliesChange()
    {
        var snap = Snap(MakeNode(1));
        var mod  = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["Comments"] = new("\"expected\"", "\"new\"") },
            [], []);
        var patch  = new ConversationPatch("conv", 2, [], [], [mod]);
        var result = PatchApplier.Apply(snap, patch, ignoreConflicts: true);
        Assert.Equal("new", result.Nodes[0].Comments);
    }

    [Fact]
    public void Apply_TextFieldInFieldChanges_ThrowsUnknownField()
    {
        // v2 patches must not contain DefaultText in FieldChanges
        var mod = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["DefaultText"] = new("\"old\"", "\"new\"") },
            [], []);
        var snap  = Snap(MakeNode(1));
        var patch = new ConversationPatch("conv", 2, [], [], [mod]);
        Assert.Throws<InvalidOperationException>(() => PatchApplier.Apply(snap, patch));
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

    // ── Modified links ────────────────────────────────────────────────────

    [Fact]
    public void Apply_ModifiedLink_UpdatesProperties()
    {
        var link = new LinkEditSnapshot(1, 5, 1f, "ShowOnce", false);
        var snap = Snap(MakeNode(1, links: [link]));
        var mod  = new NodeModification(1, new Dictionary<string, FieldChange>(),
            [], [],
            [new ModifiedLink(5, 2f, "Always")]);
        var patch  = new ConversationPatch("conv", 1, [], [], [mod]);
        var result = PatchApplier.Apply(snap, patch);
        Assert.Single(result.Nodes[0].Links);
        Assert.Equal(2f,      result.Nodes[0].Links[0].RandomWeight);
        Assert.Equal("Always",result.Nodes[0].Links[0].QuestionNodeTextDisplay);
    }

    [Fact]
    public void Apply_ModifiedLink_PreservesOtherLinks()
    {
        var link1 = new LinkEditSnapshot(1, 5, 1f, "ShowOnce", false);
        var link2 = new LinkEditSnapshot(1, 9, 1f, "ShowOnce", false);
        var snap = Snap(MakeNode(1, links: [link1, link2]));
        var mod  = new NodeModification(1, new Dictionary<string, FieldChange>(),
            [], [],
            [new ModifiedLink(5, 0.5f, "Always")]);
        var patch  = new ConversationPatch("conv", 1, [], [], [mod]);
        var result = PatchApplier.Apply(snap, patch);
        Assert.Equal(2, result.Nodes[0].Links.Count);
        // link to 9 unchanged
        Assert.Equal(1f,         result.Nodes[0].Links.First(l => l.ToNodeId == 9).RandomWeight);
        Assert.Equal("ShowOnce", result.Nodes[0].Links.First(l => l.ToNodeId == 9).QuestionNodeTextDisplay);
    }

    // ── Multi-patch stacking ──────────────────────────────────────────────

    [Fact]
    public void ApplyAll_TwoNonConflictingPatches_BothApplied()
    {
        var snap   = Snap(MakeNode(1));
        var patchA = new ConversationPatch("conv", 2, [MakeNode(2)], [], []);
        var modB   = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["Comments"] = new("\"\"", "\"updated\"") },
            [], []);
        var patchB = new ConversationPatch("conv", 2, [], [], [modB]);

        var result = PatchApplier.ApplyAll(snap, [patchA, patchB]);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Equal("updated", result.Nodes.First(n => n.NodeId == 1).Comments);
    }

    [Fact]
    public void ApplyAll_ConflictingPatches_ThrowsPatchConflictException()
    {
        var snap   = Snap(MakeNode(1));
        var modA   = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["Comments"] = new("\"\"", "\"versionA\"") },
            [], []);
        var modB   = new NodeModification(1,
            new Dictionary<string, FieldChange> { ["Comments"] = new("\"\"", "\"versionB\"") },
            [], []);
        var patchA = new ConversationPatch("conv", 2, [], [], [modA]);
        var patchB = new ConversationPatch("conv", 2, [], [], [modB]);

        Assert.Throws<PatchConflictException>(() => PatchApplier.ApplyAll(snap, [patchA, patchB]));
    }
}
