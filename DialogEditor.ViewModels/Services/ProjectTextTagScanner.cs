using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// What kind of text problem a sweep row reports.
public enum TextIssueType { Tag, Spelling }

/// One project-wide sweep result: a token/markup or spelling problem in
/// writer-touched text. Language is "" for the primary language (displayed as
/// "Default"). Word is set on Spelling rows (feeds Add-to-dictionary).
public sealed record TextTagIssueRow(
    string ConversationName, int NodeId, string Language, string Message,
    TextIssueType Type = TextIssueType.Tag, string? Word = null);

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
        TokenValidationService? validator = null, SpellCheckService? spell = null)
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
                    Append(convName, t.NodeId, label, lang, t.DefaultText);
                    Append(convName, t.NodeId, label, lang, t.FemaleText);
                }
            }

            // Defensive: the current schema zeroes AddedNodes text at save, but a
            // legacy or hand-edited patch may still carry it.
            foreach (var n in patch.AddedNodes)
            {
                Append(convName, n.NodeId, "", primaryLanguage, n.DefaultText);
                Append(convName, n.NodeId, "", primaryLanguage, n.FemaleText);
            }
        }

        return rows
            .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
            .ThenBy(r => r.NodeId)
            .ThenBy(r => r.Language, StringComparer.Ordinal)
            .ThenBy(r => r.Type)   // tags before spelling within a node/language
            .ToList();

        void Append(string conv, int nodeId, string label, string checkLang, string? text)
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
                rows.Add(new TextTagIssueRow(conv, nodeId, label, msg));
            }

            if (spell is null) return;
            // The spell check runs against the text's ACTUAL language; skipped
            // silently when no dictionary is installed for it.
            foreach (var issue in spell.Check(text, checkLang))
            {
                var msg = issue.Suggestion is not null
                    ? Loc.Format("Spelling_Misspelled_Suggest", issue.Word, issue.Suggestion)
                    : Loc.Format("Spelling_Misspelled", issue.Word);
                rows.Add(new TextTagIssueRow(
                    conv, nodeId, label, msg, TextIssueType.Spelling, issue.Word));
            }
        }
    }
}
