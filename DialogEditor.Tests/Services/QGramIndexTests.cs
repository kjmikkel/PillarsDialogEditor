using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class QGramIndexTests
{
    private static readonly double[] Presets = [0.75, 0.80, 0.85, 0.90, 0.95];

    private static readonly string[] Words =
    [
        "the", "wind", "howls", "through", "rigging", "tonight", "captain", "we", "sail",
        "at", "dawn", "gold", "is", "not", "enough", "my", "friend", "a", "storm", "comes",
        "watch", "your", "tongue", "or", "lose", "it", "sea", "hungry", "ha",
    ];

    private static string RandomLine(Random rng) =>
        string.Join(' ', Enumerable.Range(0, rng.Next(4, 18)).Select(_ => Words[rng.Next(Words.Length)]));

    /// One random edit: substitute, insert, delete, or transpose a character.
    private static string Perturb(string s, Random rng)
    {
        var chars = s.ToList();
        var i = rng.Next(chars.Count);
        switch (rng.Next(4))
        {
            case 0: chars[i] = (char)('a' + rng.Next(26)); break;
            case 1: chars.Insert(i, (char)('a' + rng.Next(26))); break;
            case 2: if (chars.Count > 1) chars.RemoveAt(i); break;
            default: if (i + 1 < chars.Count) (chars[i], chars[i + 1]) = (chars[i + 1], chars[i]); break;
        }
        return new string(chars.ToArray());
    }

    [Fact] // The whole point: the filter never drops a pair brute force would accept.
    public void Query_NeverMissesAPairBruteForceAccepts()
    {
        // Sized to keep the brute-force oracle (every query × every line × Levenshtein)
        // around a second: 40 × 9 = 360 lines, 120 queries, 5 presets.
        var rng = new Random(1234);
        var corpus = new List<string>();
        for (var n = 0; n < 40; n++)
        {
            var line = RandomLine(rng);
            corpus.Add(line);
            // Perturbed copies at increasing edit counts straddle every preset.
            var copy = line;
            for (var e = 0; e < 8; e++) { copy = Perturb(copy, rng); corpus.Add(copy); }
        }
        var index = new QGramIndex(corpus);

        var checkedPairs = 0;
        foreach (var t in Presets)
        foreach (var query in corpus.Where((_, i) => i % 3 == 0))
        {
            var found = index.Query(query, t).ToHashSet();
            for (var i = 0; i < corpus.Count; i++)
            {
                if (DuplicateLineScanner.Ratio(query, corpus[i]) < t) continue;
                checkedPairs++;
                Assert.True(found.Contains(i), $"missed at t={t}: «{query}» vs «{corpus[i]}»");
            }
        }
        // Guard against a vacuous pass: the fixture must actually produce accepted pairs.
        Assert.True(checkedPairs > 500, $"only {checkedPairs} accepted pairs");
    }

    [Fact] // The filter must do real work, or the scan is the O(n·m) sweep it replaces.
    public void Query_PrunesUnrelatedLineOfSimilarLength()
    {
        var index = new QGramIndex(["watch your tongue or lose it, captain of the sea"]);
        var hits = index.Query("gold is not enough for my hungry friend at dawn!", 0.85);
        Assert.Empty(hits);
    }

    [Fact] // Repeated grams: a distinct-gram count would under-count here and break the bound.
    public void Query_RepeatedGrams_StillFindsNearMatch()
    {
        const string a = "ha ha ha ha ha ha ha ha ha ha";
        const string b = "ha ha ha ha ha ha ha ha ha ho";
        Assert.True(DuplicateLineScanner.Ratio(a, b) >= 0.95);

        var index = new QGramIndex([b]);
        Assert.Contains(0, index.Query(a, 0.95));
    }

    [Fact] // Short lines at a low bar: the count bound is <= 0, so length alone decides.
    public void Query_ShortLinesAtLowThreshold_ReturnedOnLengthAlone()
    {
        // At M = 8, t = 0.75: k = floor(0.25 · 8) = 2, bound = 8 − 2 − 3·2 = 0. The pair
        // shares no gram at all, so only iterating the length window can return it.
        const string a = "abcd efg";   // 8 chars
        const string b = "wxyz uvq";   // 8 chars, no shared gram
        var index = new QGramIndex([b]);
        Assert.Contains(0, index.Query(a, 0.75));
    }

    [Fact] // Length window: a far longer line can never reach the bar and is not returned.
    public void Query_ExcludesLinesOutsideLengthWindow()
    {
        const string a = "the wind howls";
        var index = new QGramIndex([a + " through the rigging tonight, captain, and every night after"]);
        Assert.Empty(index.Query(a, 0.75));
    }
}
