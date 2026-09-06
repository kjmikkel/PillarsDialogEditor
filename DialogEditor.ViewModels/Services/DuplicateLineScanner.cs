using System.Text.RegularExpressions;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// One located line. Text is the original (trimmed) writer text, for display.
/// Language follows the TextTagIssueRow convention: "" means the primary language,
/// anything else is the real language code. IsFemale marks the female variant.
/// Both default so existing construction sites stay valid.
public record LineRef(
    string ConversationName, int NodeId, string Text,
    string Language = "", bool IsFemale = false);

/// What the duplicate sweep should look at. Bundled into a record rather than threaded
/// as three loose values because the consuming ViewModel constructor is already long.
/// Both scope flags default OFF, so the historical report is what you get unasked.
public record DuplicateScanOptions(
    double NearThreshold         = DuplicateLineScanner.DefaultNearThreshold,
    bool   IncludeFemaleText     = false,
    bool   IncludeOtherLanguages = false);

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
/// (patch.Translations, the same source as ProjectTextTagScanner). Scope is the primary
/// language's Default text unless widened via DuplicateScanOptions (issue #14).
/// Pure and IO-free. Entries on project.IgnoredDuplicates are filtered out.
///
/// Three rules keep the widened scope usable rather than noisy:
///   1. A node is never paired with ITSELF across fields. A female variant differing by
///      a pronoun is what the data is supposed to look like, not a defect.
///   2. Candidates are compared only within one language — Levenshtein across languages
///      is noise.
///   3. Ignore keys carry no language or gender (see IgnoredDuplicate), so an ignore
///      silences a byte-identical line in EVERY language. Deliberate: it keeps stored
///      ignore lists valid, and two languages sharing a 4+-word line is itself suspect.
/// Spec: docs/superpowers/specs/2026-07-13-duplicate-line-detection-design.md
/// </summary>
public static class DuplicateLineScanner
{
    /// The historical bar, and the default when no configured value is supplied.
    public const double DefaultNearThreshold = 0.85;

    // The configured threshold is clamped to this range. The lower bound is not taste:
    // the length-blocking break below divides by the threshold, so a value at or near
    // zero makes the divisor huge (or infinite) and degrades this scan to a full O(n^2)
    // Levenshtein sweep. The upper bound keeps a "100%" setting from silently meaning
    // "exact only", which the Exact tier already covers.
    private const double MinNearThreshold = 0.50;
    private const double MaxNearThreshold = 0.99;

    private const int MinWords = 4;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static DuplicateLineReport Scan(
        DialogProject project, string primaryLanguage, DuplicateScanOptions? options = null)
    {
        var opts = options ?? new DuplicateScanOptions();

        // settings.json is hand-editable, so an out-of-range (or NaN) threshold is reachable
        // without going through the UI's fixed preset list. NaN fails every comparison, so
        // test for it explicitly rather than relying on Math.Clamp.
        var threshold = double.IsNaN(opts.NearThreshold)
            ? DefaultNearThreshold
            : Math.Clamp(opts.NearThreshold, MinNearThreshold, MaxNearThreshold);

        // 1. Collect candidates. The key carries language and field as well as the node
        //    (rule 3 of the class remarks): keyed on (Conv, Node) alone, the first text
        //    per node would win and every widened candidate would vanish silently.
        //    Loop shape follows ProjectTextTagScanner: a display LABEL ("" for primary)
        //    kept separate from the real language code.
        var byKey = new Dictionary<(string Conv, int Node, string Lang, bool Female),
                                   (LineRef Ref, string Norm)>();

        foreach (var (conv, patch) in project.Patches)
        {
            if (patch.IsEmpty) continue;

            foreach (var (lang, entries) in patch.Translations)
            {
                var isPrimary = string.Equals(lang, primaryLanguage, StringComparison.OrdinalIgnoreCase);
                if (!isPrimary && !opts.IncludeOtherLanguages) continue;

                var label = isPrimary ? "" : lang;
                foreach (var t in entries)
                {
                    AddCandidate(conv, t.NodeId, label, female: false, t.DefaultText);
                    if (opts.IncludeFemaleText)
                        AddCandidate(conv, t.NodeId, label, female: true, t.FemaleText);
                }
            }

            // Defensive only: DiffEngine blanks added-node text (it belongs in Translations)
            // and both fields are [JsonIgnore], so this is empty for any current patch.
            // Text here is implicitly primary-language, as ProjectTextTagScanner assumes.
            foreach (var n in patch.AddedNodes)   // translations win; this only fills gaps
            {
                AddCandidate(conv, n.NodeId, "", female: false, n.DefaultText);
                if (opts.IncludeFemaleText)
                    AddCandidate(conv, n.NodeId, "", female: true, n.FemaleText);
            }
        }

        var candidates = byKey.Values.ToList();

        // 2. Ignore sets.
        var ignored     = project.IgnoredDuplicates ?? [];
        var ignoredExact = new HashSet<string>(
            ignored.Where(e => e.Kind == DuplicateKind.Exact).Select(e => e.Keys[0]));
        var ignoredNear = new HashSet<string>(
            ignored.Where(e => e.Kind == DuplicateKind.Near).Select(e => NearKey(e.Keys[0], e.Keys[1])));

        // 3. Exact: group by (language, normalized text) — rule 2. A group counts only if
        //    it spans two DISTINCT nodes (rule 1); a node matching its own female text is
        //    the ordinary shape of gendered writing, not a finding. The group's ignore Key
        //    stays the bare normalized text, hence the cross-language reach noted above.
        var exact      = new List<ExactDuplicateGroup>();
        var exactNorms = new HashSet<(string Lang, string Norm)>();
        foreach (var g in candidates
                     .GroupBy(c => (Lang: c.Ref.Language, c.Norm))
                     .Where(g => g.Select(c => (c.Ref.ConversationName, c.Ref.NodeId))
                                  .Distinct().Count() >= 2))
        {
            exactNorms.Add(g.Key);
            if (ignoredExact.Contains(g.Key.Norm)) continue;
            var members = g.Select(c => c.Ref)
                .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
                .ThenBy(r => r.NodeId)
                .ThenBy(r => r.IsFemale)
                .ToList();
            exact.Add(new ExactDuplicateGroup(g.Key.Norm, members[0].Text, members));
        }

        // 4. Near: per-language buckets (rule 2), each length-blocked pairwise. Bucketing
        //    rather than filtering keeps the length-sorted break exact — it is what stops
        //    this being a full O(n^2) Levenshtein sweep.
        var near = new List<NearDuplicatePair>();
        foreach (var bucket in candidates
                     .Where(c => !exactNorms.Contains((c.Ref.Language, c.Norm)))
                     .GroupBy(c => c.Ref.Language))
        {
            var nearCandidates = bucket.OrderBy(c => c.Norm.Length).ToList();
            for (var i = 0; i < nearCandidates.Count; i++)
            {
                var a = nearCandidates[i];
                for (var j = i + 1; j < nearCandidates.Count; j++)
                {
                    var b = nearCandidates[j];
                    // Sorted ascending by length: once b is too long to possibly reach
                    // the threshold, no later j can either — stop.
                    if (b.Norm.Length > a.Norm.Length / threshold) break;

                    // Rule 1: the same node's own Default and Female text are a variant
                    // pair by design. Never report a node against itself.
                    if (a.Ref.ConversationName == b.Ref.ConversationName &&
                        a.Ref.NodeId == b.Ref.NodeId) continue;

                    var ratio = Ratio(a.Norm, b.Norm);
                    if (ratio < threshold) continue;

                    var key = new[] { a.Norm, b.Norm }.OrderBy(s => s, StringComparer.Ordinal).ToList();
                    if (ignoredNear.Contains(NearKey(key[0], key[1]))) continue;

                    near.Add(new NearDuplicatePair(key, a.Ref, b.Ref, (int)Math.Round(ratio * 100)));
                }
            }
        }

        return new DuplicateLineReport(exact, near);

        void AddCandidate(string conv, int nodeId, string lang, bool female, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var key = (conv, nodeId, lang, female);
            if (byKey.ContainsKey(key)) return;   // already have this node/language/field
            var norm = Normalize(text);
            if (WordCount(norm) < MinWords) return;
            byKey[key] = (new LineRef(conv, nodeId, text.Trim(), lang, female), norm);
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
