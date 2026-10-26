namespace DialogEditor.ViewModels.Services;

/// Moves already-found misspelling spans along with a text edit (GitHub issue 312), so the
/// squiggles elsewhere in the box stay put while the writer types and only the edited word
/// loses its squiggle until the delayed full re-check runs. Pure: no UI dependencies.
///
/// The edit is modelled as one replaced range (common prefix + common suffix of the two
/// strings). A span touching that range is dropped; spans before/after it are kept or
/// shifted, but only if the same word still stands there with non-word neighbours (so a
/// letter typed right against a word drops it - it is a different word now).
public static class SpellSpanShift
{
    public static IReadOnlyList<SpellSpan> Apply(IReadOnlyList<SpellSpan> spans, string before, string after)
    {
        if (before == after) return spans;

        var max = Math.Min(before.Length, after.Length);
        var p = 0;
        while (p < max && before[p] == after[p]) p++;
        var s = 0;
        while (s < max - p && before[before.Length - 1 - s] == after[after.Length - 1 - s]) s++;

        var oldEditEnd = before.Length - s;
        var delta = after.Length - before.Length;

        var result = new List<SpellSpan>();
        foreach (var span in spans)
        {
            var end = span.Start + span.Length;
            int newStart;
            if (end <= p) newStart = span.Start;
            else if (span.Start >= oldEditEnd) newStart = span.Start + delta;
            else continue;      // overlaps the edited range

            var newEnd = newStart + span.Length;
            if (span.Start < 0 || span.Length <= 0 || end > before.Length) continue;
            if (newStart < 0 || newEnd > after.Length) continue;
            if (string.CompareOrdinal(after, newStart, before, span.Start, span.Length) != 0) continue;
            if (newStart > 0 && IsWordChar(after[newStart - 1])) continue;
            if (newEnd < after.Length && IsWordChar(after[newEnd])) continue;
            result.Add(new SpellSpan(newStart, span.Length));
        }
        return result;
    }

    // Matches SpellCheckService's token regex [\w'’-].
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '\'' || c == '’' || c == '-';
}
