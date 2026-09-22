namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Lossless prefilter for the writer-vs-base-game near-duplicate pass (issue #14).
/// Returns a SUPERSET of the strings whose DuplicateLineScanner.Ratio against the query
/// reaches the threshold, so the caller runs Levenshtein only on those.
///
/// Why it cannot miss a match (the q-gram lemma, q = 3): a string of length M has M − 2
/// character 3-grams, and one edit destroys at most 3 of them. Two strings within edit
/// distance k therefore share at least M − 2 − 3k grams, counted as a MULTISET. Ratio ≥ t is
/// exactly editDistance ≤ (1 − t)·M, which fixes k. Counting distinct grams instead would
/// break the bound — hence the per-posting counts.
///
/// Every string in the length window is examined, not only those the posting walk reached:
/// when the bound drops to ≤ 0 (short lines, low threshold) a string sharing no gram at all
/// must still be returned.
///
/// Not thread-safe: Query reuses one counting buffer. The scanner builds one index per scan.
/// Spec: docs/superpowers/specs/2026-09-22-cross-vanilla-duplicate-detection-design.md
/// </summary>
internal sealed class QGramIndex
{
    private const int Q = 3;

    // Float slack: (1 − 0.85) · 100 is 14.999999999999998 in IEEE doubles. Nudging every
    // bound in the permissive direction keeps the filter a superset; the caller's Ratio
    // check makes the final call either way.
    private const double Epsilon = 1e-9;

    private readonly int[] _lengths;          // by id
    private readonly int[] _idsByLength;      // ids sorted by ascending length
    private readonly int[] _sortedLengths;    // _lengths[_idsByLength[i]]
    private readonly Dictionary<long, List<(int Id, int Count)>> _postings = new();
    private readonly int[] _shared;           // reusable per-query counting buffer

    public QGramIndex(IReadOnlyList<string> strings)
    {
        _lengths       = strings.Select(s => s.Length).ToArray();
        _idsByLength   = Enumerable.Range(0, strings.Count).OrderBy(i => _lengths[i]).ToArray();
        _sortedLengths = _idsByLength.Select(i => _lengths[i]).ToArray();
        _shared        = new int[strings.Count];

        for (var id = 0; id < strings.Count; id++)
            foreach (var (gram, count) in Grams(strings[id]))
            {
                if (!_postings.TryGetValue(gram, out var list)) _postings[gram] = list = [];
                list.Add((id, count));
            }
    }

    public IEnumerable<int> Query(string normalized, double threshold)
    {
        var la = normalized.Length;
        // Ratio ≤ min/max (the edit distance is at least the length difference), so only
        // lengths in [t·La, La/t] can reach the bar — the scanner's existing blocking rule.
        var lo = (int)Math.Ceiling(threshold * la - Epsilon);
        var hi = (int)Math.Floor(la / threshold + Epsilon);

        Array.Clear(_shared);
        foreach (var (gram, countA) in Grams(normalized))
        {
            if (!_postings.TryGetValue(gram, out var list)) continue;
            foreach (var (id, countB) in list)
            {
                var lb = _lengths[id];
                if (lb < lo || lb > hi) continue;   // cheap: skip what the window drops anyway
                _shared[id] += Math.Min(countA, countB);
            }
        }

        var results = new List<int>();
        for (var i = LowerBound(_sortedLengths, lo); i < _sortedLengths.Length && _sortedLengths[i] <= hi; i++)
        {
            var id   = _idsByLength[i];
            var m    = Math.Max(la, _sortedLengths[i]);
            var k    = (int)Math.Floor((1 - threshold) * m + Epsilon);
            var need = m - (Q - 1) - Q * k;
            if (_shared[id] >= need) results.Add(id);
        }
        return results;
    }

    /// Multiset of 3-grams, each packed into a long (3 × 16-bit chars) to avoid allocating
    /// a substring per gram across a ~50k-line corpus.
    private static Dictionary<long, int> Grams(string s)
    {
        var grams = new Dictionary<long, int>();
        for (var i = 0; i + Q <= s.Length; i++)
        {
            var key = ((long)s[i] << 32) | ((long)s[i + 1] << 16) | s[i + 2];
            grams[key] = grams.GetValueOrDefault(key) + 1;
        }
        return grams;
    }

    private static int LowerBound(int[] sorted, int value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] < value) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}
