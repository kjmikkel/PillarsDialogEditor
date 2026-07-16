using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Search;

namespace DialogEditor.Tests.Search;

public class NodeConditionSearchServiceTests
{
    private static ConditionLeaf CondLeaf(string full, params string[] args) =>
        new(full, args, Not: false, Operator: "And");

    private static NodeEditSnapshot Node(
        int id,
        IReadOnlyList<ConditionNode>? nodeConds = null,
        IReadOnlyList<ScriptCall>? scripts = null,
        IReadOnlyList<ConditionNode>? linkConds = null)
    {
        var links = linkConds is null
            ? (IReadOnlyList<LinkEditSnapshot>)[]
            : new[] { new LinkEditSnapshot(id, id + 1, 0f, "", HasConditions: true) { Conditions = linkConds } };
        return new NodeEditSnapshot(id, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false,
            links, nodeConds ?? [], scripts ?? []);
    }

    private static readonly CatalogueMatch DispQuery =
        new("Boolean IsDisposition(Guid, Rank, Operator)", new[] { ParameterPin.Wildcard });

    [Fact]
    public void FindMatches_HitViaNodeCondition()
    {
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, nodeConds: new ConditionNode[] { CondLeaf("Boolean IsDisposition(Guid, Rank, Operator)", "b", "2", "GT") }),
            Node(1),
        });
        Assert.Equal(new[] { 0 }, NodeConditionSearchService.FindMatches(snap, DispQuery).Order());
    }

    [Fact]
    public void FindMatches_HitViaLinkCondition()
    {
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, linkConds: new ConditionNode[] { CondLeaf("Boolean IsDisposition(Guid, Rank, Operator)", "b", "2", "GT") }),
        });
        Assert.Contains(0, NodeConditionSearchService.FindMatches(snap, DispQuery));
    }

    [Fact]
    public void FindMatches_HitViaScript()
    {
        var query = new CatalogueMatch("Void SetGlobalValue(String, Int32)", new[] { ParameterPin.Wildcard, ParameterPin.Wildcard });
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, scripts: new[] { new ScriptCall("Void SetGlobalValue(String, Int32)", new[] { "g", "1" }, ScriptCategory.Enter) }),
        });
        Assert.Contains(0, NodeConditionSearchService.FindMatches(snap, query));
    }

    [Fact]
    public void FindMatches_NodeMatchingTwoSites_ReturnedOnce()
    {
        var leaf = CondLeaf("Boolean IsDisposition(Guid, Rank, Operator)", "b", "2", "GT");
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, nodeConds: new ConditionNode[] { leaf }, linkConds: new ConditionNode[] { leaf }),
        });
        Assert.Single(NodeConditionSearchService.FindMatches(snap, DispQuery));
    }

    [Fact]
    public void FindMatches_NoMatch_ReturnsEmpty()
    {
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, nodeConds: new ConditionNode[] { CondLeaf("Boolean IsGlobalValue(String, Operator, Int32)", "g", "EqualTo", "1") }),
        });
        Assert.Empty(NodeConditionSearchService.FindMatches(snap, DispQuery));
    }
}
