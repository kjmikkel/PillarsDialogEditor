namespace DialogEditor.Patch.GitConflict;

/// Reconstructs the "mine" (ours) and "theirs" sides of a git-conflicted text
/// file purely from its conflict markers. No git dependency.
public static class GitConflictMarkers
{
    private const string Start = "<<<<<<<";
    private const string Base  = "|||||||";
    private const string Mid   = "=======";
    private const string End   = ">>>>>>>";

    public static bool HasMarkers(string text)
    {
        foreach (var line in SplitLines(text))
            if (line.StartsWith(Start, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// Returns the two full-file reconstructions. Outside conflict hunks the two
    /// strings are identical; a diff3 base section (||||||| … =======) is dropped
    /// from both sides.
    public static (string Mine, string Theirs) SplitSides(string text)
    {
        var mine   = new System.Text.StringBuilder();
        var theirs = new System.Text.StringBuilder();

        // 0 = common, 1 = ours hunk, 2 = base hunk (drop), 3 = theirs hunk
        int region = 0;
        foreach (var line in SplitLines(text))
        {
            if (line.StartsWith(Start, StringComparison.Ordinal)) { region = 1; continue; }
            if (line.StartsWith(Base,  StringComparison.Ordinal)) { region = 2; continue; }
            if (line.StartsWith(Mid,   StringComparison.Ordinal)) { region = 3; continue; }
            if (line.StartsWith(End,   StringComparison.Ordinal)) { region = 0; continue; }

            switch (region)
            {
                case 0: mine.Append(line); theirs.Append(line); break;
                case 1: mine.Append(line); break;
                case 3: theirs.Append(line); break;
                // case 2: base — dropped
            }
        }
        return (mine.ToString(), theirs.ToString());
    }

    // Yields each line WITH its trailing newline preserved so reconstruction is faithful.
    private static IEnumerable<string> SplitLines(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            int nl = text.IndexOf('\n', i);
            if (nl < 0) { yield return text[i..]; break; }
            yield return text[i..(nl + 1)];
            i = nl + 1;
        }
    }
}
