using DialogEditor.Core.Localisation;

namespace DialogEditor.Patch;

/// <summary>
/// Plain-text description of a cross-mod conflict for dialog-patcher's console output
/// (issue #6). The Patch Manager renders the same conflicts through
/// PatchConflictRowViewModel and its localised resources; the CLI is English-only, like
/// the rest of its help and diagnostics, so the text lives here where it can be tested.
///
/// Each line says who wins where PatchMerger's outcome depends on load order, so the
/// user knows which argument to move. A deletion is reported without a winner: the merged
/// patch deletes the node whatever the order.
/// </summary>
[NotLocalised("dialog-patcher console output; the Patch Manager uses PatchConflictRowViewModel")]
public static class CrossModConflictReport
{
    public static string Describe(PatchConflict c, IReadOnlyList<string> projectNames)
    {
        var first  = projectNames[c.FirstPatchIndex];
        var second = projectNames[c.SecondPatchIndex];
        const string LaterWins = "(later in the load order)";

        return c.Kind switch
        {
            PatchConflictKind.Deletion =>
                $"{c.ConversationName}, node {c.NodeId}: deleted by '{first}' but changed by '{second}'",
            PatchConflictKind.AddedNode =>
                $"{c.ConversationName}, node {c.NodeId}: added by both '{first}' and '{second}'; " +
                $"the node from '{second}' replaces the other {LaterWins}",
            PatchConflictKind.Link =>
                $"{c.ConversationName}, link {c.NodeId} -> {c.LinkToNodeId}: changed by '{first}' and '{second}'; " +
                $"'{second}' wins {LaterWins}",
            PatchConflictKind.Text =>
                $"{c.ConversationName}, node {c.NodeId}, text ({c.Language}): changed by '{first}' and '{second}'; " +
                $"'{second}' wins {LaterWins}",
            _ =>
                $"{c.ConversationName}, node {c.NodeId}, {c.FieldName}: changed by '{first}' and '{second}'; " +
                $"'{second}' wins {LaterWins}",
        };
    }
}
