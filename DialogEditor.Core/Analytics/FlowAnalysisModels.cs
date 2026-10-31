namespace DialogEditor.Core.Analytics;

public record FlowStatistics(
    int    TotalNodes,
    int    WordCount,
    int    MaxDepth,
    int    PlayerCount,
    int    NpcCount,
    int    NarratorCount,
    int    ScriptCount,
    double AvgLinksPerNode,
    int    ConditionalLinkCount,
    int    TotalLinkCount);

public enum FlowIssueKind
{
    Unreachable,
    PlayerDeadEnd,
    EmptyText,
    NoIncomingLinks,
    BarkTextTooLong,
    BarkHasPlayerChoiceChild,

    /// A node that both links onward AND starts another conversation. The engine spawns a
    /// second FlowChartPlayer without stopping the current one, so both really do run —
    /// almost always an authoring mistake. Reported whether or not path stats are set to
    /// follow handoffs: this is a graph-shape defect, not a stats setting (#14).
    ConversationJumpWhileContinuing,

    /// Deadfire only: a ScriptNode that links onward and has OnExit scripts. The game runs
    /// its OnEnter scripts and moves straight on (FlowChart.MoveToNode), so the exit scripts
    /// run only when none of its links can be followed (issue 167).
    ScriptExitNeverRuns,

    /// Deadfire only: a ScriptNode with OnUpdate scripts. A script node is never the current
    /// node, and update scripts run only on the current node, so they never run (issue 167).
    ScriptUpdateNeverRuns,

    /// A link that can never fire because an earlier link on the same node always passes
    /// (the games take the first passing link; issue 353). Reported on the source node, one
    /// per hidden link.
    LinkShadowed
}

public record FlowIssue(int NodeId, FlowIssueKind Kind)
{
    /// LinkShadowed: the hidden link's target node.
    public int? RelatedNodeId { get; init; }

    /// LinkShadowed: the target node of the earlier link that hides it.
    public int? HiderNodeId { get; init; }
}

public record FlowAnalysisReport(
    FlowStatistics           Statistics,
    IReadOnlyList<FlowIssue> Issues)
{
    /// False when the conversation has nodes but no node 0 (issue 172). Unreachable is then
    /// not reported: walking from a start node that doesn't exist would flag every node.
    public bool HasStartNode { get; init; } = true;
}
