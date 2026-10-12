namespace DialogEditor.Core.Models;

/// Which game node type a <see cref="SpeakerCategory.Script"/> node really is. The parsers
/// fold ScriptNode, BankNode and TriggerConversationNode into one category, but Deadfire runs
/// their exit and update scripts differently (FlowChart.MoveToNode), so validation needs to
/// tell them apart (issue 167). Meaningless for any other category.
public enum ScriptNodeKind { Script, Bank, TriggerConversation }

public static class ScriptNodeKinds
{
    /// The kind for a node's game type. Substring match, like the parsers' ClassifySpeaker:
    /// PoE1 writes bare names ("BankNode"), PoE2 assembly-qualified ones.
    public static ScriptNodeKind FromType(string? type) =>
        type is null                                                        ? ScriptNodeKind.Script
        : type.Contains("BankNode", StringComparison.Ordinal)                ? ScriptNodeKind.Bank
        : type.Contains("TriggerConversationNode", StringComparison.Ordinal) ? ScriptNodeKind.TriggerConversation
        : ScriptNodeKind.Script;
}
