using DialogEditor.Core.Models;

namespace DialogEditor.Core.Analytics;

/// Scripts on a Deadfire script node that the game never runs (issue 167). Shared by flow
/// analysis and the detail pane, so the two cannot disagree.
public static class ScriptNodeRules
{
    /// From Deadfire's FlowChart.MoveToNode: entering a ScriptNode runs its OnEnter scripts,
    /// then, if GetNextNode finds a link to follow, makes that next node current at once. The
    /// script node is never the current node, so its OnExit scripts run only when no link can
    /// be followed, and its OnUpdate scripts never run. Bank and trigger-conversation nodes
    /// have their own branches, which do run exit scripts. PoE1's MoveToNode has no ScriptNode
    /// branch at all, so a PoE1 script node runs everything like any other node.
    public static IReadOnlyList<FlowIssueKind> NeverRun(
        string gameId, SpeakerCategory category, ScriptNodeKind kind,
        bool hasLinks, IReadOnlyList<ScriptCall> scripts)
    {
        if (!string.Equals(gameId, "poe2", StringComparison.OrdinalIgnoreCase)
            || category != SpeakerCategory.Script || kind != ScriptNodeKind.Script)
            return [];

        var kinds = new List<FlowIssueKind>(2);
        if (hasLinks && scripts.Any(s => s.Category == ScriptCategory.Exit))
            kinds.Add(FlowIssueKind.ScriptExitNeverRuns);
        if (scripts.Any(s => s.Category == ScriptCategory.Update))
            kinds.Add(FlowIssueKind.ScriptUpdateNeverRuns);
        return kinds;
    }
}
