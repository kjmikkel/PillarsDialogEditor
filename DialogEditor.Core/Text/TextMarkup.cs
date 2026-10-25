using System.Text;
using System.Text.RegularExpressions;

namespace DialogEditor.Core.Text;

/// <summary>One stretch of text with the styling the game's markup gives it.</summary>
public sealed record MarkupRun(string Text, bool Italic, bool Bold);

/// <summary>
/// The game's inline text markup, for showing node text outside the edit field
/// (GitHub issue 258): cards render it as italic and bold runs, one-line summaries drop it.
/// The edit field and the search tools keep the raw text, since that is what gets saved.
///
/// Only the tags the game uses are recognised (see the Markup entries in tags.json), so a
/// literal "&lt;3" or an unknown tag is left as written. <c>&lt;i&gt;</c> and
/// <c>&lt;ispeech&gt;</c> (inner speech) render italic and <c>&lt;b&gt;</c> bold; colour, links
/// and dialect styling keep their inner text unstyled, and an inline sprite is dropped.
/// </summary>
public static partial class TextMarkup
{
    // "<name", optional attributes up to the next '>', or "</name>". The attribute part is not
    // parsed, which also tolerates the shipped links that miss a closing quote.
    [GeneratedRegex(@"<(?<close>/?)(?<name>[a-zA-Z]+)(?=[\s=>])[^>]*>")]
    private static partial Regex Tag();

    private static readonly HashSet<string> Italic = new(StringComparer.OrdinalIgnoreCase) { "i", "ispeech" };
    private static readonly HashSet<string> Bold = new(StringComparer.OrdinalIgnoreCase) { "b" };
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
        { "i", "ispeech", "b", "xg", "color", "link", "sprite" };

    /// <summary>The text with the game's markup tags removed.</summary>
    public static string Strip(string text)
    {
        if (text.IndexOf('<') < 0) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var run in Parse(text)) sb.Append(run.Text);
        return sb.ToString();
    }

    /// <summary>
    /// The text split into styled runs. An unclosed tag styles to the end of the text and a
    /// close with no matching open is dropped, so damaged markup still reads sensibly.
    /// </summary>
    public static IReadOnlyList<MarkupRun> Parse(string text)
    {
        var runs = new List<MarkupRun>();
        var italic = 0;
        var bold = 0;
        var pos = 0;
        var pending = new StringBuilder();

        void Flush()
        {
            if (pending.Length == 0) return;
            var styled = (italic > 0, bold > 0);
            if (runs.Count > 0 && (runs[^1].Italic, runs[^1].Bold) == styled)
                runs[^1] = runs[^1] with { Text = runs[^1].Text + pending };
            else
                runs.Add(new MarkupRun(pending.ToString(), styled.Item1, styled.Item2));
            pending.Clear();
        }

        foreach (Match m in Tag().Matches(text))
        {
            var name = m.Groups["name"].Value;
            if (!Known.Contains(name)) continue;

            pending.Append(text, pos, m.Index - pos);
            pos = m.Index + m.Length;

            var delta = m.Groups["close"].Value == "/" ? -1 : 1;
            if (Italic.Contains(name) && italic + delta >= 0) { Flush(); italic += delta; }
            else if (Bold.Contains(name) && bold + delta >= 0) { Flush(); bold += delta; }
        }

        pending.Append(text, pos, text.Length - pos);
        Flush();
        if (runs.Count == 0) runs.Add(new MarkupRun(string.Empty, false, false));
        return runs;
    }
}
