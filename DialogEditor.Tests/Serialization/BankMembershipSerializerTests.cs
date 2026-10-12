using System.Text.Json.Nodes;
using System.Xml.Linq;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Serialization;

/// Issue 161: bank membership lives twice in the file, in the bank's ChildNodeIDs and in
/// each child's ContainerNodeID. The editor model carries neither (the serializers copy
/// them, #113), so a delete has to be reconciled there. Bank 5 of the canonical
/// conversation has children 8 and 9 in both games.
public class BankMembershipSerializerTests
{
    private static string Poe1Xml => File.ReadAllText(Path.Combine(CanonicalFixture.SourceDir("poe1"),
        "PillarsOfEternity_Data", "data", "conversations", "canonical", "canonical.conversation"));

    private static string Poe2Json => File.ReadAllText(Path.Combine(CanonicalFixture.SourceDir("poe2"),
        "PillarsOfEternityII_Data", "exported", "design", "conversations", "canonical.conversationbundle"));

    private static ConversationEditSnapshot Snapshot(IReadOnlyList<ConversationNode> nodes) =>
        ConversationSnapshotBuilder.Build(new Conversation("canonical", nodes, new StringTable([])));

    /// The snapshot with the given nodes deleted the way the canvas deletes them: the nodes
    /// and every link into them go.
    private static ConversationEditSnapshot Without(ConversationEditSnapshot snap, params int[] ids) =>
        new(snap.Nodes.Where(n => !ids.Contains(n.NodeId))
                      .Select(n => n with { Links = n.Links.Where(l => !ids.Contains(l.ToNodeId)).ToList() })
                      .ToList());

    // ── PoE1 ──────────────────────────────────────────────────────────────

    private static XElement Poe1Node(string xml, int id) =>
        XDocument.Parse(xml).Descendants("FlowChartNode").Single(n => (int)n.Element("NodeID")! == id);

    private static string Poe1Save(params int[] deleted) =>
        Poe1ConversationSerializer.Serialize(Poe1Xml, Without(Snapshot(Poe1ConversationParser.ParseXml(Poe1Xml)), deleted));

    [Fact]
    public void Poe1_DeletingABankChild_DropsItFromTheBank()
    {
        var bank = Poe1Node(Poe1Save(9), 5);
        Assert.Equal([8], bank.Element("ChildNodeIDs")!.Elements("int").Select(e => (int)e));
    }

    [Fact]
    public void Poe1_DeletingTheFirstBankChild_KeepsTheRest()
    {
        var bank = Poe1Node(Poe1Save(8), 5);
        Assert.Equal([9], bank.Element("ChildNodeIDs")!.Elements("int").Select(e => (int)e));
    }

    [Fact]
    public void Poe1_DeletingTheBank_TakesItsChildrenOutOfIt()
    {
        var xml = Poe1Save(5);
        Assert.Equal(-1, (int)Poe1Node(xml, 8).Element("ContainerNodeID")!);
        Assert.Equal(-1, (int)Poe1Node(xml, 9).Element("ContainerNodeID")!);
    }

    [Fact]
    public void Poe1_SaveWithoutDeletes_KeepsMembership()
    {
        var xml = Poe1Save();
        Assert.Equal([8, 9], Poe1Node(xml, 5).Element("ChildNodeIDs")!.Elements("int").Select(e => (int)e));
        Assert.Equal(5, (int)Poe1Node(xml, 9).Element("ContainerNodeID")!);
    }

    // ── PoE2 ──────────────────────────────────────────────────────────────

    private static JsonNode Poe2Node(string json, int id) =>
        JsonNode.Parse(json)!["Conversations"]![0]!["Nodes"]!.AsArray().Single(n => n!["NodeID"]!.GetValue<int>() == id)!;

    private static string Poe2Save(params int[] deleted) =>
        Poe2ConversationSerializer.Serialize(Poe2Json, Without(Snapshot(Poe2ConversationParser.ParseJson(Poe2Json)), deleted));

    private static IEnumerable<int> Children(JsonNode bank) =>
        bank["ChildNodeIDs"]!.AsArray().Select(i => i!.GetValue<int>());

    [Fact]
    public void Poe2_DeletingABankChild_DropsItFromTheBank()
    {
        Assert.Equal([8], Children(Poe2Node(Poe2Save(9), 5)));
    }

    [Fact]
    public void Poe2_DeletingTheFirstBankChild_KeepsTheRest()
    {
        Assert.Equal([9], Children(Poe2Node(Poe2Save(8), 5)));
    }

    [Fact]
    public void Poe2_DeletingTheBank_TakesItsChildrenOutOfIt()
    {
        var json = Poe2Save(5);
        Assert.Equal(-1, Poe2Node(json, 8)["ContainerNodeID"]!.GetValue<int>());
        Assert.Equal(-1, Poe2Node(json, 9)["ContainerNodeID"]!.GetValue<int>());
    }

    [Fact]
    public void Poe2_SaveWithoutDeletes_KeepsMembership()
    {
        var json = Poe2Save();
        Assert.Equal([8, 9], Children(Poe2Node(json, 5)));
        Assert.Equal(5, Poe2Node(json, 9)["ContainerNodeID"]!.GetValue<int>());
    }
}
