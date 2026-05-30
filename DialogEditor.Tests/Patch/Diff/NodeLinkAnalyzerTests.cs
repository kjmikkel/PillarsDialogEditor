using DialogEditor.Core.Editing;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class NodeLinkAnalyzerTests
{
    private static DialogProject Project(string name, ConversationPatch patch) =>
        new("p", DialogProject.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch> { [name] = patch });

    private static ConversationPatch Patch(
        string name,
        IReadOnlyList<NodeEditSnapshot>? added = null,
        IReadOnlyList<int>? deleted = null,
        IReadOnlyList<NodeModification>? modified = null) =>
        new(name, ConversationPatch.CurrentSchemaVersion, added ?? [], deleted ?? [], modified ?? []);

    private static NodeEditSnapshot NodeWithLink(int id, int toId) =>
        new(id, false, default, "", "", "", "", "", "", "", "", "", false, false,
            [new LinkEditSnapshot(id, toId, 1f, "", false)], [], []);

    [Fact]
    public void Analyze_FlagsAddedNodeLink_ToADeletedNode()
    {
        var project = Project("c", Patch("c", added: [NodeWithLink(5, 8)], deleted: [8]));

        var dangling = NodeLinkAnalyzer.Analyze(project);

        var link = Assert.Single(dangling);
        Assert.Equal(("c", 5, 8), (link.Conversation, link.FromNode, link.ToNode));
    }

    [Fact]
    public void Analyze_FlagsModifiedNodeAddedLink_ToADeletedNode()
    {
        var mod = new NodeModification(5, new Dictionary<string, FieldChange>(),
            [new LinkEditSnapshot(5, 8, 1f, "", false)], [], []);
        var project = Project("c", Patch("c", deleted: [8], modified: [mod]));

        var dangling = NodeLinkAnalyzer.Analyze(project);

        Assert.Single(dangling);
    }

    [Fact]
    public void Analyze_FlagsModifiedNodeModifiedLink_ToADeletedNode()
    {
        var mod = new NodeModification(5, new Dictionary<string, FieldChange>(),
            [], [], [new ModifiedLink(8, 1f, "")]);
        var project = Project("c", Patch("c", deleted: [8], modified: [mod]));

        var dangling = NodeLinkAnalyzer.Analyze(project);

        var link = Assert.Single(dangling);
        Assert.Equal(("c", 5, 8), (link.Conversation, link.FromNode, link.ToNode));
    }

    [Fact]
    public void Analyze_ReturnsEmpty_WhenNoLinkTargetsADeletedNode()
    {
        var project = Project("c", Patch("c", added: [NodeWithLink(5, 6)]));
        Assert.Empty(NodeLinkAnalyzer.Analyze(project));
    }
}
