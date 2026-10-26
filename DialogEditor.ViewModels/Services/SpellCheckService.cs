using System.Text.RegularExpressions;

namespace DialogEditor.ViewModels.Services;

/// An exact misspelt-word span (start index and length) in the checked text.
public readonly record struct SpellSpan(int Start, int Length);

/// A misspelled word found in dialog text: the word, its first position in the
/// text, and Hunspell's top suggestion (null when none).
public sealed record SpellIssue(string Word, int Position, string? Suggestion);

/// Spell-checks dialog text against the three-layer SpellDictionaryStore.
/// Tag/token spans are never checked (same span rules as TokenValidationService);
/// text in a language with no installed layer-1 dictionary is skipped entirely.
/// Pure, non-throwing. Spec: 2026-07-11-spell-checker-design.md.
public sealed class SpellCheckService
{
    private readonly SpellDictionaryStore _store;

    public SpellCheckService(SpellDictionaryStore store) => _store = store;

    // Same spans TokenValidationService treats as tag territory.
    private static readonly Regex BracketSpan = new(@"\[[^\[\]\n\r]*\]", RegexOptions.Compiled);
    private static readonly Regex MarkupSpan  = new(@"<[^>]*>", RegexOptions.Compiled);

    // Tokens: word chars + apostrophes/hyphens. Tokens containing digits ("3rd",
    // "x2") are skipped entirely rather than yielding letter fragments ("rd").
    // Mirrors lexicon-gen's tokenize.
    private static readonly Regex Token = new(@"[\w'’-]+", RegexOptions.Compiled);

    public IReadOnlyList<SpellIssue> Check(string text, string languageCode)
    {
        var issues = new List<SpellIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (word, _, token) in Words(text, languageCode))
        {
            if (!seen.Add(word)) continue;                      // report each word once
            if (_store.IsCorrect(word, languageCode)) continue;
            issues.Add(new SpellIssue(word, token, _store.Suggest(word, languageCode)));
        }
        return issues;
    }

    /// Every occurrence of every misspelt word, as exact spans for inline squiggles
    /// (issue 9). Runs per keystroke, so it never asks Hunspell for suggestions, and
    /// caches the verdict per distinct word within the call.
    public IReadOnlyList<SpellSpan> FindMisspellings(string text, string languageCode)
    {
        var spans = new List<SpellSpan>();
        var verdicts = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (word, start, _) in Words(text, languageCode))
        {
            if (!verdicts.TryGetValue(word, out var ok))
                verdicts[word] = ok = _store.IsCorrect(word, languageCode);
            if (!ok) spans.Add(new SpellSpan(start, word.Length));
        }
        return spans;
    }

    /// The checkable words (trimmed of edge apostrophes/hyphens) with their real start
    /// index and their raw token index, skipping tags, single letters and tokens with
    /// digits/underscores. Shared by Check and FindMisspellings.
    private IEnumerable<(string Word, int Start, int TokenIndex)> Words(string text, string languageCode)
    {
        if (string.IsNullOrEmpty(text) || !_store.HasDictionary(languageCode))
            yield break;

        // Blank out tag spans with spaces so word positions stay meaningful.
        var stripped = MarkupSpan.Replace(
            BracketSpan.Replace(text, m => new string(' ', m.Length)),
            m => new string(' ', m.Length));

        foreach (Match m in Token.Matches(stripped))
        {
            var word = m.Value.Trim('\'', '’', '-');
            if (word.Length < 2) continue;                      // single letters aren't words
            if (word.Any(c => char.IsDigit(c) || c == '_')) continue; // "3rd", "x2", ids
            var lead = m.Value.Length - m.Value.TrimStart('\'', '’', '-').Length;
            yield return (word, m.Index + lead, m.Index);
        }
    }
}
