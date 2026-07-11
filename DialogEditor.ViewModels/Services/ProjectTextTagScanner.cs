using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// One project-wide sweep result: a token/markup problem in writer-touched text.
/// Language is "" for the primary language (displayed as "Default").
public sealed record TextTagIssueRow(
    string ConversationName, int NodeId, string Language, string Message);

/// Scans every saved patch's text (all languages) for token typos and unbalanced
/// markup. Pure and IO-free: DiffEngine stores ALL dialog text in
/// patch.Translations[lang] (AddedNodes text is zeroed at save; FieldChanges never
/// holds dialog text — DiffEngine.cs:21,69), so the walk is Translations plus a
/// defensive AddedNodes check for legacy patches.
/// Spec: docs/superpowers/specs/2026-07-09-text-tag-project-sweep-design.md
public static class ProjectTextTagScanner
{
    public static IReadOnlyList<TextTagIssueRow> Scan(
        DialogProject project, string gameId, string primaryLanguage,
        TokenValidationService? validator = null)
    {
        validator ??= new TokenValidationService();
        var rows = new List<TextTagIssueRow>();

        foreach (var (convName, patch) in project.Patches)
        {
            if (patch.IsEmpty) continue;

            foreach (var (lang, entries) in patch.Translations)
            {
                var label = string.Equals(lang, primaryLanguage, StringComparison.OrdinalIgnoreCase)
                    ? "" : lang;
                foreach (var t in entries)
                {
                    Append(convName, t.NodeId, label, t.DefaultText);
                    Append(convName, t.NodeId, label, t.FemaleText);
                }
            }

            // Defensive: the current schema zeroes AddedNodes text at save, but a
            // legacy or hand-edited patch may still carry it.
            foreach (var n in patch.AddedNodes)
            {
                Append(convName, n.NodeId, "", n.DefaultText);
                Append(convName, n.NodeId, "", n.FemaleText);
            }
        }

        return rows
            .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
            .ThenBy(r => r.NodeId)
            .ThenBy(r => r.Language, StringComparer.Ordinal)
            .ToList();

        void Append(string conv, int nodeId, string lang, string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var issue in validator!.Validate(text, gameId))
            {
                var msg = issue.Kind switch
                {
                    TokenIssueKind.UnbalancedMarkup =>
                        Loc.Format("Validation_UnbalancedMarkup", issue.Fragment),
                    _ when issue.Suggestion is not null =>
                        Loc.Format("Validation_UnknownToken_Suggest", issue.Fragment, issue.Suggestion),
                    _ => Loc.Format("Validation_UnknownToken", issue.Fragment),
                };
                rows.Add(new TextTagIssueRow(conv, nodeId, lang, msg));
            }
        }
    }
}
