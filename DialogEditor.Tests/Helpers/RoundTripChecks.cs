using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Xml.Serialization;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Helpers;

/// <summary>
/// What "saved unchanged and came back intact" means, shared by the shipped-data fidelity
/// tests (issue 117) and the canonical-conversation tests (issue 122), so both judge alike.
/// Each check returns null when the two sides agree, else a short description of the first
/// difference.
/// </summary>
internal static class RoundTripChecks
{
    // What an unchanged save writes: the conversation exactly as the editor loads it.
    public static ConversationEditSnapshot UnchangedSnapshot(IGameDataProvider provider, ConversationFile file) =>
        ConversationSnapshotBuilder.Build(provider.LoadConversation(file));

    public static string? ModelDifference(IReadOnlyList<ConversationNode> before, IReadOnlyList<ConversationNode> after)
    {
        var a = before.OrderBy(n => n.NodeId).Select(Describe).ToList();
        var b = after.OrderBy(n => n.NodeId).Select(Describe).ToList();
        if (a.Count != b.Count) return $"{a.Count} nodes before, {b.Count} after";
        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return $"model differs:\n    before {a[i]}\n    after  {b[i]}";
        return null;
    }

    public static string? JsonDifference(string originalJson, string savedJson) =>
        JsonNode.DeepEquals(JsonNode.Parse(originalJson), JsonNode.Parse(savedJson))
            ? null
            : FirstNodeDifference(originalJson, savedJson);

    public static string? FirstLineDifference(string a, string b)
    {
        if (a == b) return null;
        var la = a.Split('\n');
        var lb = b.Split('\n');
        var i  = 0;
        while (i < la.Length && i < lb.Length && la[i] == lb[i]) i++;
        return $"differs in the game model at line {i + 1}:\n    before {Line(la, i)}\n    after  {Line(lb, i)}";
    }

    // Read straight from the XML rather than through StringTableParser, so the check does
    // not share the code it is checking. A missing element is null, not "".
    public static List<(int Id, string? DefaultText, string? FemaleText)> StringTableEntries(string xml) =>
        XDocument.Parse(xml).Descendants("Entry")
            .Select(e => ((int)e.Element("ID")!, (string?)e.Element("DefaultText"), (string?)e.Element("FemaleText")))
            .ToList();

    // The game's own XmlSerializer model, loaded from the install at run time: the
    // assembly ships with the game and is never referenced or redistributed by this repo.
    public static XmlSerializer Poe1GameSerializer(string installDir)
    {
        var dll = Path.Combine(installDir, "PillarsOfEternity_Data", "Managed", "OEIFormats.dll");
        Assert.True(File.Exists(dll), $"OEIFormats.dll not found at {dll}");
        var type = Assembly.LoadFrom(dll).GetType("OEIFormats.FlowCharts.Conversations.ConversationData", throwOnError: true)!;
        return new XmlSerializer(type);
    }

    /// The XML as the game sees it: deserialized into its own model and written back, so
    /// formatting (&lt;X /&gt; vs &lt;X&gt;&lt;/X&gt;) and elements the game ignores drop out.
    public static string Poe1GameCanonical(XmlSerializer gameSerializer, string xml)
    {
        using var reader = new StringReader(xml);
        var data = gameSerializer.Deserialize(reader);
        using var writer = new StringWriter();
        gameSerializer.Serialize(writer, data);
        return writer.ToString();
    }

    // Records compare lists by reference, so describe each node structurally instead.
    private static string Describe(ConversationNode n) =>
        $"#{n.NodeId} {n.SpeakerCategory} player={n.IsPlayerChoice} speaker={n.SpeakerGuid} listener={n.ListenerGuid} " +
        $"display={n.DisplayType} persist={n.Persistence} dir={n.ActorDirection} comments={n.Comments} " +
        $"vo={n.ExternalVO}/{n.HasVO} hide={n.HideSpeaker} " +
        $"links=[{string.Join("; ", n.Links.Select(l => $"{l.FromNodeId}>{l.ToNodeId} w={l.RandomWeight} q={l.QuestionNodeTextDisplay} c={Describe(l.Conditions)}"))}] " +
        $"conds={Describe(n.Conditions)} " +
        $"scripts=[{string.Join("; ", n.Scripts.Select(s => $"{s.Category}:{s.FullName}({string.Join(",", s.Parameters)})"))}]";

    private static string Describe(IReadOnlyList<ConditionNode> conditions) =>
        "[" + string.Join(", ", conditions.Select(c => c switch
        {
            ConditionLeaf l   => $"{(l.Not ? "!" : "")}{l.FullName}({string.Join(",", l.Parameters)}){l.Operator}",
            ConditionBranch b => $"{(b.Not ? "!" : "")}{Describe(b.Components)}{b.Operator}",
            _                 => c.ToString(),
        })) + "]";

    private static string Line(string[] lines, int i) => i < lines.Length ? lines[i].Trim() : "<end>";

    private static string FirstNodeDifference(string originalJson, string savedJson)
    {
        var before = JsonNode.Parse(originalJson)!["Conversations"]![0]!["Nodes"]!.AsArray();
        var after  = JsonNode.Parse(savedJson)!["Conversations"]![0]!["Nodes"]!.AsArray();
        if (before.Count != after.Count) return $"{before.Count} nodes before, {after.Count} after";
        for (var i = 0; i < before.Count; i++)
            if (!JsonNode.DeepEquals(before[i], after[i]))
                return $"node at index {i} (NodeID {before[i]?["NodeID"]}) differs";
        return "JSON outside Nodes differs";
    }
}
