using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

/// Tests for condition editing on links — diff detection, patch apply,
/// and JSON serialisation round-trip.
public class LinkConditionTests
{
    private static readonly ConditionLeaf Leaf =
        new("Boolean IsGlobalValue(String, Operator, Int32)",
            ["flag", "EqualTo", "1"], false, "And");

    private static NodeEditSnapshot MakeNode(
        int id,
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, false, SpeakerCategory.Npc, "", "", "text", "",
            "Conversation", "None", "", "", "", false, false,
            links ?? [], [], []);

    private static ConversationEditSnapshot Snap(params NodeEditSnapshot[] nodes) =>
        new(nodes);

    // ── DiffEngine ────────────────────────────────────────────────────────

    [Fact]
    public void Diff_LinkConditionAdded_EmitsModifiedLinkWithConditions()
    {
        var baseLink = new LinkEditSnapshot(1, 5, 1f, "ShowOnce", false);
        var newLink  = baseLink with { Conditions = [Leaf] };

        var patch = DiffEngine.Diff("c",
            Snap(MakeNode(1, [baseLink])),
            Snap(MakeNode(1, [newLink])),
            "en");

        var ml = Assert.Single(patch.ModifiedNodes[0].ModifiedLinks);
        Assert.NotNull(ml.Conditions);
        Assert.Single(ml.Conditions!);
    }

    [Fact]
    public void Diff_LinkConditionUnchanged_NoModifiedLink()
    {
        var link = new LinkEditSnapshot(1, 5, 1f, "ShowOnce", false)
            { Conditions = [Leaf] };
        var patch = DiffEngine.Diff("c", Snap(MakeNode(1, [link])), Snap(MakeNode(1, [link])), "en");
        Assert.True(patch.IsEmpty);
    }

    // ── PatchApplier ──────────────────────────────────────────────────────

    [Fact]
    public void Apply_ModifiedLink_AppliesConditions()
    {
        var link = new LinkEditSnapshot(1, 5, 1f, "ShowOnce", false);
        var snap = Snap(MakeNode(1, [link]));
        var ml   = new ModifiedLink(5, 1f, "ShowOnce", [Leaf]);
        var mod  = new NodeModification(1, new Dictionary<string, FieldChange>(),
            [], [], [ml]);
        var result = PatchApplier.Apply(snap, new ConversationPatch("c", 1, [], [], [mod]));

        var resultLink = result.Nodes[0].Links[0];
        Assert.NotNull(resultLink.Conditions);
        Assert.Single(resultLink.Conditions);
    }

    [Fact]
    public void Apply_ModifiedLink_NullConditions_PreservesExisting()
    {
        var link = new LinkEditSnapshot(1, 5, 1f, "ShowOnce", false)
            { Conditions = [Leaf] };
        var snap = Snap(MakeNode(1, [link]));
        // ModifiedLink with null Conditions — should not touch existing
        var ml   = new ModifiedLink(5, 2f, "Always");   // only weight changed
        var mod  = new NodeModification(1, new Dictionary<string, FieldChange>(),
            [], [], [ml]);
        var result = PatchApplier.Apply(snap, new ConversationPatch("c", 1, [], [], [mod]));

        Assert.Single(result.Nodes[0].Links[0].Conditions);   // original preserved
    }

    // ── Serialiser round-trip ─────────────────────────────────────────────

    [Fact]
    public void PatchSerializer_RoundTrip_PreservesLinkConditions()
    {
        var ml     = new ModifiedLink(5, 1f, "ShowOnce", [Leaf]);
        var mod    = new NodeModification(1, new Dictionary<string, FieldChange>(),
            [], [], [ml]);
        var patch  = new ConversationPatch("conv", 1, [], [], [mod]);

        var json   = PatchSerializer.Serialize(patch);
        var result = PatchSerializer.Deserialize(json);

        var conditions = result.ModifiedNodes[0].ModifiedLinks[0].Conditions;
        Assert.NotNull(conditions);
        Assert.Single(conditions!);
        Assert.IsType<ConditionLeaf>(conditions[0]);
    }
}
