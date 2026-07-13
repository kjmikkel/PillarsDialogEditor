namespace DialogEditor.ViewModels.Services;

/// Builds a one-line, context-bounded preview around a match for a find result.
public static class FindSnippet
{
    public static string Extract(string text, int matchIndex, int matchLength, int context = 30)
    {
        var start = Math.Max(0, matchIndex - context);
        var end   = Math.Min(text.Length, matchIndex + matchLength + context);
        var slice = text[start..end].Replace("\r", " ").Replace("\n", " ");
        var prefix = start > 0 ? "…" : "";
        var suffix = end < text.Length ? "…" : "";
        return prefix + slice + suffix;
    }
}
