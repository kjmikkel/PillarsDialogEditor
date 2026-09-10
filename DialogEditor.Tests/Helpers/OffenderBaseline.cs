namespace DialogEditor.Tests.Helpers;

/// <summary>
/// Migration ratchet for NoHardcodedUiStringsInCodeTests. The guard went in long after
/// the violations did, so it starts from a baseline of the sites that already existed.
///
/// This is NOT the permanent allowlist shape that was rejected when the guard was
/// designed: [NotLocalised("reason")] remains the way a literal is legitimately exempt,
/// documented at the site. The baseline is temporary and one-directional — it may only
/// shrink, which is why a stale entry is as much a failure as a new offender.
///
/// Entries are file|text with no line number: line numbers churn on every unrelated
/// edit above them, which would make the baseline unmergeable.
/// </summary>
public static class OffenderBaseline
{
    public static string Key(LiteralOffender offender) => $"{offender.File}|{offender.Text}";

    public static IReadOnlyList<string> Parse(string content) =>
        content.Split((char)10)
               // Strip the CR of a CRLF only. A full Trim() would eat the trailing
               // space of concatenation fragments like "… LessThan, ", permanently
               // unmatching those entries so they read as new offenders forever.
               .Select(l => l.TrimEnd((char)13))
               .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
               .ToList();

    public static (IReadOnlyList<string> Added, IReadOnlyList<string> Stale) Compare(
        IEnumerable<string> baseline, IEnumerable<LiteralOffender> offenders)
    {
        var known   = baseline.ToHashSet(StringComparer.Ordinal);
        var current = offenders.Select(Key).ToHashSet(StringComparer.Ordinal);

        var added = current.Where(k => !known.Contains(k)).Order(StringComparer.Ordinal).ToList();
        var stale = known.Where(k => !current.Contains(k)).Order(StringComparer.Ordinal).ToList();
        return (added, stale);
    }
}
