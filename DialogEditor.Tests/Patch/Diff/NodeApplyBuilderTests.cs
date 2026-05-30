using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
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

    [Fact]
    public void Apply_LeavesNodeComments_Untouched()
    {
        var target = Project("c", Patch("c", added: [Node(7)]) with
        {
            NodeComments = new Dictionary<int, string> { [7] = "mine" }
        });
        var source = Project("c", Patch("c", added: [Node(7)]) with
        {
            NodeComments = new Dictionary<int, string> { [7] = "theirs" }
        });

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        Assert.Equal("mine", result.Patches["c"].NodeComments[7]); // target's note preserved, not transplanted
    }

    private static NodeTranslation Tr(int id) => new(id, "hello", "");

    [Fact]
    public void Apply_RevertsNode_WhenSourceHasNoContribution()
    {
        var target = Project("c", Patch("c", added: [Node(7)]));
        var source = Project("c", Patch("c"));

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        Assert.DoesNotContain(result.Patches["c"].AddedNodes, n => n.NodeId == 7);
    }

    [Fact]
    public void Apply_BringsInADeletion_FromSource()
    {
        var target = Project("c", Patch("c"));
        var source = Project("c", Patch("c", deleted: [7]));

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        Assert.Contains(7, result.Patches["c"].DeletedNodeIds);
    }

    [Fact]
    public void Apply_BringsInATranslation_AndDropsTargetsOwn()
    {
        var target = Project("c", Patch("c") with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { ["en"] = [new NodeTranslation(7, "OLD", "")] }
        });
        var source = Project("c", Patch("c") with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { ["en"] = [Tr(7)] }
        });

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        var en = result.Patches["c"].Translations["en"];
        Assert.Equal("hello", Assert.Single(en, t => t.NodeId == 7).DefaultText);
    }

    [Fact]
    public void Apply_OnlyTouchesSelectedConversations()
    {
        var target = new DialogProject("p", DialogProject.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch>
            {
                ["a"] = Patch("a", added: [Node(1)]),
                ["b"] = Patch("b", added: [Node(2)]),
            });
        var source = new DialogProject("p", DialogProject.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch>
            {
                ["a"] = Patch("a", added: [Node(1), Node(9)]),
                ["b"] = Patch("b"),
            });

        var result = NodeApplyBuilder.Apply(target, source, [new("a", 9)]);

        Assert.Contains(result.Patches["a"].AddedNodes, n => n.NodeId == 9);
        Assert.Contains(result.Patches["b"].AddedNodes, n => n.NodeId == 2);
    }

    [Fact]
    public void Apply_DoesNotMutateInputs()
    {
        var targetPatch = Patch("c", added: [Node(1)]);
        var target = Project("c", targetPatch);
        var source = Project("c", Patch("c", added: [Node(9)]));

        NodeApplyBuilder.Apply(target, source, [new("c", 9)]);

        Assert.Single(target.Patches["c"].AddedNodes);
        Assert.Single(targetPatch.AddedNodes);
    }
}
