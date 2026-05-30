using DialogEditor.Core.Editing;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class NodeApplyBuilderTests
{
    private static DialogProject Project(string name, ConversationPatch patch) =>
        new("p", DialogProject.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch> { [name] = patch });

    private static ConversationPatch Patch(
        string name,
        IReadOnlyList<NodeEditSnapshot>? added = null,
        IReadOnlyList<int>? deleted = null,
        IReadOnlyList<NodeModification>? modified = null) =>
        new(name, ConversationPatch.CurrentSchemaVersion,
            added ?? [], deleted ?? [], modified ?? []);

    private static NodeEditSnapshot Node(int id) =>
        new(id, false, default, "", "", "", "", "", "", "", "", "", false, false, [], [], []);

    private static NodeModification Mod(int id) =>
        new(id, new Dictionary<string, FieldChange>(), [], [], []);

    [Fact]
    public void Apply_BringsInAnAddedNode_FromSource()
    {
        var target = Project("c", Patch("c"));
        var source = Project("c", Patch("c", added: [Node(7)]));

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        Assert.Contains(result.Patches["c"].AddedNodes, n => n.NodeId == 7);
    }

    [Fact]
    public void Apply_BringsInAModifiedNode_FromSource()
    {
        var target = Project("c", Patch("c"));
        var source = Project("c", Patch("c", modified: [Mod(7)]));

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        Assert.Contains(result.Patches["c"].ModifiedNodes, m => m.NodeId == 7);
    }

    [Fact]
    public void Apply_ReplacesTargetsOwnVersion_WithSources()
    {
        var target = Project("c", Patch("c", modified: [Mod(7)]));
        var srcMod = new NodeModification(7, new Dictionary<string, FieldChange>(),
            [new LinkEditSnapshot(7, 99, 1f, "", false)], [], []);
        var source = Project("c", Patch("c", modified: [srcMod]));

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        var mod = Assert.Single(result.Patches["c"].ModifiedNodes, m => m.NodeId == 7);
        Assert.Single(mod.AddedLinks);
    }

    [Fact]
    public void Apply_EmptySelection_ReturnsTargetUnchanged()
    {
        var target = Project("c", Patch("c", added: [Node(1)]));
        var result = NodeApplyBuilder.Apply(target, target, []);
        Assert.Same(target, result);
    }
}
