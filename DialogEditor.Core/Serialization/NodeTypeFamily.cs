using DialogEditor.Core.Models;

namespace DialogEditor.Core.Serialization;

/// <summary>
/// Decides which node type a save writes for an existing node (issue 113).
/// </summary>
/// <remarks>
/// The parsers fold several game node types into one <see cref="SpeakerCategory"/>:
/// BankNode and TriggerConversationNode are read as <see cref="SpeakerCategory.Script"/>,
/// exactly like ScriptNode. Deriving the type from the category alone therefore rewrote
/// every bank and trigger node as a ScriptNode on save, and the game discarded their
/// type-specific data (ChildNodeIDs, BankNodePlayType, ConversationFilename/Guid,
/// StartNodeID). So the original type is kept whenever it still belongs to the family of
/// the snapshot's category; only an explicit category change maps to that category's
/// default type. The family table mirrors the parsers' ClassifySpeaker.
/// </remarks>
internal static class NodeTypeFamily
{
    private enum Family { Talk, Player, Script }

    /// <param name="originalType">The node's type in the file being saved over (PoE1
    /// <c>xsi:type</c> or PoE2 <c>$type</c>); <c>null</c> when it has none.</param>
    /// <param name="category">The category the node has in the edit snapshot.</param>
    /// <param name="typeForCategory">The game-specific default type for <paramref name="category"/>.</param>
    public static string Resolve(string? originalType, SpeakerCategory category, string typeForCategory) =>
        !string.IsNullOrEmpty(originalType) && FamilyOf(originalType) == FamilyOf(category)
            ? originalType
            : typeForCategory;

    private static Family FamilyOf(SpeakerCategory category) => category switch
    {
        SpeakerCategory.Player => Family.Player,
        SpeakerCategory.Script => Family.Script,
        _                      => Family.Talk,
    };

    // Substring match: PoE1 writes bare names ("BankNode"), PoE2 assembly-qualified ones
    // ("OEIFormats.FlowCharts.BankNode, OEIFormats"). Anything else (TalkNode, or a type
    // the editor does not know) is read as Npc/Narrator, i.e. the Talk family.
    private static Family FamilyOf(string type) =>
        type.Contains("PlayerResponseNode", StringComparison.Ordinal) ? Family.Player
        : type.Contains("ScriptNode", StringComparison.Ordinal)
          || type.Contains("BankNode", StringComparison.Ordinal)
          || type.Contains("TriggerConversationNode", StringComparison.Ordinal) ? Family.Script
        : Family.Talk;
}
