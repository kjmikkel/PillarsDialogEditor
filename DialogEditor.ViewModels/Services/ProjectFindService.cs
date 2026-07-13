using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// Project-wide read-only find over the EFFECTIVE text (vanilla base + the writer's
/// edits) of every patched conversation. Mirrors ProjectVoRowScanner's walk: the open
/// conversation is searched via its live snapshot (unsaved edits included); every other
/// patched conversation is loaded vanilla + patch; an unreadable conversation is skipped.
/// Spec: docs/superpowers/specs/2026-07-12-find-in-project-design.md
public static class ProjectFindService
{
    public static IReadOnlyList<FindMatchRow> Search(
        DialogProject project, IGameDataProvider provider, string primaryLanguage,
        ProjectFindQuery query,
        string? openConversationName = null, ConversationEditSnapshot? openSnapshot = null)
    {
        var rows = new List<FindMatchRow>();
        if (string.IsNullOrEmpty(query.Text)) return rows;
        var cmp = query.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var (convName, patch) in project.Patches)
        {
            ConversationEditSnapshot snap;
            if (convName == openConversationName && openSnapshot is not null)
            {
                snap = openSnapshot;
            }
            else
            {
                try
                {
                    var file     = provider.FindConversation(convName);
                    var baseSnap = file is not null
                        ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                        : new ConversationEditSnapshot([]);
                    snap = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Find in project: could not load '{convName}': {ex.Message}");
                    continue;
                }
            }

            var primaryTranslations = (patch.Translations.GetValueOrDefault(primaryLanguage) ?? [])
                .ToDictionary(t => t.NodeId);

            foreach (var node in snap.Nodes)
            {
                // Added nodes loaded from disk have empty [JsonIgnore] text; fall back
                // to the primary translation entry (ProjectVoRowScanner precedent).
                var def = !string.IsNullOrEmpty(node.DefaultText) ? node.DefaultText
                        : primaryTranslations.TryGetValue(node.NodeId, out var pt) ? pt.DefaultText ?? "" : "";
                var fem = !string.IsNullOrEmpty(node.FemaleText) ? node.FemaleText
                        : primaryTranslations.TryGetValue(node.NodeId, out var pf) ? pf.FemaleText ?? "" : "";

                Check(convName, node.NodeId, "FindField_DefaultText", "", def, query.Text, cmp, rows);
                Check(convName, node.NodeId, "FindField_FemaleText",  "", fem, query.Text, cmp, rows);

                if (query.InLinkChoice)
                    foreach (var link in node.Links)
                        Check(convName, node.NodeId, "FindField_LinkChoice", "",
                              link.QuestionNodeTextDisplay, query.Text, cmp, rows);

                if (query.InNodeComments &&
                    patch.NodeComments.TryGetValue(node.NodeId, out var comment))
                    Check(convName, node.NodeId, "FindField_NodeComment", "",
                          comment, query.Text, cmp, rows);
            }

            if (query.InTranslations)
                foreach (var (lang, entries) in patch.Translations)
                {
                    if (string.Equals(lang, primaryLanguage, StringComparison.OrdinalIgnoreCase))
                        continue; // primary covered by the Default/Female fallback
                    foreach (var t in entries)
                    {
                        Check(convName, t.NodeId, "FindField_DefaultText", lang, t.DefaultText ?? "", query.Text, cmp, rows);
                        Check(convName, t.NodeId, "FindField_FemaleText",  lang, t.FemaleText ?? "", query.Text, cmp, rows);
                    }
                }
        }

        return rows
            .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
            .ThenBy(r => r.NodeId)
            .ThenBy(r => r.FieldLabel, StringComparer.Ordinal)
            .ThenBy(r => r.Language, StringComparer.Ordinal)
            .ToList();
    }

    private static void Check(string conv, int nodeId, string fieldKey, string lang,
        string value, string search, StringComparison cmp, List<FindMatchRow> rows)
    {
        if (string.IsNullOrEmpty(value)) return;
        var idx = value.IndexOf(search, cmp);
        if (idx < 0) return;
        rows.Add(new FindMatchRow(conv, nodeId, Loc.Get(fieldKey), lang,
            FindSnippet.Extract(value, idx, search.Length)));
    }
}
