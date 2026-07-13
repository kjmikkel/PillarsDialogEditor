using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// Which node-keyed data category a stale reference lives in.
public enum StaleDataKind { Comment, Translation, Layout }

/// Confirmed = referenced ID is in the patch's own DeletedNodeIds (zero false
/// positives). Likely = ID absent from the reconstructed effective node set
/// (may be a version-skew false positive).
public enum StaleConfidence { Confirmed, Likely }

/// One stale-data finding. Language is the raw code (e.g. "en"/"de") for
/// Translation rows, null for Comment/Layout. Display labelling is the VM's job
/// — this record is Loc-free so the scanner stays pure and testable.
public sealed record StaleDataRow(
    string ConversationName, int NodeId, StaleDataKind Kind,
    string? Language, StaleConfidence Confidence);

/// Finds NodeComments/Translations/Layouts entries pointing at nodes that no
/// longer exist. The confirmed pass is pure over project.Patches. The likely
/// pass runs only when effectiveNodeIds is supplied: it maps a conversation
/// name to its live node-ID set, or null when the conversation can't be
/// resolved (that conversation is then skipped, never flagged).
/// Spec: docs/superpowers/specs/2026-07-13-stale-patch-data-hygiene-design.md
public static class ProjectStaleDataScanner
{
    public static IReadOnlyList<StaleDataRow> Scan(
        DialogProject project,
        Func<string, IReadOnlySet<int>?>? effectiveNodeIds = null)
    {
        var rows = new List<StaleDataRow>();

        foreach (var (conv, patch) in project.Patches)
        {
            var deleted   = patch.DeletedNodeIds.ToHashSet();
            var effective = effectiveNodeIds?.Invoke(conv);

            foreach (var (kind, id, lang) in References(conv, patch, project))
            {
                if (deleted.Contains(id))
                    rows.Add(new StaleDataRow(conv, id, kind, lang, StaleConfidence.Confirmed));
                else if (effective is not null && !effective.Contains(id))
                    rows.Add(new StaleDataRow(conv, id, kind, lang, StaleConfidence.Likely));
            }
        }

        // Stable-sort by (conversation, node, kind) only — Language is deliberately
        // NOT a sort key: within a (conv, node, kind) group, rows must retain the
        // enumeration order of patch.Translations (OrderBy is a stable sort in
        // LINQ, so ties fall back to insertion order).
        return rows
            .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
            .ThenBy(r => r.NodeId)
            .ThenBy(r => r.Kind)
            .ToList();
    }

    private static IEnumerable<(StaleDataKind Kind, int NodeId, string? Lang)> References(
        string conv, ConversationPatch patch, DialogProject project)
    {
        foreach (var id in patch.NodeComments.Keys)
            yield return (StaleDataKind.Comment, id, null);

        foreach (var (lang, entries) in patch.Translations)
            foreach (var t in entries)
                yield return (StaleDataKind.Translation, t.NodeId, lang);

        var layout = project.GetLayout(conv);
        if (layout is not null)
            foreach (var id in layout.Keys)
                yield return (StaleDataKind.Layout, id, null);
    }
}
