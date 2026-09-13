using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Analytics;

/// <summary>
/// Identifies a node across conversations.
///
/// The single-conversation overload of <see cref="PathStatsService.Analyze(ConversationEditSnapshot)"/>
/// uses the empty string as the conversation: a <see cref="ConversationEditSnapshot"/> carries no
/// name, and inventing one would be a lie. Every NodeRef in that mode is therefore ("", nodeId).
/// </summary>
public readonly record struct NodeRef(string Conversation, int NodeId);

/// <summary>
/// A resolved StartConversation handoff: the node running the script, and the node it starts.
///
/// Kept as a flat edge list on <see cref="MultiConversationGraph"/> rather than an adjacency
/// map, so the resolver never has to think about lookup shape; PathStatsService groups by
/// <see cref="From"/> once at the start of the analysis.
/// </summary>
public record JumpEdge(NodeRef From, NodeRef To);

/// Why a discovered handoff was not walked.
public enum UnfollowedReason
{
    /// The target conversation is not one this project patches (the traversal boundary).
    NotPatched,

    /// The target could not be identified — an unknown conversation GUID, or a script whose
    /// catalogue entry does not declare a Conversation parameter.
    Unresolved,

    /// The target was identified and patched, but reading it from disk failed.
    LoadFailed,
}

/// <summary>
/// A handoff that was found but not walked.
///
/// Reported rather than dropped so a project-patched-only boundary never silently removes
/// content from the end of a report — a writer can see that a number stops here, and why.
/// <see cref="TargetLabel"/> is display text, never an identifier.
/// </summary>
public record UnfollowedJump(NodeRef From, string TargetLabel, UnfollowedReason Reason);

/// <summary>
/// One or more conversations plus the handoffs between them, resolved and ready to analyse.
///
/// Produced by ConversationJumpResolver (in DialogEditor.ViewModels, which owns the IO, patch
/// application and GUID resolution) and consumed by <see cref="PathStatsService"/> here, which
/// stays pure because DialogEditor.Core has no project references.
///
/// Node text in <see cref="Conversations"/> is already effective text: the resolver restores
/// it from the patch's translations for added nodes, whose DefaultText is [JsonIgnore] and
/// therefore empty when loaded from a patch.
/// </summary>
public record MultiConversationGraph(
    string RootConversation,
    IReadOnlyDictionary<string, ConversationEditSnapshot> Conversations,
    IReadOnlyList<JumpEdge>       Jumps,
    IReadOnlyList<UnfollowedJump> Unfollowed);

/// <summary>
/// Script verbs that actually start another conversation.
///
/// Deliberately a whitelist rather than "any script declaring a Conversation lookup kind":
/// MarkConversationNodeAsRead(Guid, Int32) and ClearConversationNodeAsRead(Guid, Int32) have an
/// identical parameter shape — a Conversation lookup kind plus a "Conversation Node ID" — and
/// start nothing. Matching on parameter shape alone would report phantom handoffs, silently and
/// with no exception. Pinned by tests in both FlowAnalysisServiceTests and
/// ConversationJumpResolverTests.
///
/// Lives in Core so FlowAnalysisService (which detects the verb but has no catalogue access)
/// and ConversationJumpResolver (which additionally resolves the arguments) cannot drift apart.
/// </summary>
public static class ConversationJumpVerbs
{
    public static readonly IReadOnlyList<string> All =
        ["StartConversation", "StartConversationFacingListener"];

    public static bool IsJump(string displayName) =>
        All.Contains(displayName, StringComparer.Ordinal);
}
