using System.Text.RegularExpressions;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// One located line. Text is the original (trimmed) writer text, for display.
public record LineRef(string ConversationName, int NodeId, string Text);

/// A set of nodes whose normalized text is identical. Key is that normalized
/// text (the ignore key); SampleText is a representative original line.
public record ExactDuplicateGroup(string Key, string SampleText, IReadOnlyList<LineRef> Members);

/// Two nodes whose normalized texts are similar (below exact, ≥ threshold).
/// Key is the two normalized texts sorted (the ignore key).
public record NearDuplicatePair(IReadOnlyList<string> Key, LineRef A, LineRef B, int SimilarityPercent);

public record DuplicateLineReport(
    IReadOnlyList<ExactDuplicateGroup> Exact,
    IReadOnlyList<NearDuplicatePair>   Near);

/// <summary>
/// Finds exact and near-duplicate lines among the writer's own edited/added text
/// (Default text in patch.Translations[primary], same source as ProjectTextTagScanner).
/// Pure and IO-free. Entries on project.IgnoredDuplicates are filtered out.
/// Spec: docs/superpowers/specs/2026-07-13-duplicate-line-detection-design.md
/// </summary>
public static class DuplicateLineScanner
{
    private const double NearThreshold = 0.85;
    private const int    MinWords      = 4;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static DuplicateLineReport Scan(DialogProject project, string primaryLanguage)
    {
        // 1. Collect one candidate per node: Default text from the primary-language
        //    translations, with a defensive AddedNodes fallback (legacy patches).
        var byNode = new Dictionary<(string Conv, int Node), (LineRef Ref, string Norm)>();

        foreach (var (conv, patch) in project.Patches)
        {
            if (patch.IsEmpty) continue;

            var entries = patch.Translations.GetValueOrDefault(primaryLanguage);
            if (entries is not null)
                foreach (var t in entries)
                    AddCandidate(conv, t.NodeId, t.DefaultText);

            foreach (var n in patch.AddedNodes)   // translations win; this only fills gaps
                AddCandidate(conv, n.NodeId, n.DefaultText);
        }

        var candidates = byNode.Values.ToList();

        // 2. Ignore sets.
        var ignored     = project.IgnoredDuplicates ?? [];
        var ignoredExact = new HashSet<string>(
            ignored.Where(e => e.Kind == DuplicateKind.Exact).Select(e => e.Keys[0]));
        var ignoredNear = new HashSet<string>(
            ignored.Where(e => e.Kind == DuplicateKind.Near).Select(e => NearKey(e.Keys[0], e.Keys[1])));

        // 3. Exact: group by normalized text; ≥2 members is a cluster.
        var exact      = new List<ExactDuplicateGroup>();
        var exactNorms = new HashSet<string>();
        foreach (var g in candidates.GroupBy(c => c.Norm).Where(g => g.Count() >= 2))
        {
            exactNorms.Add(g.Key);
            if (ignoredExact.Contains(g.Key)) continue;
            var members = g.Select(c => c.Ref)
                .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
                .ThenBy(r => r.NodeId)
                .ToList();
            exact.Add(new ExactDuplicateGroup(g.Key, members[0].Text, members));
        }

        // 4. Near: unique-normalized candidates, length-blocked pairwise.
        var nearCandidates = candidates
            .Where(c => !exactNorms.Contains(c.Norm))
            .OrderBy(c => c.Norm.Length)
            .ToList();

        var near = new List<NearDuplicatePair>();
        for (var i = 0; i < nearCandidates.Count; i++)
        {
            var a = nearCandidates[i];
            for (var j = i + 1; j < nearCandidates.Count; j++)
            {
                var b = nearCandidates[j];
                // Sorted ascending by length: once b is too long to possibly reach
                // the threshold, no later j can either — stop.
                if (b.Norm.Length > a.Norm.Length / NearThreshold) break;

                var ratio = Ratio(a.Norm, b.Norm);
                if (ratio < NearThreshold) continue;

                var key = new[] { a.Norm, b.Norm }.OrderBy(s => s, StringComparer.Ordinal).ToList();
                if (ignoredNear.Contains(NearKey(key[0], key[1]))) continue;

                near.Add(new NearDuplicatePair(key, a.Ref, b.Ref, (int)Math.Round(ratio * 100)));
            }
        }

        return new DuplicateLineReport(exact, near);

        void AddCandidate(string conv, int nodeId, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (byNode.ContainsKey((conv, nodeId))) return;   // already have this node's text
            var norm = Normalize(text);
            if (WordCount(norm) < MinWords) return;
            byNode[(conv, nodeId)] = (new LineRef(conv, nodeId, text.Trim()), norm);
        }
    }

    private static string Normalize(string s) => Whitespace.Replace(s.Trim(), " ").ToLowerInvariant();

    private static int WordCount(string normalized) =>
        normalized.Length == 0 ? 0 : normalized.Split(' ').Length;

    private static string NearKey(string a, string b) => a + " " + b;   // a,b already sorted

    private static double Ratio(string a, string b)
    {
        var max = Math.Max(a.Length, b.Length);
        if (max == 0) return 1.0;
        return 1.0 - (double)Levenshtein(a, b) / max;
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur  = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
