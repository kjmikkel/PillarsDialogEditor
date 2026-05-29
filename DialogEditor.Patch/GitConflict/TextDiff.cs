namespace DialogEditor.Patch.GitConflict;

public enum DiffKind { Common, MineOnly, TheirsOnly }

public record DiffSpan(string Text, DiffKind Kind);

/// Minimal inline diff: the shared leading prefix and trailing suffix are
/// emitted as Common spans, and whatever differs in the middle becomes a
/// MineOnly span followed by a TheirsOnly span. Char-level (not token-level) —
/// simple, allocation-light, and good enough to highlight what changed in a
/// short dialogue field.
public static class TextDiff
{
    public static IReadOnlyList<DiffSpan> Diff(string mine, string theirs)
    {
        int max = Math.Min(mine.Length, theirs.Length);

        int prefix = 0;
        while (prefix < max && mine[prefix] == theirs[prefix])
            prefix++;

        int suffix = 0;
        while (suffix < max - prefix
               && mine[mine.Length - 1 - suffix] == theirs[theirs.Length - 1 - suffix])
            suffix++;

        var spans = new List<DiffSpan>();

        if (prefix > 0)
            spans.Add(new DiffSpan(mine[..prefix], DiffKind.Common));

        var mineMid   = mine[prefix..(mine.Length - suffix)];
        var theirsMid = theirs[prefix..(theirs.Length - suffix)];
        if (mineMid.Length > 0)
            spans.Add(new DiffSpan(mineMid, DiffKind.MineOnly));
        if (theirsMid.Length > 0)
            spans.Add(new DiffSpan(theirsMid, DiffKind.TheirsOnly));

        if (suffix > 0)
            spans.Add(new DiffSpan(mine[(mine.Length - suffix)..], DiffKind.Common));

        return spans;
    }
}
