namespace DialogEditor.Core.Analytics;

/// <summary>
/// The one word counter, shared by the canvas toolbar and Flow Analytics so they always agree
/// (GitHub issue 256): words are runs of non-whitespace, so tabs and line breaks separate words
/// just as spaces do.
/// </summary>
public static class WordCount
{
    public static int Of(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
