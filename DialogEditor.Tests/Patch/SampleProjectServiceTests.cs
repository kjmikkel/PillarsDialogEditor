using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using Xunit;

namespace DialogEditor.Tests.Patch;

public class SampleProjectServiceTests
{
    private sealed class OkGit : IGitRunner
    {
        public GitResult Run(string workingDirectory, params string[] args) => new(0, "", "");
    }

    // A 3-node line: 1 (root → 2), 2 (→ 3), 3 (leaf). Node 1 = anchor, node 3 = deletable leaf.
    private static Conversation ThreeNodeEder()
    {
        ConversationNode N(int id, int? linkTo) => new(
            NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: linkTo is int t ? [new NodeLink(id, t, [], 1f, "")] : [],
            Conditions: [], Scripts: [], DisplayType: "ConversationLine", Persistence: "None",
            ActorDirection: "", Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false);

        var nodes = new List<ConversationNode> { N(1, 2), N(2, 3), N(3, null) };
        var strings = new StringTable(new[]
        {
            new StringEntry(1, "Hello there.", ""),
            new StringEntry(2, "I am Eder.", ""),
            new StringEntry(3, "Farewell.", ""),
        });
        // Named to match the service's poe1 target so the test is independent of the
        // literal constant value (which is confirmed against real game data later).
        return new Conversation(SampleProjectService.Poe1SampleConversation, nodes, strings);
    }

    [Fact]
    public void BuildSample_ProducesAllFourDemoEdits()
    {
        var provider = new FakeGameDataProvider("poe1", "en", ThreeNodeEder());
        var build = new SampleProjectService(new OkGit()).BuildSample(provider);

        Assert.Equal("sample-poe1.dialogproject", build.ProjectFileName);

        var patch = build.Final.Patches[SampleProjectService.Poe1SampleConversation];

        // Edit 2a — an added node.
        Assert.Contains(patch.AddedNodes, n => n.NodeId == 4);
        // Edit 2b — the leaf node removed.
        Assert.Contains(3, patch.DeletedNodeIds);
        // Edit 1 + added-node text — both land in Translations[en].
        Assert.Contains(patch.Translations["en"], t => t.NodeId == 1);
        Assert.Contains(patch.Translations["en"], t => t.NodeId == 4);
        // Edit 3 — translator note on the anchor.
        Assert.Contains(1, patch.NodeComments.Keys);
        // The added link 1 → 4 and the removed link 2 → 3 are recorded.
        Assert.Contains(patch.ModifiedNodes, m => m.NodeId == 1 && m.AddedLinks.Any(l => l.ToNodeId == 4));
        Assert.Contains(patch.ModifiedNodes, m => m.NodeId == 2 && m.DeletedLinks.Any(l => l.ToNodeId == 3));
    }

    [Fact]
    public void BuildSample_ConversationMissing_Throws()
    {
        var provider = new FakeGameDataProvider("poe1", "en"); // no conversations
        Assert.Throws<SampleConversationNotFoundException>(
            () => new SampleProjectService(new OkGit()).BuildSample(provider));
    }
}
