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
    BarkHasPlayerChoiceChild
}

public record FlowIssue(int NodeId, FlowIssueKind Kind);

public record FlowAnalysisReport(
    FlowStatistics           Statistics,
    IReadOnlyList<FlowIssue> Issues);
