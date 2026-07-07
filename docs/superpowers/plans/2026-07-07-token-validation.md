# Token & Markup Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Flag likely token typos (`[Player Nmae]`) and unbalanced markup (`<i>` with no `</i>`) in node dialog text, while staying silent on legitimate free-text bracket conventions (`[Say nothing.]`, `[Vailian]`).

**Architecture:** A pure `TokenValidationService` in `DialogEditor.ViewModels` checks text against the engine-verified `TagCatalogue`. Results surface two ways: a live warning box in the node detail panel (Default/Female text, mirroring the existing `BarkWarnings`) and a separate token-issues section in the Flow Analytics window (the open conversation's Default/Female + every translation language). Core's `FlowAnalysisService` is untouched — it cannot reference `TagCatalogue`, so token validation runs entirely at the ViewModels layer.

**Tech Stack:** C# / .NET, Avalonia UI, xUnit, CommunityToolkit.Mvvm. Vocabulary in embedded `tags.json` via `TagCatalogue`.

## Global Constraints

- **TDD, red first.** Every behaviour gets a failing test before implementation. Tests live in `DialogEditor.Tests` mirroring `DialogEditor.Core`/`DialogEditor.ViewModels` structure.
- **Localisation.** No user-visible string hardcoded in C#/XAML. UI strings go in `DialogEditor.Avalonia/Resources/Strings.axaml` and are read via `{DynamicResource}` (XAML) or `Loc.Get`/`Loc.Format` (C#).
- **Tooltips.** Every new interactive/informational control carries `ToolTip.Tip` (+ mirrored `AutomationProperties.HelpText` where the existing pattern does).
- **Colour tokens.** No hex literals outside `Palette*.axaml`; new brushes are semantic `Brush.*` tokens in `Tokens.axaml`. `NoStrayHexTests` must stay green.
- **Error handling.** No bare `catch`; caught exceptions logged via `AppLog.Error`/`AppLog.Warn` (except `OperationCanceledException`). The service itself is pure and non-throwing.
- **Tests run serially** (`DialogEditor.Tests` has parallelization disabled — do not re-enable).
- **Game ids** are the lowercase strings `"poe1"` / `"poe2"`; an empty string means "no game folder open" → validate against the union of both games (never applies PoE2-only lowercase variants).
- **Two copies of `tags.json`** must stay byte-identical: the embedded `DialogEditor.ViewModels/Resources/tags.json` and the human-readable `data/tags.json`. Edit both.
- **Build/test command:** `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`. Filter a single test with `--filter "FullyQualifiedName~<name>"`.

---

### Task 1: Data — `Lowercase` flag on tokens with a valid lowercase form

**Files:**
- Modify: `DialogEditor.ViewModels/Services/TagCatalogue.cs` (the `TagEntry` record)
- Modify: `DialogEditor.ViewModels/Resources/tags.json`
- Modify: `data/tags.json`
- Test: `DialogEditor.Tests/Services/TagLowercaseFlagTests.cs` (create)

**Interfaces:**
- Produces: `TagEntry.Lowercase` (`bool`, default `false`) — true on the five Player entries whose all-lowercase form the PoE2 engine also substitutes.

- [ ] **Step 1: Write the failing test**

Create `DialogEditor.Tests/Services/TagLowercaseFlagTests.cs`:

```csharp
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class TagLowercaseFlagTests
{
    // Every entry whose notes advertise a lowercase form must carry Lowercase=true,
    // so the prose note and the machine-readable flag cannot drift apart.
    [Fact]
    public void EntriesWhoseNotesMentionLowercase_CarryTheFlag()
    {
        foreach (var entry in TagCatalogue.Instance.All)
        {
            var notesMentionLowercase =
                entry.Notes is not null &&
                entry.Notes.Contains("lowercase form", System.StringComparison.OrdinalIgnoreCase);

            if (notesMentionLowercase)
                Assert.True(entry.Lowercase,
                    $"'{entry.Name}' notes mention a lowercase form but Lowercase is false.");
        }
    }

    [Fact]
    public void PlayerRace_HasLowercaseFlag()
    {
        var race = System.Linq.Enumerable.First(
            TagCatalogue.Instance.All, e => e.Name == "[Player Race]");
        Assert.True(race.Lowercase);
    }

    [Fact]
    public void PlayerName_DoesNotHaveLowercaseFlag()
    {
        var name = System.Linq.Enumerable.First(
            TagCatalogue.Instance.All, e => e.Name == "[Player Name]");
        Assert.False(name.Lowercase);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~TagLowercaseFlagTests"`
Expected: FAIL — `TagEntry` has no `Lowercase` member (compile error), or the flag is absent.

- [ ] **Step 3: Add the `Lowercase` property to `TagEntry`**

In `DialogEditor.ViewModels/Services/TagCatalogue.cs`, extend the record (append the new optional parameter last so existing positional callers still compile):

```csharp
public record TagEntry(
    string Name,
    string Kind,
    IReadOnlyList<string> Games,
    string Category,
    string Description,
    string? Example = null,
    int Count = 0,
    string? Notes = null,
    string? Insert = null,
    bool Lowercase = false);
```

- [ ] **Step 4: Add `"lowercase": true` to the five Player entries in BOTH `tags.json` files**

In `DialogEditor.ViewModels/Resources/tags.json` AND `data/tags.json`, add `"lowercase": true` to these entries: `[Player Race]`, `[Player Subrace]`, `[Player Class]`, `[Player Culture]`, `[Player Background]`. Example for `[Player Race]`:

```json
  { "name": "[Player Race]", "kind": "Token", "games": ["poe1", "poe2"], "category": "Player",
    "description": "The Watcher's race (e.g. \"elf\").",
    "example": "What is this, [Player Race]-creature?", "count": 91, "lowercase": true,
    "notes": "Deadfire also replaces the lowercase form [player race] with the lower-cased value for mid-sentence use; PoE1 has no lowercase pairs — matching is exact-case." },
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~TagLowercaseFlagTests"`
Expected: PASS (all three facts).

- [ ] **Step 6: Verify the two tags.json copies are still identical**

Run: `git diff --no-index DialogEditor.ViewModels/Resources/tags.json data/tags.json`
Expected: no output (identical).

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.ViewModels/Services/TagCatalogue.cs DialogEditor.ViewModels/Resources/tags.json data/tags.json DialogEditor.Tests/Services/TagLowercaseFlagTests.cs
git commit -m "feat(validation): mark tokens with a valid lowercase form"
```

---

### Task 2: `TokenValidationService` — types + unknown-token detection

**Files:**
- Create: `DialogEditor.ViewModels/Services/TokenValidationService.cs`
- Test: `DialogEditor.Tests/Services/TokenValidationServiceTests.cs` (create)

**Interfaces:**
- Consumes: `TagCatalogue`, `TagEntry` (incl. `Lowercase`, `Insert`) from Task 1.
- Produces:
  - `enum TokenIssueKind { UnknownToken, UnbalancedMarkup }`
  - `record TokenIssue(TokenIssueKind Kind, string Fragment, string? Suggestion, int Position)`
  - `class TokenValidationService` with ctor `(TagCatalogue? catalogue = null)` and method `IReadOnlyList<TokenIssue> Validate(string text, string gameId)`.
  - This task implements unknown-token detection only; markup balance is added in Task 3 (same `Validate` method, additive).

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Services/TokenValidationServiceTests.cs`:

```csharp
using System.Linq;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class TokenValidationServiceTests
{
    private readonly TokenValidationService _svc = new();

    // ── Known tokens pass ────────────────────────────────────────────────
    [Fact]
    public void KnownToken_NoIssues()
        => Assert.Empty(_svc.Validate("Hello [Player Name], welcome.", "poe2"));

    [Fact]
    public void ParameterisedToken_WithDigit_NoIssues()
        => Assert.Empty(_svc.Validate("[Specified 0] nods. [Slot 3] waits.", "poe2"));

    // ── Lowercase variant is game-aware ──────────────────────────────────
    [Fact]
    public void LowercaseVariant_Poe2_NoIssues()
        => Assert.Empty(_svc.Validate("a proud [player race]", "poe2"));

    [Fact]
    public void LowercaseVariant_Poe1_IsFlagged()
    {
        var issues = _svc.Validate("a proud [player race]", "poe1");
        var issue  = Assert.Single(issues);
        Assert.Equal(TokenIssueKind.UnknownToken, issue.Kind);
        Assert.Equal("[Player Race]", issue.Suggestion);
    }

    [Fact]
    public void LowercaseOfNonLowercaseToken_IsFlagged()
    {
        // [Player Name] has no lowercase form even in PoE2.
        var issue = Assert.Single(_svc.Validate("hi [player name]", "poe2"));
        Assert.Equal("[Player Name]", issue.Suggestion);
    }

    // ── Fuzzy "did you mean" ─────────────────────────────────────────────
    [Fact]
    public void MisspelledToken_FlaggedWithSuggestion()
    {
        var issue = Assert.Single(_svc.Validate("Hello [Player Nmae]!", "poe2"));
        Assert.Equal(TokenIssueKind.UnknownToken, issue.Kind);
        Assert.Equal("[Player Name]", issue.Suggestion);
        Assert.Equal("[Player Nmae]", issue.Fragment);
    }

    [Fact]
    public void MisspelledParameterisedToken_FlaggedWithFamilySuggestion()
    {
        var issue = Assert.Single(_svc.Validate("[Specfied 0] arrives.", "poe2"));
        Assert.Equal("[Specified n]", issue.Suggestion);
    }

    // ── Free-text conventions are silent (the false-positive guard) ──────
    [Theory]
    [InlineData("[Say nothing.]")]
    [InlineData("[Draw your weapons and attack.]")]
    [InlineData("[Attack]")]
    [InlineData("[Lie]")]
    [InlineData("[Leave]")]
    [InlineData("[Vailian]")]
    [InlineData("[Pained grunt]")]
    [InlineData("[Diplomacy]")]
    public void FreeTextConvention_NoIssues(string convention)
        => Assert.Empty(_svc.Validate($"Player option: {convention}", "poe2"));

    // ── Position reported ────────────────────────────────────────────────
    [Fact]
    public void UnknownToken_PositionIsFragmentStart()
    {
        var issue = Assert.Single(_svc.Validate("ab [Player Nmae]", "poe2"));
        Assert.Equal(3, issue.Position);
    }

    // ── Empty / whitespace ───────────────────────────────────────────────
    [Fact]
    public void EmptyText_NoIssues() => Assert.Empty(_svc.Validate("", "poe2"));

    [Fact]
    public void NoBrackets_NoIssues()
        => Assert.Empty(_svc.Validate("plain narration with no tags", "poe2"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~TokenValidationServiceTests"`
Expected: FAIL — `TokenValidationService` does not exist (compile error).

- [ ] **Step 3: Implement the service (unknown-token detection)**

Create `DialogEditor.ViewModels/Services/TokenValidationService.cs`:

```csharp
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
        // Task 3 adds: ValidateMarkup(text, gameId, issues);
        return issues;
    }

    // ── Unknown-token detection ──────────────────────────────────────────
    private void ValidateTokens(string text, string gameId, List<TokenIssue> issues)
    {
        var tokens  = TokensFor(gameId);
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
        var literal = e.Insert!.Replace("${0}", " "); // marker placeholder
        var escaped = Regex.Escape(literal).Replace(" ", @"\d+");
        return new Regex("^" + escaped + "$");
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
            if (dist > 0 && dist < bestDist)
            {
                bestDist = dist;
                best     = e.Name;
            }
        }

        var inner    = span.Length >= 2 ? span[1..^1] : span;
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~TokenValidationServiceTests"`
Expected: PASS (all facts/theories).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/TokenValidationService.cs DialogEditor.Tests/Services/TokenValidationServiceTests.cs
git commit -m "feat(validation): unknown-token detection service"
```

---

### Task 3: `TokenValidationService` — unbalanced-markup detection

**Files:**
- Modify: `DialogEditor.ViewModels/Services/TokenValidationService.cs`
- Modify: `DialogEditor.Tests/Services/TokenValidationServiceTests.cs`

**Interfaces:**
- Consumes: the `Validate`/`TokenIssue` shape from Task 2.
- Produces: `TokenIssueKind.UnbalancedMarkup` findings from the same `Validate` call. Fragment is the offending tag literal (e.g. `<i>`); Suggestion is null; Position is the tag's offset.

- [ ] **Step 1: Write the failing tests**

Append to `DialogEditor.Tests/Services/TokenValidationServiceTests.cs` (inside the class):

```csharp
    // ── Markup balance ───────────────────────────────────────────────────
    [Fact]
    public void BalancedItalics_NoIssues()
        => Assert.Empty(_svc.Validate("she said <i>quietly</i> then left", "poe2"));

    [Fact]
    public void UnclosedItalics_Flagged()
    {
        var issue = Assert.Single(_svc.Validate("she said <i>quietly then left", "poe2"));
        Assert.Equal(TokenIssueKind.UnbalancedMarkup, issue.Kind);
        Assert.Equal("<i>", issue.Fragment);
    }

    [Fact]
    public void StrayClosingTag_Flagged()
    {
        var issue = Assert.Single(_svc.Validate("text</i> more", "poe2"));
        Assert.Equal(TokenIssueKind.UnbalancedMarkup, issue.Kind);
        Assert.Equal("</i>", issue.Fragment);
    }

    [Fact]
    public void NestedMarkup_Balanced_NoIssues()
        => Assert.Empty(_svc.Validate("<i><ispeech>voice</ispeech></i>", "poe2"));

    [Fact]
    public void SelfClosingSprite_NeverUnbalanced()
        => Assert.Empty(_svc.Validate("press <sprite=\"Inline\" name=\"Fire\" tint=1>", "poe2"));

    // Leniency: vanilla ships <link> with a missing closing attribute quote.
    // We only balance tag NAMES, never parse attributes, so this must pass.
    [Fact]
    public void MalformedLinkAttribute_NoMarkupIssue()
    {
        var text = "[Vailian] <link=\"neutralvalue://Vailian: hi>\"Perla\"</link>.";
        Assert.DoesNotContain(_svc.Validate(text, "poe2"),
            i => i.Kind == TokenIssueKind.UnbalancedMarkup);
    }

    [Fact]
    public void UnknownTag_NotBalanceChecked()
        => Assert.Empty(_svc.Validate("<b>bold</b> and <foo>", "poe2"));

    // Colour tags balance by name; the attribute value is not parsed.
    [Fact]
    public void BalancedColour_NoIssues()
        => Assert.Empty(_svc.Validate("<color=\"red\">warn</color>", "poe2"));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~TokenValidationServiceTests"`
Expected: FAIL — the new markup tests fail (no markup validation yet); the Task 2 tests still pass.

- [ ] **Step 3: Implement markup balance**

In `TokenValidationService.cs`, enable the markup pass in `Validate`:

```csharp
    public IReadOnlyList<TokenIssue> Validate(string text, string gameId)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var issues = new List<TokenIssue>();
        ValidateTokens(text, gameId, issues);
        ValidateMarkup(text, gameId, issues);
        return issues;
    }
```

Add these members to the class:

```csharp
    // Matches an opening tag "<name" or "<name ...", a closing tag "</name>",
    // capturing the tag name. Attribute content is deliberately not parsed.
    private static readonly Regex TagToken =
        new(@"<(?<close>/?)(?<name>[a-zA-Z]+)(?<self>[^>]*?)>", RegexOptions.Compiled);

    // The paired markup tag names we balance-check. Self-closing "sprite" is
    // intentionally excluded. Derived from the Markup catalogue entries but
    // pinned to names because catalogue Names carry "…"/attribute placeholders.
    private static readonly HashSet<string> PairedMarkup =
        new(System.StringComparer.OrdinalIgnoreCase)
        { "i", "ispeech", "xg", "color", "link" };

    private void ValidateMarkup(string text, string gameId, List<TokenIssue> issues)
    {
        var stack = new Stack<(string Name, int Pos, string Frag)>();

        foreach (Match m in TagToken.Matches(text))
        {
            var name    = m.Groups["name"].Value;
            if (!PairedMarkup.Contains(name)) continue;   // unknown/self-closing → ignore
            var isClose = m.Groups["close"].Value == "/";

            if (!isClose)
            {
                stack.Push((name, m.Index, OpenTagFragment(name)));
            }
            else
            {
                if (stack.Count > 0 &&
                    string.Equals(stack.Peek().Name, name, System.StringComparison.OrdinalIgnoreCase))
                    stack.Pop();
                else
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

    private static string OpenTagFragment(string name) => $"<{name}>";
```

Note on `SelfClosingSprite`: `<sprite=...>` has name `sprite`, which is not in `PairedMarkup`, so it is skipped — correct. The `self` capture group is unused by the balance logic (kept for clarity/future use).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~TokenValidationServiceTests"`
Expected: PASS (Task 2 + Task 3 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/TokenValidationService.cs DialogEditor.Tests/Services/TokenValidationServiceTests.cs
git commit -m "feat(validation): unbalanced-markup detection (tag-name balance, lenient attrs)"
```

---

### Task 4: False-positive regression corpus

**Files:**
- Create: `DialogEditor.Tests/Services/TokenValidationFalsePositiveTests.cs`

**Interfaces:**
- Consumes: `TokenValidationService.Validate` (Tasks 2–3).
- Produces: nothing consumed downstream; a guard that real shipped convention values yield zero issues.

- [ ] **Step 1: Write the failing/guard test**

Create `DialogEditor.Tests/Services/TokenValidationFalsePositiveTests.cs`. This is a representative sample of real shipped free-text bracket conventions and markup; every one must produce zero issues.

```csharp
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class TokenValidationFalsePositiveTests
{
    private readonly TokenValidationService _svc = new();

    public static TheoryData<string> ShippedConventions => new()
    {
        // Stage directions
        "[Say nothing.]", "[Attack]", "[Leave]", "[Lie]", "[Remain silent.]",
        "[Draw your weapons and attack.]", "[Hand over the coins.]", "[Nod.]",
        "[Refuse.]", "[Wait.]", "[Give the letter to him.]", "[Lie] \"I saw nothing.\"",
        // Language markers
        "[Vailian]", "[Huana]", "[Rauataian]", "[Engwithan]", "[Ixamitl]",
        "[Eld Aedyran]", "[Ordhjóma]", "[Lembur]",
        // VO / chatter annotations (PoE1)
        "[Pained grunt]", "[Sighs]", "[Laughs]", "[A low whistle]",
        // Skill & disposition labels (engine-built, never authored — but appear in text)
        "[Diplomacy]", "[Honest]", "[Perception]", "[Might]", "[Bluff]",
    };

    [Theory]
    [MemberData(nameof(ShippedConventions))]
    public void ShippedConvention_ProducesNoIssues_Poe2(string convention)
        => Assert.Empty(_svc.Validate($"Option: {convention}", "poe2"));

    [Theory]
    [MemberData(nameof(ShippedConventions))]
    public void ShippedConvention_ProducesNoIssues_Poe1(string convention)
        => Assert.Empty(_svc.Validate($"Option: {convention}", "poe1"));

    [Fact]
    public void ShippedMalformedLink_ProducesNoIssues()
    {
        // Real shape: neutralvalue attribute missing its closing quote.
        var text = "[Vailian] <link=\"neutralvalue://Vailian: Do you speak Vailian, sir?>" +
                   "\"Perla Vailian, fentre?\"</link>";
        Assert.Empty(_svc.Validate(text, "poe2"));
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~TokenValidationFalsePositiveTests"`
Expected: Ideally PASS. **If any case fails**, it is a genuine false positive — tighten the fuzzy threshold or matching in `TokenValidationService` (do not weaken the test). Common fix: lower the short-token threshold, or require the fuzzy probe length to be within ±2 of the candidate length before accepting a suggestion. Re-run until green.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/Services/TokenValidationFalsePositiveTests.cs
git commit -m "test(validation): shipped-convention false-positive regression guard"
```

---

### Task 5: Detail-panel warnings — `NodeDetailViewModel.TokenWarnings`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/NodeDetailViewModelValidationTests.cs` (create)

**Interfaces:**
- Consumes: `TokenValidationService`, `TokenIssue`, `TokenIssueKind` (Tasks 2–3); `NodeDetailViewModel.ActiveGameId`, `DefaultText`, `FemaleText`.
- Produces: `NodeDetailViewModel.TokenWarnings` (`IReadOnlyList<string>`) — localised messages for the selected node's Default + Female text; raised on selection change and on Default/Female edits.

- [ ] **Step 1: Add localisation strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, add (near other node-detail strings):

```xml
  <system:String x:Key="Validation_UnknownToken_Suggest">Unknown tag "{0}" — did you mean "{1}"?</system:String>
  <system:String x:Key="Validation_UnknownToken">Unknown tag "{0}".</system:String>
  <system:String x:Key="Validation_UnbalancedMarkup">Unbalanced markup "{0}" — check the opening/closing tags match.</system:String>
  <system:String x:Key="ToolTip_TokenWarnings">Possible problems with substitution tokens or markup in this line's text. These are warnings, not errors — free-text stage directions are never flagged.</system:String>
```

(The `system` namespace and `<system:String>` pattern already exist at the top of `Strings.axaml`; follow the surrounding entries exactly.)

- [ ] **Step 2: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/NodeDetailViewModelValidationTests.cs`. Mirror the construction style already used in `NodeDetailViewModelTests.cs` (open that file first to copy how a `NodeDetailViewModel` and its backing node are built in this codebase; the snippet below assumes the same `CreateViewModelWithNode`-style helper — reuse the existing helper rather than inventing one).

```csharp
using System.Linq;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelValidationTests
{
    [Fact]
    public void TokenWarnings_CleanText_IsEmpty()
    {
        var vm = NodeDetailViewModelTestHelpers.WithDefaultText("Hello [Player Name].", gameId: "poe2");
        Assert.Empty(vm.TokenWarnings);
    }

    [Fact]
    public void TokenWarnings_MisspelledToken_ReportsSuggestion()
    {
        var vm = NodeDetailViewModelTestHelpers.WithDefaultText("Hi [Player Nmae]!", gameId: "poe2");
        var msg = Assert.Single(vm.TokenWarnings);
        Assert.Contains("[Player Name]", msg);   // suggestion appears in the localised message
    }

    [Fact]
    public void TokenWarnings_RecomputeOnDefaultTextEdit()
    {
        var vm = NodeDetailViewModelTestHelpers.WithDefaultText("clean text", gameId: "poe2");
        Assert.Empty(vm.TokenWarnings);
        vm.DefaultText = "now with [Player Nmae]";
        Assert.NotEmpty(vm.TokenWarnings);
    }

    [Fact]
    public void TokenWarnings_ValidatesFemaleText()
    {
        var vm = NodeDetailViewModelTestHelpers.WithFemaleText("she says [Player Nmae]", gameId: "poe2");
        Assert.NotEmpty(vm.TokenWarnings);
    }
}
```

**Before writing this file**, open `DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs` and reuse its existing node/VM construction. If no reusable helper exists, add a tiny private helper in *this* test file that builds a `NodeDetailViewModel` bound to a node, sets `ActiveGameId`, and sets Default/Female text — matching how the existing tests do it. Do not fabricate constructor signatures; copy the real ones.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NodeDetailViewModelValidationTests"`
Expected: FAIL — `TokenWarnings` does not exist.

- [ ] **Step 4: Implement `TokenWarnings`**

In `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`:

Add a service field near the other fields:

```csharp
    private readonly TokenValidationService _tokenValidator = new();
```

Add the property (place it next to `BarkWarnings`, ~line 368):

```csharp
    public IReadOnlyList<string> TokenWarnings
    {
        get
        {
            if (_node is null) return [];
            var messages = new List<string>();
            AppendTokenWarnings(_node.DefaultText, messages);
            if (_node.HasFemaleText)
                AppendTokenWarnings(_node.FemaleText, messages);
            return messages;
        }
    }

    private void AppendTokenWarnings(string? text, List<string> into)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var issue in _tokenValidator.Validate(text, ActiveGameId))
        {
            var msg = issue.Kind switch
            {
                TokenIssueKind.UnbalancedMarkup =>
                    Loc.Format("Validation_UnbalancedMarkup", issue.Fragment),
                _ when issue.Suggestion is not null =>
                    Loc.Format("Validation_UnknownToken_Suggest", issue.Fragment, issue.Suggestion),
                _ => Loc.Format("Validation_UnknownToken", issue.Fragment),
            };
            into.Add(msg);
        }
    }
```

Add `using DialogEditor.ViewModels.Services;` if not already present.

Raise change notifications: in `NotifyAllProxies()` add alongside the `BarkWarnings` line (~747):

```csharp
        OnPropertyChanged(nameof(TokenWarnings));
```

And in the `DefaultText` / `FemaleText` setters (~67–76), raise it so warnings update live as the user edits:

```csharp
    public string DefaultText
    {
        get => _node?.DefaultText ?? string.Empty;
        set { if (_node != null) { _node.DefaultText = value; OnPropertyChanged(nameof(TokenWarnings)); } }
    }

    public string FemaleText
    {
        get => _node?.FemaleText ?? string.Empty;
        set { if (_node != null) { _node.FemaleText = value; OnPropertyChanged(nameof(TokenWarnings)); } }
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NodeDetailViewModelValidationTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/NodeDetailViewModelValidationTests.cs
git commit -m "feat(validation): token warnings on the node detail view-model"
```

---

### Task 6: Detail-panel warnings — XAML box + colour token

**Files:**
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml` (add `Brush.Validation.*`)
- Test: none new (View markup; behaviour covered by Task 5). Existing `NoStrayHexTests`/token tests guard the brush.

**Interfaces:**
- Consumes: `NodeDetailViewModel.TokenWarnings` (Task 5); the existing `CountToVis` converter.
- Produces: a visible warning box; new semantic brushes `Brush.Validation.Background`, `Brush.Validation.Border`, `Brush.Validation.Text`.

- [ ] **Step 1: Add the colour tokens**

In `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`, add three semantic brushes (reuse existing palette colours — do NOT introduce hex; the bark tokens `Brush.Bark.Detail.*` are a good reference for value choices, or map to `Brush.Severity.Warning` family). Example mapping onto existing palette entries:

```xml
    <SolidColorBrush x:Key="Brush.Validation.Background" Color="{StaticResource Palette.Amber.610}" Opacity="0.14"/>
    <SolidColorBrush x:Key="Brush.Validation.Border"     Color="{StaticResource Palette.Amber.610}"/>
    <SolidColorBrush x:Key="Brush.Validation.Text"       Color="{StaticResource Palette.Amber.610}"/>
```

If those exact palette keys differ, open `Tokens.axaml` and reuse whatever warning/amber palette key the bark or severity tokens already reference — the rule is "no new hex, reuse an existing `Palette.*` colour". Keep all three keys present in every palette by virtue of referencing shared `Palette.*` (they resolve per-theme automatically).

- [ ] **Step 2: Add the warning box to the view**

In `DialogEditor.Avalonia/Views/NodeDetailView.axaml`, immediately after the bark-warning `Border` block (the one bound to `BarkWarnings`, ~line 340–351), add a sibling box bound to `TokenWarnings`:

```xml
                <Border Background="{DynamicResource Brush.Validation.Background}"
                        BorderBrush="{DynamicResource Brush.Validation.Border}" BorderThickness="1"
                        CornerRadius="2" Padding="6,4" Margin="0,0,0,4"
                        IsVisible="{Binding TokenWarnings.Count, Converter={StaticResource CountToVis}}"
                        ToolTip.Tip="{DynamicResource ToolTip_TokenWarnings}"
                        AutomationProperties.HelpText="{DynamicResource ToolTip_TokenWarnings}">
                    <ItemsControl ItemsSource="{Binding TokenWarnings}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding}" Foreground="{DynamicResource Brush.Validation.Text}"
                                           FontSize="{DynamicResource FontSize.Small}" TextWrapping="Wrap"/>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </Border>
```

- [ ] **Step 3: Verify the build and token tests**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NoStrayHexTests|FullyQualifiedName~PaletteSetParityTests|FullyQualifiedName~TokenRegistryTests"`
Expected: PASS (no stray hex; all palettes still have parity with the new keys).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/NodeDetailView.axaml DialogEditor.Avalonia.Shared/Resources/Tokens.axaml
git commit -m "feat(validation): token-warning box in the node detail panel"
```

---

### Task 7: Flow Analytics — token-issue rows + translation pass (view-model)

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelValidationTests.cs` (create)

**Interfaces:**
- Consumes: `TokenValidationService`/`TokenIssue` (Tasks 2–3); `ConversationEditSnapshot` (already injected via `_getSnapshot`); `NodeTranslation` (`DialogEditor.Core.Models`).
- Produces:
  - `class TokenIssueRowViewModel` with `int NodeId`, `string Language` (empty for Default/Female), `string DisplayText`, `ICommand NavigateCommand`.
  - `FlowAnalyticsViewModel.TokenIssues` (`ObservableCollection<TokenIssueRowViewModel>`).
  - New ctor parameter `Func<IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>>? getTranslations = null` (optional, defaults to empty — keeps existing test/call sites compiling).

- [ ] **Step 1: Add localisation strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml` add:

```xml
  <system:String x:Key="FlowAnalytics_TagIssues">Text tag issues</system:String>
  <system:String x:Key="FlowAnalytics_TagIssues_None">No text tag issues.</system:String>
  <system:String x:Key="FlowAnalytics_TagIssue_Row">Node {0} ({1}): {2}</system:String>
  <system:String x:Key="FlowAnalytics_TagIssue_Row_Default">Node {0}: {1}</system:String>
```

- [ ] **Step 2: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelValidationTests.cs`. Reuse the `SimpleSnapshot` helper style already in `FlowAnalyticsViewModelTests.cs` (open it first). Build a snapshot whose node has a bad token, plus a translations dict with a bad token in a translation.

```csharp
using System.Collections.Generic;
using System.Linq;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class FlowAnalyticsViewModelValidationTests
{
    // Copy the node-snapshot construction from FlowAnalyticsViewModelTests; this
    // helper mirrors its NodeEditSnapshot(...) call exactly.
    private static ConversationEditSnapshot SnapshotWith(string defaultText)
        => new(new[] { FlowAnalyticsTestFactory.Node(0, defaultText) });

    [Fact]
    public void TokenIssues_BadTokenInDefaultText_Surfaced()
    {
        var vm = new FlowAnalyticsViewModel(
            () => SnapshotWith("Hi [Player Nmae]"),
            _ => { },
            () => new Dictionary<string, IReadOnlyList<NodeTranslation>>());
        vm.RefreshCommand.Execute(null);
        Assert.NotEmpty(vm.TokenIssues);
        Assert.Contains(vm.TokenIssues, r => r.NodeId == 0 && r.Language == "");
    }

    [Fact]
    public void TokenIssues_BadTokenInTranslation_Surfaced()
    {
        var translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["fr"] = new[] { new NodeTranslation(0, "Bonjour [Player Nmae]", "") }
        };
        var vm = new FlowAnalyticsViewModel(
            () => SnapshotWith("Hi [Player Name]"),   // default clean
            _ => { },
            () => translations);
        vm.RefreshCommand.Execute(null);
        Assert.Contains(vm.TokenIssues, r => r.NodeId == 0 && r.Language == "fr");
    }

    [Fact]
    public void TokenIssues_CleanConversation_IsEmpty()
    {
        var vm = new FlowAnalyticsViewModel(
            () => SnapshotWith("Hi [Player Name]"),
            _ => { },
            () => new Dictionary<string, IReadOnlyList<NodeTranslation>>());
        vm.RefreshCommand.Execute(null);
        Assert.Empty(vm.TokenIssues);
    }
}
```

Add a tiny factory `FlowAnalyticsTestFactory.Node(int id, string text)` in the test project (or inline the real `NodeEditSnapshot(...)` constructor call copied from `FlowAnalyticsViewModelTests`). Do not invent constructor arguments — copy the exact positional args the existing test uses.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~FlowAnalyticsViewModelValidationTests"`
Expected: FAIL — the three-arg ctor and `TokenIssues` don't exist.

- [ ] **Step 4: Implement the row VM + translation pass**

In `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`:

Add usings:

```csharp
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Services;
```

Add the row view-model (below `FlowIssueViewModel`):

```csharp
public partial class TokenIssueRowViewModel : ObservableObject
{
    private readonly Action<int> _navigate;

    public int    NodeId      { get; }
    public string Language    { get; }   // "" for Default/Female text
    public string Message     { get; }

    public string DisplayText => string.IsNullOrEmpty(Language)
        ? Loc.Format("FlowAnalytics_TagIssue_Row_Default", NodeId, Message)
        : Loc.Format("FlowAnalytics_TagIssue_Row", NodeId, Language, Message);

    public TokenIssueRowViewModel(int nodeId, string language, string message, Action<int> navigate)
    {
        NodeId    = nodeId;
        Language  = language;
        Message   = message;
        _navigate = navigate;
    }

    [RelayCommand]
    private void Navigate() => _navigate(NodeId);
}
```

Add fields, collection, ctor param, and the message formatter to `FlowAnalyticsViewModel`:

```csharp
    private readonly Func<IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>> _getTranslations;
    private readonly TokenValidationService _tokenValidator = new();
    private string _gameId = string.Empty;

    public ObservableCollection<TokenIssueRowViewModel> TokenIssues { get; } = [];

    public FlowAnalyticsViewModel(
        Func<ConversationEditSnapshot?> getSnapshot,
        Action<int>                     navigateToNode,
        Func<IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>>? getTranslations = null,
        string gameId = "")
    {
        _getSnapshot     = getSnapshot;
        _navigateToNode  = navigateToNode;
        _getTranslations = getTranslations
            ?? (() => new Dictionary<string, IReadOnlyList<NodeTranslation>>());
        _gameId          = gameId;
    }
```

Delete the old two-arg constructor body (replace with the above; the two-arg call sites keep working because the new params are optional — but a class may have only one primary set of assignments, so ensure `_getSnapshot`/`_navigateToNode` are still assigned exactly once here and remove the previous ctor).

At the end of `Refresh()` (after the flow-issue loop), add the token pass:

```csharp
        TokenIssues.Clear();
        foreach (var node in snapshot.Nodes)
        {
            AddTokenIssues(node.NodeId, "", node.DefaultText);
            AddTokenIssues(node.NodeId, "", node.FemaleText);
        }
        foreach (var (lang, entries) in _getTranslations())
            foreach (var t in entries)
            {
                AddTokenIssues(t.NodeId, lang, t.DefaultText);
                AddTokenIssues(t.NodeId, lang, t.FemaleText);
            }
```

Add the helper:

```csharp
    private void AddTokenIssues(int nodeId, string language, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var issue in _tokenValidator.Validate(text, _gameId))
        {
            var msg = issue.Kind switch
            {
                TokenIssueKind.UnbalancedMarkup =>
                    Loc.Format("Validation_UnbalancedMarkup", issue.Fragment),
                _ when issue.Suggestion is not null =>
                    Loc.Format("Validation_UnknownToken_Suggest", issue.Fragment, issue.Suggestion),
                _ => Loc.Format("Validation_UnknownToken", issue.Fragment),
            };
            TokenIssues.Add(new TokenIssueRowViewModel(nodeId, language, msg, _navigateToNode));
        }
    }
```

Note: the `Validation_*` string keys were added in Task 5 — reused here.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~FlowAnalyticsViewModel"`
Expected: PASS (both the new validation tests and the existing `FlowAnalyticsViewModelTests`, which still use the two-arg ctor).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelValidationTests.cs
git commit -m "feat(validation): token-issue rows + translation pass in flow analytics VM"
```

---

### Task 8: Flow Analytics — token-issue section (view) + wiring translations & game id

**Files:**
- Modify: `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (the `FlowAnalytics_Click` wiring, ~line 417)
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (expose current-conversation translations + active game id)

**Interfaces:**
- Consumes: `FlowAnalyticsViewModel.TokenIssues`, `TokenIssueRowViewModel.DisplayText`/`NavigateCommand` (Task 7).
- Produces: a visible "Text tag issues" section; `MainWindowViewModel.CurrentConversationTranslations` and `MainWindowViewModel.ActiveGameId` accessors used by the wiring.

- [ ] **Step 1: Expose translations + game id on `MainWindowViewModel`**

In `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`, add public accessors (place near `IsProjectOpen`, ~line 215). Confirm the field names `_project`, `_currentFile`, `_provider` (all verified present) and the type `DialogProject.Patches` (a `Dictionary<string, ConversationPatch>` keyed by conversation file name). If `ActiveGameId` already exists on this VM (it is passed to `Detail.ActiveGameId`), reuse it instead of adding a duplicate — search first.

```csharp
    /// The open conversation's saved translations (language → per-node text),
    /// or empty when no conversation/patch is loaded. Used by Flow Analytics to
    /// validate translation text for the open conversation.
    public IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> CurrentConversationTranslations
    {
        get
        {
            if (_project is not null && _currentFile is not null &&
                _project.Patches.TryGetValue(_currentFile.Name, out var patch))
                return patch.Translations;
            return new Dictionary<string, IReadOnlyList<NodeTranslation>>();
        }
    }
```

Ensure `using DialogEditor.Core.Models;` is present (for `NodeTranslation`).

For the game id: search the VM for how `Detail.ActiveGameId` is set (it is set to a `"poe1"`/`"poe2"`/`""` value somewhere on directory load). Expose that same value as a public read-only property `public string ActiveGameId => <the existing expression>;` if not already public. If it is derived from a field like `_activeGameId`, add the property returning it.

- [ ] **Step 2: Pass translations + game id into the Flow Analytics VM**

In `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`, update the `new FlowAnalyticsViewModel(...)` call (~line 417):

```csharp
            var analyticsVm = new FlowAnalyticsViewModel(
                () => vm.Canvas.BuildSnapshot(),
                nodeId =>
                {
                    var node = vm.Canvas.Nodes.FirstOrDefault(n => n.NodeId == nodeId);
                    if (node is not null) CanvasView.ScrollToNode(node);
                },
                () => vm.CurrentConversationTranslations,
                vm.ActiveGameId);
```

- [ ] **Step 3: Add the token-issue section to the window**

In `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`, the issues area is `Grid.Row="1"` holding a single `ScrollViewer`. Replace that `ScrollViewer` (lines ~111–156) with a two-part vertical layout so flow issues and tag issues each scroll within their share. Wrap both in a `Grid RowDefinitions="*,Auto,*"`:

```xml
        <!-- ── Issues area: flow issues + text-tag issues ─────────────────── -->
        <Grid Grid.Row="1" RowDefinitions="*,Auto,*" Margin="0,6,0,6">

            <!-- Flow issues (unchanged template) -->
            <ScrollViewer Grid.Row="0">
                <ItemsControl ItemsSource="{Binding Issues}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="vm:FlowIssueViewModel">
                            <!-- (keep the existing FlowIssueViewModel template exactly as-is) -->
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>

            <!-- Section divider + header -->
            <StackPanel Grid.Row="1" Spacing="4" IsVisible="{Binding HasData}">
                <Separator Background="{DynamicResource Brush.Border.Default}" Height="1" Margin="0,4"/>
                <TextBlock Classes="section-header"
                           Text="{DynamicResource FlowAnalytics_TagIssues}"
                           ToolTip.Tip="{DynamicResource ToolTip_TokenWarnings}"/>
                <TextBlock Text="{DynamicResource FlowAnalytics_TagIssues_None}"
                           Foreground="{DynamicResource Brush.Text.Disabled}"
                           FontSize="{DynamicResource FontSize.Small}" FontStyle="Italic"
                           IsVisible="{Binding !TokenIssues.Count}"/>
            </StackPanel>

            <!-- Text-tag issues -->
            <ScrollViewer Grid.Row="2">
                <ItemsControl ItemsSource="{Binding TokenIssues}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="vm:TokenIssueRowViewModel">
                            <Grid ColumnDefinitions="*,Auto" Margin="0,2" Height="28">
                                <TextBlock Grid.Column="0"
                                           Text="{Binding DisplayText}"
                                           Foreground="{DynamicResource Brush.Text.Secondary}"
                                           FontSize="{DynamicResource FontSize.Label}"
                                           VerticalAlignment="Center"
                                           TextTrimming="CharacterEllipsis"/>
                                <Button Grid.Column="1"
                                        Content="→"
                                        Command="{Binding NavigateCommand}"
                                        Background="Transparent" BorderThickness="0"
                                        Foreground="{DynamicResource Brush.Text.Muted}"
                                        FontSize="{DynamicResource FontSize.Subtitle}"
                                        Padding="8,0" Margin="4,0,0,0"
                                        VerticalAlignment="Center"
                                        ToolTip.Tip="{DynamicResource ToolTip_FlowAnalytics_Navigate}"
                                        AutomationProperties.HelpText="{DynamicResource ToolTip_FlowAnalytics_Navigate}"
                                        AutomationProperties.Name="{DynamicResource AutomationName_GoToNode}"/>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>
```

Keep the existing `FlowIssueViewModel` `DataTemplate` content verbatim inside the flow-issues `ScrollViewer` — only its wrapping `Grid` row placement changes. The `!TokenIssues.Count` binding uses Avalonia's built-in bool negation of a non-zero count; if the headless build rejects int-to-bool negation, substitute the existing `CountToVis`/inverse converter pattern used elsewhere in the project (search for an `InverseCountToVis`/`ZeroToVis` converter; if none, bind `IsVisible="{Binding TokenIssues.Count, Converter={StaticResource CountToVis}}"` on the list and invert the empty-state with the opposite converter).

- [ ] **Step 4: Build and run the full test suite**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: PASS (entire suite; no regressions).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat(validation): text-tag issues section in flow analytics window"
```

---

### Task 9: End-to-end app verification + Gaps.md update

**Files:**
- Modify: `Gaps.md` (mark the validation half done)

**Interfaces:** none (documentation + manual verification).

- [ ] **Step 1: Run the app and verify in the real GUI**

Use the `running-the-app` skill to launch the editor. Open the sample project (Help ▸ Create Sample Project if needed), select a node, and:
- Type `[Player Nmae]` into the Default text field → the token-warning box appears with "did you mean [Player Name]?".
- Type `<i>unclosed` → an unbalanced-markup warning appears.
- Type `[Say nothing.]` → **no** warning (convention silent).
- Open Test ▸ Flow Analytics (or the relevant menu) → the "Text tag issues" section lists the node; the → button navigates to it.

Capture a screenshot of the detail-panel warning and the Flow Analytics section for the record.

- [ ] **Step 2: Update `Gaps.md`**

In `Gaps.md`, under "Token autocomplete and validation in node text editing", change the **Validation** bullet from open to implemented, mirroring the autocomplete bullet's style. Suggested text:

```markdown
- **Validation ✅ IMPLEMENTED (2026-07-07).** Hybrid unknown-token detection
  (fuzzy "did you mean" over digit-normalised forms, so both `[Player Nmae]`
  and `[Specfied 0]` are caught with a suggestion) plus tag-name-only markup
  balance (lenient on vanilla malformed attributes — the shipped `<link>`
  missing-quote case passes). Free-text stage directions / language markers
  are never flagged (guarded by a shipped-convention false-positive regression
  test). Surfaced as a live warning box in the node detail panel (Default/Female)
  and a "Text tag issues" section in Flow Analytics (the open conversation's
  Default/Female + every translation language). Pure `TokenValidationService`
  (`DialogEditor.ViewModels`) + `tags.json` `lowercase` flag. Per-conversation
  scope; a project-wide translation sweep is deferred. Spec:
  docs/superpowers/specs/2026-07-07-token-validation-design.md.
```

Also update the section's opening sentence so it no longer implies validation is outstanding (both assists now implemented).

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark token validation half implemented"
```

---

## Self-Review

**Spec coverage:**
- Layer A (`Lowercase` data + drift test) → Task 1. ✔
- Layer B unknown-token (exact incl. lowercase/param, fuzzy w/ digit-normalization) → Task 2. ✔
- Layer B markup (tag-name balance, lenient attrs, self-closing skip, unknown-tag skip) → Task 3. ✔
- False-positive regression corpus → Task 4. ✔
- Layer C1 detail panel (`TokenWarnings` VM + XAML box + brush + strings + live recompute) → Tasks 5–6. ✔
- Layer C2 Flow Analytics (row VM, Default/Female + translations pass, separate section, navigation, scope limit) → Tasks 7–8. ✔
- Cross-cutting: localisation (Tasks 5/7 strings), tooltips (Tasks 6/8), colour tokens (Task 6 + `NoStrayHexTests` in Task 6 step 3), pure/non-throwing service (Task 2), no bare catch (service is pure; VM code has no try/catch). ✔
- E2E verification + Gaps.md → Task 9. ✔

**Placeholder scan:** No TBD/TODO left as work; the one deliberate "copy the existing helper/ctor" instructions (Tasks 5, 7) are explicit direction to reuse verified real signatures rather than fabricate them, with the exact fallback described.

**Type consistency:** `TokenIssue(Kind, Fragment, Suggestion, Position)` and `TokenIssueKind {UnknownToken, UnbalancedMarkup}` are defined in Task 2 and used identically in Tasks 3/5/7. `TokenWarnings` (Task 5) and `TokenIssues`/`TokenIssueRowViewModel` (Task 7) names match their view bindings (Tasks 6/8). `Validate(text, gameId)` signature stable across all consumers. The `Validation_*` string keys defined in Task 5 are reused verbatim in Task 7. `CurrentConversationTranslations` return type (`IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>`) matches the Task 7 ctor parameter type.

**Known implementation-time verifications flagged in-plan (not fabricated):** the exact `NodeDetailViewModel`/`FlowAnalyticsViewModel`/`NodeEditSnapshot` constructor shapes and the existing `ActiveGameId` accessor must be confirmed against the real files while implementing Tasks 5, 7, 8 (each step says so). These are reuse instructions, not placeholders.
