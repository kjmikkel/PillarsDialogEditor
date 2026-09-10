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
    ConversationJumpWhileContinuing
}

public record FlowIssue(int NodeId, FlowIssueKind Kind);

public record FlowAnalysisReport(
    FlowStatistics           Statistics,
    IReadOnlyList<FlowIssue> Issues);
