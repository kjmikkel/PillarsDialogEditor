using System.Text.Json.Nodes;

namespace DialogEditor.Core.Serialization;

/// <summary>
/// Which of the node properties the PoE2 serializer writes each node type actually
/// declares (issue 116).
/// </summary>
/// <remarks>
/// The serializer used to set SpeakerGuid, ListenerGuid, HasVO, ExternalVO, HideSpeaker,
/// DisplayType and Persistence on every node. Deadfire's bundle parser reads each node
/// against its $type and answers every property that type lacks with
/// <c>Debug.LogError("Parse read unexpected property …")</c>, then drops it: an unchanged
/// save of the shipped conversations logged ~68,000 such errors.
/// The table mirrors OEIFormats (TalkNode : DialogueNode : FlowChartNode, BankNode :
/// FlowChartNode) and matches the key sets of all ~52,000 nodes in the shipped bundles.
/// A type not in the table keeps a property only if the original node already had it.
/// </remarks>
internal static class Poe2NodeProperties
{
    // TalkNode only: player responses, script and trigger nodes have no speaker or VO.
    private static readonly string[] TalkNode = ["SpeakerGuid", "ListenerGuid", "HasVO", "ExternalVO"];

    // DialogueNode, shared by TalkNode, PlayerResponseNode, ScriptNode and
    // TriggerConversationNode; only Persistence is also on BankNode.
    private static readonly string[] DialogueNode =
    [
        "HideSpeaker", "DisplayType", "Persistence", "IsQuestionNode", "IsTempText",
        "PlayVOAs3DSound", "PlayType", "NoPlayRandomWeight", "VOPositioning", "NotSkippable",
    ];

    private static readonly string[] BankNode = ["Persistence"];

    /// <summary>Every property this serializer may set on a node.</summary>
    private static readonly string[] Managed = [.. TalkNode, .. DialogueNode];

    /// <summary>
    /// Removes each managed property that <paramref name="node"/>'s final $type does not
    /// declare. <paramref name="original"/> is the node as it was in the file, or
    /// <c>null</c> for a new node.
    /// </summary>
    public static void RemoveUndeclared(JsonNode node, JsonNode? original)
    {
        var declared = Declared(node["$type"]?.GetValue<string>() ?? "");
        var obj      = node.AsObject();
        foreach (var key in Managed)
        {
            var keep = declared?.Contains(key) ?? original?.AsObject().ContainsKey(key) ?? false;
            if (!keep) obj.Remove(key);
        }
    }

    // Substring match: $type is assembly-qualified ("OEIFormats.FlowCharts.BankNode, OEIFormats").
    private static string[]? Declared(string type) =>
        type.Contains("TalkNode", StringComparison.Ordinal)                ? [.. TalkNode, .. DialogueNode]
        : type.Contains("PlayerResponseNode", StringComparison.Ordinal)
          || type.Contains("ScriptNode", StringComparison.Ordinal)
          || type.Contains("TriggerConversationNode", StringComparison.Ordinal) ? DialogueNode
        : type.Contains("BankNode", StringComparison.Ordinal)              ? BankNode
        : null;
}
