namespace DialogEditor.Core.Search;

/// <summary>
/// Edit distance for "did you mean" suggestions (token typos in the editor, flag typos in
/// dialog-patcher). Case-sensitive: callers fold case first when case shouldn't count.
/// </summary>
public static class EditDistance
{
    /// <summary>
    /// Optimal-string-alignment Damerau–Levenshtein distance: insertions, deletions,
    /// substitutions and adjacent transpositions each cost one, so "verison" is one edit
    /// from "version" (a plain Levenshtein would say two).
    /// </summary>
    public static int DamerauLevenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        for (var j = 1; j <= m; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(
                d[i - 1, j] + 1,
                d[i, j - 1] + 1),
                d[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }
        return d[n, m];
    }
}
