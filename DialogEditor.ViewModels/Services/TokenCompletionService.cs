namespace DialogEditor.ViewModels.Services;

/// An open completion context under the caret: which delimiter opened it, where
/// that delimiter is, and the text typed so far (delimiter included).
public sealed record CompletionContext(char Delimiter, int FragmentStart, string Fragment);

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
}
