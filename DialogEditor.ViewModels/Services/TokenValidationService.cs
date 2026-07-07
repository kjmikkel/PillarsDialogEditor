using System.Text.RegularExpressions;

namespace DialogEditor.ViewModels.Services;

public enum TokenIssueKind { UnknownToken, UnbalancedMarkup }

/// A single validation finding. Fragment is the offending literal (e.g.
/// "[Player Nmae]"); Suggestion is a nearest-known token when one is close,
/// else null; Position is the fragment's start offset in the validated text.
public sealed record TokenIssue(
    TokenIssueKind Kind, string Fragment, string? Suggestion, int Position);

/// Pure validation of dialog text against the engine-verified tag vocabulary.
/// Flags likely token typos and (Task 3) unbalanced markup while staying silent
/// on free-text bracket conventions. No UI, never throws.
/// Spec: docs/superpowers/specs/2026-07-07-token-validation-design.md
public sealed class TokenValidationService
{
    private readonly TagCatalogue _catalogue;

    public TokenValidationService(TagCatalogue? catalogue = null)
        => _catalogue = catalogue ?? TagCatalogue.Instance;

    private static readonly Regex BracketSpan =
        new(@"\[[^\[\]\n\r]*\]", RegexOptions.Compiled);

    public IReadOnlyList<TokenIssue> Validate(string text, string gameId)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var issues = new List<TokenIssue>();
        ValidateTokens(text, gameId, issues);
        ValidateMarkup(text, gameId, issues);
        return issues;
    }

    // ── Unknown-token detection ──────────────────────────────────────────
    private void ValidateTokens(string text, string gameId, List<TokenIssue> issues)
    {
        var tokens = TokensFor(gameId);
        foreach (Match m in BracketSpan.Matches(text))
        {
            var span = m.Value; // includes the surrounding brackets
            if (IsKnownToken(span, tokens, gameId)) continue;

            var suggestion = NearestSuggestion(span, tokens);
            if (suggestion is not null)
                issues.Add(new TokenIssue(
                    TokenIssueKind.UnknownToken, span, suggestion, m.Index));
            // else: assumed a free-text convention → silent
        }
    }

    // ── Unbalanced-markup detection ──────────────────────────────────────
    // Matches an opening tag "<name" or "<name ...", a closing tag "</name>",
    // capturing the tag name. Attribute content is deliberately not parsed.
    private static readonly Regex TagToken =
        new(@"<(?<close>/?)(?<name>[a-zA-Z]+)[^>]*?>", RegexOptions.Compiled);

    // The paired markup tag names we balance-check. Self-closing "sprite" is
    // intentionally excluded. Only known markup names are checked, so unknown
    // tags (e.g. "<b>") are ignored (leniency).
    private static readonly HashSet<string> PairedMarkup =
        new(System.StringComparer.OrdinalIgnoreCase)
        { "i", "ispeech", "xg", "color", "link" };

    private void ValidateMarkup(string text, string gameId, List<TokenIssue> issues)
    {
        var stack = new Stack<(string Name, int Pos, string Frag)>();

        foreach (Match m in TagToken.Matches(text))
        {
            var name = m.Groups["name"].Value;
            if (!PairedMarkup.Contains(name)) continue;   // unknown/self-closing → ignore
            var isClose = m.Groups["close"].Value == "/";

            if (!isClose)
            {
                stack.Push((name, m.Index, $"<{name}>"));
            }
            else if (stack.Count > 0 &&
                     string.Equals(stack.Peek().Name, name, System.StringComparison.OrdinalIgnoreCase))
            {
                stack.Pop();
            }
            else
            {
                issues.Add(new TokenIssue(
                    TokenIssueKind.UnbalancedMarkup, $"</{name}>", null, m.Index));
            }
        }

        while (stack.Count > 0)
        {
            var open = stack.Pop();
            issues.Add(new TokenIssue(
                TokenIssueKind.UnbalancedMarkup, open.Frag, null, open.Pos));
        }
    }

    private IReadOnlyList<TagEntry> TokensFor(string gameId)
    {
        var union = string.IsNullOrEmpty(gameId);
        return _catalogue.All
            .Where(e => e.Kind == "Token")
            .Where(e => union || e.Games.Any(g =>
                string.Equals(g, gameId, System.StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    // A parameterised token carries a numeric insert marker "${0}"; its literal
    // instances are "<prefix><digits><suffix>", e.g. "[Specified 3]".
    private static bool IsParameterised(TagEntry e)
        => e.Insert is not null && e.Insert.Contains("${0}");

    private static Regex ParamRegex(TagEntry e)
    {
        // Build "^<escaped-prefix>\d+<escaped-suffix>$" from the insert literal.
        // Split on the numeric marker and escape each part separately — escaping the
        // whole string first would turn the marker's own characters into literals.
        var parts = e.Insert!.Split("${0}");
        var pattern = "^" + string.Join(@"\d+", parts.Select(Regex.Escape)) + "$";
        return new Regex(pattern);
    }

    private static bool IsKnownToken(
        string span, IReadOnlyList<TagEntry> tokens, string gameId)
    {
        var isPoe2 = string.Equals(gameId, "poe2", System.StringComparison.OrdinalIgnoreCase)
                     || string.IsNullOrEmpty(gameId);

        foreach (var e in tokens)
        {
            if (IsParameterised(e))
            {
                if (ParamRegex(e).IsMatch(span)) return true;
                continue;
            }
            if (string.Equals(span, e.Name, System.StringComparison.Ordinal))
                return true;
            if (e.Lowercase && isPoe2 &&
                string.Equals(span, e.Name.ToLowerInvariant(), System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // Fuzzy: normalize a trailing digit run to "n" so a parameterised typo
    // ("[Specfied 0]") compares against its family ("[Specified n]"). Returns the
    // nearest token Name when within a conservative, length-scaled threshold.
    private static string? NearestSuggestion(
        string span, IReadOnlyList<TagEntry> tokens)
    {
        var probe = NormalizeDigits(span);
        string? best = null;
        var     bestDist = int.MaxValue;

        foreach (var e in tokens)
        {
            var candidate = NormalizeDigits(e.Name);
            var dist = DamerauLevenshtein(
                probe.ToLowerInvariant(), candidate.ToLowerInvariant());
            // dist == 0 means a case-only mismatch (the span isn't exact-known, or
            // we would not be here) — suggest the correctly-cased token.
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = e.Name;
            }
        }

        var inner     = span.Length >= 2 ? span[1..^1] : span;
        var threshold = inner.Length <= 6 ? 1 : 2;
        return bestDist <= threshold ? best : null;
    }

    private static readonly Regex TrailingDigits = new(@"\d+(?=\]$)", RegexOptions.Compiled);
    private static string NormalizeDigits(string s) => TrailingDigits.Replace(s, "n");

    private static int DamerauLevenshtein(string a, string b)
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
            d[i, j] = System.Math.Min(System.Math.Min(
                d[i - 1, j] + 1,
                d[i, j - 1] + 1),
                d[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                d[i, j] = System.Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }
        return d[n, m];
    }
}
