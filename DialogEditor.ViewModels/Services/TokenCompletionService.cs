namespace DialogEditor.ViewModels.Services;

/// An open completion context under the caret: which delimiter opened it, where
/// that delimiter is, and the text typed so far (delimiter included).
public sealed record CompletionContext(char Delimiter, int FragmentStart, string Fragment);

/// The edit to apply when a completion is accepted: which span of the text to
/// replace, the text to insert, and where to leave the caret/selection.
public sealed record CompletionEdit(
    int ReplaceStart, int ReplaceLength, string InsertedText,
    int SelectionStart, int SelectionLength);

/// Pure logic for token/markup autocomplete in dialog text. Owns context
/// detection, candidate ranking, and the exact edit applied on accept — no UI.
/// Spec: docs/superpowers/specs/2026-07-06-token-autocomplete-design.md
public sealed class TokenCompletionService
{
    private readonly TagCatalogue _catalogue;

    public TokenCompletionService(TagCatalogue? catalogue = null)
        => _catalogue = catalogue ?? TagCatalogue.Instance;

    /// Finds whether the caret sits inside a token/markup being typed. Scans back
    /// to the nearest opener; a closing delimiter or line boundary first means
    /// there is no active context.
    public CompletionContext? TryGetContext(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text) || caretIndex <= 0 || caretIndex > text.Length)
            return null;

        for (var i = caretIndex - 1; i >= 0; i--)
        {
            var c = text[i];
            if (c is ']' or '>' or '\n' or '\r')
                return null; // context closed, or a line boundary — no completion
            if (c is '[' or '<')
                return new CompletionContext(c, i, text.Substring(i, caretIndex - i));
        }
        return null;
    }

    /// The offerable entries for a context, game-filtered and ranked. `[` offers
    /// Tokens, `<` offers Markup; Conventions are never offered. An empty/unknown
    /// gameId offers the union of both games.
    public IReadOnlyList<TagEntry> GetCandidates(CompletionContext context, string gameId)
    {
        var kind = context.Delimiter == '[' ? "Token" : "Markup";
        var offerUnion = string.IsNullOrEmpty(gameId);

        return _catalogue.All
            .Where(e => e.Kind == kind)
            .Where(e => offerUnion || e.Games.Any(g =>
                string.Equals(g, gameId, System.StringComparison.OrdinalIgnoreCase)))
            .Where(e => InsertionOf(e).Literal.StartsWith(
                context.Fragment, System.StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Count)
            .ThenBy(e => InsertionOf(e).Literal, System.StringComparer.Ordinal)
            .ToList();
    }

    /// Computes the text replacement and resulting caret/selection when `entry`
    /// is accepted in `context`. The View applies the result verbatim.
    public CompletionEdit ApplyCompletion(CompletionContext context, TagEntry entry)
    {
        var (literal, selStart, selLen) = InsertionOf(entry);
        return new CompletionEdit(
            ReplaceStart:    context.FragmentStart,
            ReplaceLength:   context.Fragment.Length,
            InsertedText:    literal,
            SelectionStart:  context.FragmentStart + selStart,
            SelectionLength: selLen);
    }

    /// Expands an entry's `insert` marker into literal text plus the selection to
    /// place on accept. No `insert` → the Name inserted verbatim, caret at the end.
    internal static (string Literal, int SelStart, int SelLen) InsertionOf(TagEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Insert))
            return (entry.Name, entry.Name.Length, 0);

        var s = entry.Insert!;
        var open = s.IndexOf("${", System.StringComparison.Ordinal);
        var close = s.IndexOf('}', open);
        var before = s[..open];
        var token = s[(open + 2)..close];
        var after = s[(close + 1)..];
        return (before + token + after, before.Length, token.Length);
    }
}
