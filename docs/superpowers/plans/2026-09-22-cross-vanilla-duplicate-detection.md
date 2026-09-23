# Cross-Vanilla Duplicate Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the Validate Text… duplicate sweep also report the writer's lines that are identical or near-identical to lines in the installed game, behind an opt-in "Base game" toggle. Closes #14.

**Architecture:** A new IO-only loader (`VanillaLineCorpus`) reads every game conversation's text once, off the UI thread. The pure `DuplicateLineScanner` gains an optional vanilla list: vanilla lines join the exact grouping, and a lossless character-3-gram prefilter (`QGramIndex`) narrows the writer-vs-vanilla near pass. `TextTagValidationViewModel` gets an async path used only when the toggle is on, so the existing synchronous behaviour and its tests are unchanged.

**Tech Stack:** C# / .NET, Avalonia 11 (XAML), CommunityToolkit.Mvvm (`[ObservableProperty]`), xUnit + Avalonia.Headless.XUnit.

**Spec:** `docs/superpowers/specs/2026-09-22-cross-vanilla-duplicate-detection-design.md`

## Global Constraints

- **TDD, strictly red → green → refactor** (CLAUDE.md). Every task writes its failing test first and runs it to see the expected failure before implementing.
- **No hard-coded user-visible text.** Every new label/tooltip/status string goes in `DialogEditor.Avalonia/Resources/Strings.axaml` and is read via `{DynamicResource Key}` in XAML or `Loc.Get` / `Loc.Format` in C#.
- **Every interactive control carries a detailed `ToolTip.Tip`**, plus `AutomationProperties.Name` and `AutomationProperties.HelpText` (matching the neighbouring checkboxes).
- **Error handling:** every caught exception in production code is logged via `AppLog.Error(...)` or `AppLog.Warn(...)`. `OperationCanceledException` is swallowed silently. No bare `catch { }`.
- **Minimum line length:** 4 words after normalization (`DuplicateLineScanner.MinWords`), for vanilla lines as for writer lines.
- **Threshold presets:** 0.75 / 0.80 / 0.85 / 0.90 / 0.95; the scanner clamps to [0.50, 0.99].
- **Vanilla side is primary-language only.** Vanilla lines always carry language label `""`.
- **Excluded vanilla nodes:** those whose text the project owns — `patch.AddedNodes` ∪ node ids in any `patch.Translations` list. `ModifiedNodes` alone does **not** exclude a node.
- **Tests run serially** (the project disables xUnit parallelism for the AppSettings/Loc race — see memory `project_flaky_test_appsettings`). Do not re-enable it.
- **Put the "why" in code comments** (project convention): design reasoning lives beside the code, not only in the spec.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test command pattern: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~<ClassName>"`.

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `DialogEditor.ViewModels/Services/QGramIndex.cs` | Create | Pure, lossless near-match prefilter over a fixed string list |
| `DialogEditor.ViewModels/Services/VanillaLineCorpus.cs` | Create | The only IO: read every game conversation's lines |
| `DialogEditor.ViewModels/Services/DuplicateLineScanner.cs` | Modify | Options/LineRef fields; vanilla param; exclusions; exact + near integration; expose `Normalize`/`WordCount`/`Ratio`/`MinWords` as `internal` |
| `DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs` | Modify | `IncludeBaseGame`, loader delegate, async base-game path, cancellation, base-game location label |
| `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Modify | Wire the loader, forward corpus, persist the new option |
| `DialogEditor.ViewModels/Services/AppSettings.cs` | Modify | `DuplicateIncludeBaseGame` setting |
| `DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml` (+ `.cs`) | Modify | Checkbox, busy indicator, failure line; `OnClosed` → `Cancel()` |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | Modify | New strings |
| `DialogEditor.Tests/Helpers/FakeGameDataProvider.cs` | Modify | `Unreadable` set to simulate a broken conversation |
| `DialogEditor.Tests/Helpers/EchoStringProvider.cs` | Create | Stub `Loc` provider that echoes keys but lets chosen keys carry real formats |
| `DialogEditor.Tests/Services/QGramIndexTests.cs` | Create | |
| `DialogEditor.Tests/Services/VanillaLineCorpusTests.cs` | Create | |
| `DialogEditor.Tests/Services/DuplicateLineScannerTests.cs` | Modify | |
| `DialogEditor.Tests/ViewModels/TextTagValidationViewModelTests.cs` | Modify | Mechanical `dupScan` lambda update + new tests |
| `DialogEditor.Tests/ViewModels/MainWindowViewModelDuplicateTests.cs` | Modify | End-to-end wiring test |
| `DialogEditor.Tests/Views/TextTagValidationWindowTests.cs` | Modify | Mechanical lambda update + checkbox smoke test |

---

### Task 1: `QGramIndex` — lossless near-match prefilter

**Files:**
- Create: `DialogEditor.ViewModels/Services/QGramIndex.cs`
- Modify: `DialogEditor.ViewModels/Services/DuplicateLineScanner.cs` (make `Ratio` `internal`, for the brute-force oracle in the test)
- Test: `DialogEditor.Tests/Services/QGramIndexTests.cs`

**Interfaces:**
- Consumes: `DuplicateLineScanner.Ratio(string, string)` (today `private static`; change to `internal static`, no other change).
- Produces:
  ```csharp
  internal sealed class QGramIndex
  {
      public QGramIndex(IReadOnlyList<string> strings);        // strings already normalized
      public IEnumerable<int> Query(string normalized, double threshold); // indices into `strings`
  }
  ```
  Guarantee: for every `i` with `DuplicateLineScanner.Ratio(query, strings[i]) >= threshold`, `i` is in `Query(query, threshold)`. Not thread-safe (reuses a buffer).

- [ ] **Step 1: Make `Ratio` internal**

In `DuplicateLineScanner.cs` change `private static double Ratio(string a, string b)` to `internal static double Ratio(string a, string b)`. Nothing else.

- [ ] **Step 2: Write the failing tests**

Create `DialogEditor.Tests/Services/QGramIndexTests.cs`:

```csharp
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

        foreach (var t in Presets)
        foreach (var query in corpus.Where((_, i) => i % 3 == 0))
        {
            var found = index.Query(query, t).ToHashSet();
            for (var i = 0; i < corpus.Count; i++)
                if (DuplicateLineScanner.Ratio(query, corpus[i]) >= t)
                    Assert.True(found.Contains(i),
                        $"missed at t={t}: «{query}» vs «{corpus[i]}»");
        }
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
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~QGramIndexTests"`
Expected: build FAILS — `The type or namespace name 'QGramIndex' could not be found`.

- [ ] **Step 4: Implement `QGramIndex`**

Create `DialogEditor.ViewModels/Services/QGramIndex.cs`:

```csharp
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
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~QGramIndexTests"`
Expected: PASS, 5 tests. If `Query_ShortLinesAtLowThreshold_ReturnedOnLengthAlone` fails, check the arithmetic in its comment against the implementation before touching either: at M = 8, t = 0.75, k = 2 and the bound is 8 − 2 − 6 = 0.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/Services/QGramIndex.cs DialogEditor.ViewModels/Services/DuplicateLineScanner.cs DialogEditor.Tests/Services/QGramIndexTests.cs
git commit -m "feat(duplicates): lossless 3-gram prefilter for base-game near pass (#14)"
```

---

### Task 2: `VanillaLineCorpus` — read the game's lines

**Files:**
- Create: `DialogEditor.ViewModels/Services/VanillaLineCorpus.cs`
- Modify: `DialogEditor.ViewModels/Services/DuplicateLineScanner.cs` (make `MinWords`, `Normalize`, `WordCount` `internal`)
- Modify: `DialogEditor.Tests/Helpers/FakeGameDataProvider.cs`
- Test: `DialogEditor.Tests/Services/VanillaLineCorpusTests.cs`

**Interfaces:**
- Consumes: `IGameDataProvider.EnumerateConversations()`, `LoadConversation(ConversationFile)`; `Conversation(Name, Nodes, Strings)`; `StringTable.Get(int) → StringEntry?(Id, DefaultText, FemaleText)`; `DuplicateLineScanner.Normalize/WordCount/MinWords` (made `internal`).
- Produces:
  ```csharp
  public record VanillaLine(string ConversationName, int NodeId, string Text, bool IsFemale);
  public static class VanillaLineCorpus
  {
      public static IReadOnlyList<VanillaLine> Load(IGameDataProvider provider, CancellationToken ct = default);
  }
  ```
  `Text` is the original text, trimmed. Blank lines and lines under 4 normalized words are omitted. Female lines are always emitted when non-blank (the scanner decides whether to use them).

- [ ] **Step 1: Expose the scanner's normalization helpers**

In `DuplicateLineScanner.cs` change `private const int MinWords = 4;` → `internal const int MinWords = 4;`, `private static string Normalize(` → `internal static string Normalize(`, `private static int WordCount(` → `internal static int WordCount(`. Nothing else.

- [ ] **Step 2: Let the fake provider simulate an unreadable conversation**

In `DialogEditor.Tests/Helpers/FakeGameDataProvider.cs`, add below the `Language` property:

```csharp
    /// Conversations whose load throws — lets whole-game scanners prove they skip a
    /// broken file instead of aborting.
    public HashSet<string> Unreadable { get; } = [];
```

and replace `LoadConversation` with:

```csharp
    public Conversation LoadConversation(ConversationFile file) =>
        Unreadable.Contains(file.Name)
            ? throw new InvalidDataException($"unreadable: {file.Name}")
            : _conversations[file.Name];
```

- [ ] **Step 3: Write the failing tests**

Create `DialogEditor.Tests/Services/VanillaLineCorpusTests.cs`:

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class VanillaLineCorpusTests
{
    private const string Long  = "the wind howls through the rigging tonight";
    private const string Long2 = "she says the wind howls through the rigging";

    private static ConversationNode Node(int id) =>
        new(id, false, SpeakerCategory.Npc, "spk", "", [], [], [], "Conversation", "None");

    private static Conversation Conv(string name, params (int Id, string Def, string Fem)[] lines) =>
        new(name,
            lines.Select(l => Node(l.Id)).ToList(),
            new StringTable(lines.Select(l => new StringEntry(l.Id, l.Def, l.Fem))));

    [Fact]
    public void Load_EmitsDefaultAndFemaleText()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", (1, "  " + Long + " ", Long2)));

        var lines = VanillaLineCorpus.Load(provider);

        Assert.Equal(2, lines.Count);
        Assert.Contains(new VanillaLine("c", 1, Long, false), lines);   // trimmed
        Assert.Contains(new VanillaLine("c", 1, Long2, true), lines);
    }

    [Fact] // Same 4-word floor as the writer side, applied at load so the index stays small.
    public void Load_DropsBlankAndShortLines()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            Conv("c", (1, "Yes.", ""), (2, "", ""), (3, "three words only", ""), (4, Long, "")));

        var lines = VanillaLineCorpus.Load(provider);

        Assert.Equal(4, Assert.Single(lines).NodeId);
    }

    [Fact] // A node with no string-table entry (script-only) contributes nothing.
    public void Load_NodeWithoutStringEntry_Skipped()
    {
        var conv = new Conversation("c", [Node(1), Node(2)],
            new StringTable([new StringEntry(1, Long, "")]));
        var provider = new FakeGameDataProvider("poe2", "en", conv);

        Assert.Equal(1, Assert.Single(VanillaLineCorpus.Load(provider)).NodeId);
    }

    [Fact]
    public void Load_UnreadableConversation_SkippedOthersLoad()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            Conv("broken", (1, Long, "")), Conv("fine", (2, Long, "")));
        provider.Unreadable.Add("broken");

        var lines = VanillaLineCorpus.Load(provider);

        Assert.Equal("fine", Assert.Single(lines).ConversationName);
    }

    [Fact]
    public void Load_CancelledToken_Throws()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", (1, Long, "")));

        Assert.Throws<OperationCanceledException>(() =>
            VanillaLineCorpus.Load(provider, new CancellationToken(canceled: true)));
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~VanillaLineCorpusTests"`
Expected: build FAILS — `VanillaLineCorpus` / `VanillaLine` not found.

- [ ] **Step 5: Implement**

Create `DialogEditor.ViewModels/Services/VanillaLineCorpus.cs`:

```csharp
using DialogEditor.Core.GameData;

namespace DialogEditor.ViewModels.Services;

/// One line of the installed game's text. Text is the original, trimmed; IsFemale marks
/// the female variant.
public record VanillaLine(string ConversationName, int NodeId, string Text, bool IsFemale);

/// <summary>
/// Reads every conversation the game exposes, as it is on disk, for the duplicate sweep's
/// base-game comparison (issue #14). The only IO in that feature — everything downstream
/// (DuplicateLineScanner, QGramIndex) is pure.
///
/// Deliberately does NOT apply the project's patches (unlike SpeakerLineScanner): the
/// scanner removes the nodes whose text the project owns at scan time, so this list stays
/// valid while the project changes and can be cached for the window's lifetime.
///
/// The provider loads in its current Language, which is the project's primary language.
/// Female text is always emitted; the scan decides whether to use it, so toggling "Female
/// text" never forces a reload. Lines under the scanner's word floor are dropped here to
/// keep the index small. An unreadable conversation is warned and skipped, never fatal.
/// Spec: docs/superpowers/specs/2026-09-22-cross-vanilla-duplicate-detection-design.md
/// </summary>
public static class VanillaLineCorpus
{
    public static IReadOnlyList<VanillaLine> Load(IGameDataProvider provider, CancellationToken ct = default)
    {
        var lines = new List<VanillaLine>();

        foreach (var file in provider.EnumerateConversations())
        {
            ct.ThrowIfCancellationRequested();

            Core.Models.Conversation conv;
            try
            {
                conv = provider.LoadConversation(file);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Base-game duplicate scan: could not load '{file.Name}': {ex.Message}");
                continue;
            }

            foreach (var node in conv.Nodes)
            {
                var entry = conv.Strings.Get(node.NodeId);
                if (entry is null) continue;
                Add(conv.Name, node.NodeId, entry.DefaultText, female: false);
                Add(conv.Name, node.NodeId, entry.FemaleText,  female: true);
            }
        }

        return lines;

        void Add(string conv, int nodeId, string? text, bool female)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (DuplicateLineScanner.WordCount(DuplicateLineScanner.Normalize(text)) < DuplicateLineScanner.MinWords)
                return;
            lines.Add(new VanillaLine(conv, nodeId, text.Trim(), female));
        }
    }
}
```

Note: `Conversation` is qualified as `Core.Models.Conversation` because the file does not import `DialogEditor.Core.Models`; if the build prefers, add `using DialogEditor.Core.Models;` and use `Conversation` — either is fine.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~VanillaLineCorpusTests|FullyQualifiedName~SpeakerLineScannerTests|FullyQualifiedName~DuplicateLineScannerTests"`
Expected: PASS (the fake-provider change must not break `SpeakerLineScannerTests`).

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.ViewModels/Services/VanillaLineCorpus.cs DialogEditor.ViewModels/Services/DuplicateLineScanner.cs DialogEditor.Tests/Helpers/FakeGameDataProvider.cs DialogEditor.Tests/Services/VanillaLineCorpusTests.cs
git commit -m "feat(duplicates): load the base game's lines for comparison (#14)"
```

---

### Task 3: Scanner — compare writer lines against the base game

**Files:**
- Modify: `DialogEditor.ViewModels/Services/DuplicateLineScanner.cs`
- Test: `DialogEditor.Tests/Services/DuplicateLineScannerTests.cs`

**Interfaces:**
- Consumes: `QGramIndex` (Task 1), `VanillaLine` (Task 2).
- Produces (all additions are trailing optional parameters, so existing call sites compile unchanged):
  ```csharp
  public record LineRef(string ConversationName, int NodeId, string Text,
      string Language = "", bool IsFemale = false, bool FromBaseGame = false);
  public record DuplicateScanOptions(double NearThreshold = DuplicateLineScanner.DefaultNearThreshold,
      bool IncludeFemaleText = false, bool IncludeOtherLanguages = false, bool IncludeBaseGame = false);
  public static DuplicateLineReport Scan(DialogProject project, string primaryLanguage,
      DuplicateScanOptions? options = null, IReadOnlyList<VanillaLine>? vanilla = null);
  ```
  Semantics: exact group `Members` are ordered writer-first, so `Members[0]` is always a writer line. Base-game near pairs have `A` = writer line, `B` = vanilla line.

- [ ] **Step 1: Write the failing tests**

In `DuplicateLineScannerTests.cs`, extend the `Opts` helper (replace the existing one) and add a vanilla helper plus the tests below, at the end of the class. Add `using DialogEditor.Core.Editing;` at the top if not present (for `NodeEditSnapshot`).

```csharp
    private static DuplicateScanOptions Opts(
        bool female = false, bool otherLangs = false,
        double threshold = DuplicateLineScanner.DefaultNearThreshold,
        bool baseGame = false) =>
        new(threshold, female, otherLangs, baseGame);

    private static IReadOnlyList<VanillaLine> Vanilla(params (string Conv, int Id, string Text, bool Fem)[] v) =>
        v.Select(x => new VanillaLine(x.Conv, x.Id, x.Text, x.Fem)).ToList();

    // ── Base game (issue #14, cross-vanilla) ────────────────────────────────

    [Fact]
    public void BaseGame_ExactMatch_ReportedWithWriterFirst()
    {
        var project = Project(PatchWith("mine", (1, L)));
        var vanilla = Vanilla(("aaa_vanilla", 7, "The wind howls through the rigging tonight", false));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true), vanilla);

        var group = Assert.Single(report.Exact);
        Assert.Equal(2, group.Members.Count);
        Assert.False(group.Members[0].FromBaseGame);   // navigation target is the writer's node,
        Assert.Equal("mine", group.Members[0].ConversationName); // even though "aaa" sorts first
        Assert.True(group.Members[1].FromBaseGame);
        Assert.Equal(7, group.Members[1].NodeId);
    }

    [Fact]
    public void BaseGame_NearMatch_ReportedWriterAsA()
    {
        var project = Project(PatchWith("mine", (1, L)));
        var vanilla = Vanilla(("v", 7, "the wind howls through the riggings tonight", false));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true), vanilla);

        var pair = Assert.Single(report.Near);
        Assert.False(pair.A.FromBaseGame);
        Assert.True(pair.B.FromBaseGame);
        Assert.Equal(("v", 7), (pair.B.ConversationName, pair.B.NodeId));
    }

    [Fact] // Vanilla-vs-vanilla is the game's business, not the writer's.
    public void BaseGame_VanillaOnlyDuplicates_NotReported()
    {
        var project = Project(PatchWith("mine", (1, "a completely different line of my own")));
        var vanilla = Vanilla(("v1", 1, L, false), ("v2", 2, L, false),
                              ("v3", 3, "the wind howls through the riggings tonight", false));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true), vanilla);

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // An edit is close to its own original by definition — not a finding.
    public void BaseGame_EditedNodesOwnOriginal_Excluded()
    {
        var project = Project(PatchWith("c", (5, "the wind howls through the rigging tonight, lads")));
        var vanilla = Vanilla(("c", 5, L, false));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true), vanilla);

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // Pack already applied to the install: the on-disk "vanilla" copy IS the writer's line.
    public void BaseGame_AddedNodeAlreadyOnDisk_Excluded()
    {
        var added = new NodeEditSnapshot(
            10, false, SpeakerCategory.Npc, "spk", "", "", "",
            "Conversation", "None", "", "", "", false, false, [], [], []);
        var patch = new ConversationPatch("c", ConversationPatch.CurrentSchemaVersion, [added], [], []);
        var project = Project(patch, PatchWith("other", (1, L)));
        var vanilla = Vanilla(("c", 10, L, false));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true), vanilla);

        Assert.Empty(report.Exact);
    }

    [Fact] // A structural-only edit leaves the node's text genuinely vanilla — still compared.
    public void BaseGame_StructurallyModifiedNode_NotExcluded()
    {
        var mod = new NodeModification(7, new Dictionary<string, FieldChange>(), [], []);
        var structural = new ConversationPatch("v", ConversationPatch.CurrentSchemaVersion, [], [], [mod]);
        var project = Project(structural, PatchWith("mine", (1, L)));
        var vanilla = Vanilla(("v", 7, L, false));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true), vanilla);

        Assert.Single(report.Exact);
    }

    [Fact] // Two identical vanilla lines must not produce two rows for one writer line.
    public void BaseGame_IdenticalVanillaLines_OneNearPair()
    {
        var project = Project(PatchWith("mine", (1, L)));
        const string near = "the wind howls through the riggings tonight";
        var vanilla = Vanilla(("v2", 2, near, false), ("v1", 1, near, false));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true), vanilla);

        var pair = Assert.Single(report.Near);
        Assert.Equal("v1", pair.B.ConversationName);   // lowest conversation is the representative
    }

    [Fact]
    public void BaseGame_IgnoredKeys_SuppressFindings()
    {
        const string nearV = "the wind howls through the riggings tonight";
        var normL = L;                       // L is already normalized (lowercase, single spaces)
        var project = Project(PatchWith("mine", (1, L), (2, "gold is never enough for a hungry friend")))
            .WithIgnoredDuplicate(new IgnoredDuplicate(DuplicateKind.Exact,
                ["gold is never enough for a hungry friend"], "x"))
            .WithIgnoredDuplicate(new IgnoredDuplicate(DuplicateKind.Near,
                new[] { normL, nearV }.OrderBy(s => s, StringComparer.Ordinal).ToList(), "y"));
        var vanilla = Vanilla(("v", 1, nearV, false), ("v", 2, "Gold is never enough for a hungry friend", false));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true), vanilla);

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact]
    public void BaseGame_FemaleVanillaLines_OnlyWithFemaleToggle()
    {
        var project = Project(PatchWith("mine", (1, L)));
        var vanilla = Vanilla(("v", 1, L, true));

        Assert.Empty(DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true), vanilla).Exact);
        var group = Assert.Single(
            DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true, female: true), vanilla).Exact);
        Assert.True(group.Members[1].IsFemale && group.Members[1].FromBaseGame);
    }

    [Fact] // The historical report is untouched unless the toggle is on AND a corpus is given.
    public void BaseGame_ToggleOffOrNoCorpus_ReportUnchanged()
    {
        var project = Project(PatchWith("mine", (1, L)));
        var vanilla = Vanilla(("v", 1, L, false));

        Assert.Empty(DuplicateLineScanner.Scan(project, "en", Opts(), vanilla).Exact);
        Assert.Empty(DuplicateLineScanner.Scan(project, "en", Opts(baseGame: true), null).Exact);
    }

    [Fact] // The vanilla side is primary-language only; other-language lines never meet it.
    public void BaseGame_OtherLanguageWriterLine_NotComparedToVanilla()
    {
        var project = Project(PatchTr("mine", ("de", 1, L, "")));
        var vanilla = Vanilla(("v", 1, L, false));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(otherLangs: true, baseGame: true), vanilla);

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DuplicateLineScannerTests"`
Expected: build FAILS — `DuplicateScanOptions` has no 4th parameter / `Scan` has no `vanilla` parameter.

- [ ] **Step 3: Implement**

In `DuplicateLineScanner.cs`:

(a) Records — replace `LineRef` and `DuplicateScanOptions`:

```csharp
/// One located line. Text is the original (trimmed) writer text, for display.
/// Language follows the TextTagIssueRow convention: "" means the primary language,
/// anything else is the real language code. IsFemale marks the female variant.
/// FromBaseGame marks a line read from the installed game rather than the project
/// (issue #14). All three default so existing construction sites stay valid.
public record LineRef(
    string ConversationName, int NodeId, string Text,
    string Language = "", bool IsFemale = false, bool FromBaseGame = false);

/// What the duplicate sweep should look at. Bundled into a record rather than threaded
/// as loose values because the consuming ViewModel constructor is already long.
/// Every scope flag defaults OFF, so the historical report is what you get unasked.
public record DuplicateScanOptions(
    double NearThreshold         = DuplicateLineScanner.DefaultNearThreshold,
    bool   IncludeFemaleText     = false,
    bool   IncludeOtherLanguages = false,
    bool   IncludeBaseGame       = false);
```

(b) Add to the class `<summary>` a fourth rule, after rule 3:

```
///   4. Base-game lines (issue #14) are reported only when they involve a writer line —
///      vanilla-vs-vanilla is the game's business, and skipping it is what makes the pass
///      affordable. A vanilla node whose text the project owns (added, or has a translation
///      entry) is dropped: it is either the writer's own original or, on an install the
///      pack was applied to, the writer's own line.
```

(c) Signature:

```csharp
    public static DuplicateLineReport Scan(
        DialogProject project, string primaryLanguage, DuplicateScanOptions? options = null,
        IReadOnlyList<VanillaLine>? vanilla = null)
```

(d) After `var candidates = byKey.Values.ToList();` insert:

```csharp
        // 1b. Base-game candidates (rule 4). Primary-language label "" always, so they only
        //     ever meet primary-language writer lines (rule 2).
        var vanillaCands = new List<(LineRef Ref, string Norm)>();
        if (opts.IncludeBaseGame && vanilla is not null)
        {
            var owned = OwnedNodes(project);
            foreach (var v in vanilla)
            {
                if (v.IsFemale && !opts.IncludeFemaleText) continue;
                if (owned.Contains((v.ConversationName, v.NodeId))) continue;
                var norm = Normalize(v.Text);
                if (WordCount(norm) < MinWords) continue;   // corpus filters too; direct callers may not
                vanillaCands.Add((new LineRef(v.ConversationName, v.NodeId, v.Text.Trim(),
                                              "", v.IsFemale, FromBaseGame: true), norm));
            }
        }
```

(e) Replace the exact-tier loop (step 3) with:

```csharp
        // 3. Exact: group by (language, normalized text) — rule 2. A group counts only if
        //    it spans two DISTINCT nodes (rule 1) and contains a writer line (rule 4). The
        //    group's ignore Key stays the bare normalized text, hence the cross-language
        //    reach noted above. Writer members sort first so Members[0] — the navigation
        //    target — is always the writer's own node.
        var exact      = new List<ExactDuplicateGroup>();
        var exactNorms = new HashSet<(string Lang, string Norm)>();
        foreach (var g in candidates.Concat(vanillaCands)
                     .GroupBy(c => (Lang: c.Ref.Language, c.Norm))
                     .Where(g => g.Any(c => !c.Ref.FromBaseGame) &&
                                 g.Select(c => (c.Ref.ConversationName, c.Ref.NodeId))
                                  .Distinct().Count() >= 2))
        {
            exactNorms.Add(g.Key);
            if (ignoredExact.Contains(g.Key.Norm)) continue;
            var members = g.Select(c => c.Ref)
                .OrderBy(r => r.FromBaseGame)
                .ThenBy(r => r.ConversationName, StringComparer.Ordinal)
                .ThenBy(r => r.NodeId)
                .ThenBy(r => r.IsFemale)
                .ToList();
            exact.Add(new ExactDuplicateGroup(g.Key.Norm, members[0].Text, members));
        }
```

Note: a writer node matching only its own vanilla copy cannot form a group — its vanilla copy was dropped as owned in 1b.

(f) After the writer-vs-writer near loop (end of step 4) and before `return`, insert:

```csharp
        // 5. Near, writer vs base game (rule 4) — no vanilla-vs-vanilla pass. Vanilla lines
        //    are collapsed to one representative per normalized text first: two identical
        //    vanilla lines must not yield two rows for one writer line, and the index shrinks.
        //    QGramIndex is a lossless prefilter, so this reports exactly what a brute-force
        //    Levenshtein sweep would.
        if (vanillaCands.Count > 0)
        {
            var reps = vanillaCands
                .Where(c => !exactNorms.Contains((c.Ref.Language, c.Norm)))
                .GroupBy(c => c.Norm)
                .Select(g => g.OrderBy(c => c.Ref.ConversationName, StringComparer.Ordinal)
                              .ThenBy(c => c.Ref.NodeId)
                              .ThenBy(c => c.Ref.IsFemale)
                              .First())
                .ToList();
            var index = new QGramIndex(reps.Select(r => r.Norm).ToList());

            foreach (var a in candidates.Where(c => c.Ref.Language.Length == 0 &&
                                                    !exactNorms.Contains(("", c.Norm))))
            {
                foreach (var id in index.Query(a.Norm, threshold))
                {
                    var b     = reps[id];
                    var ratio = Ratio(a.Norm, b.Norm);
                    if (ratio < threshold) continue;

                    var key = new[] { a.Norm, b.Norm }.OrderBy(s => s, StringComparer.Ordinal).ToList();
                    if (ignoredNear.Contains(NearKey(key[0], key[1]))) continue;

                    near.Add(new NearDuplicatePair(key, a.Ref, b.Ref, (int)Math.Round(ratio * 100)));
                }
            }
        }
```

(g) Add the helper beside `Normalize`:

```csharp
    /// Nodes whose text the project owns: added nodes, and any node with a translation
    /// entry (the DiffEngine records every added or edited node's text there). A node in
    /// ModifiedNodes alone had structural edits only; its text is still vanilla.
    private static HashSet<(string Conv, int Node)> OwnedNodes(DialogProject project)
    {
        var owned = new HashSet<(string, int)>();
        foreach (var (conv, patch) in project.Patches)
        {
            foreach (var n in patch.AddedNodes) owned.Add((conv, n.NodeId));
            foreach (var entries in patch.Translations.Values)
                foreach (var t in entries) owned.Add((conv, t.NodeId));
        }
        return owned;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DuplicateLineScannerTests"`
Expected: PASS — all new tests plus every pre-existing one (they don't pass `vanilla`, so behaviour is unchanged).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/DuplicateLineScanner.cs DialogEditor.Tests/Services/DuplicateLineScannerTests.cs
git commit -m "feat(duplicates): compare writer lines against the base game (#14)"
```

---

### Task 4: ViewModel — opt-in async base-game path

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs:571-574` (compile fix only: forward the corpus)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (location-label strings used by `Describe`)
- Modify (mechanical): `DialogEditor.Tests/ViewModels/TextTagValidationViewModelTests.cs`, `DialogEditor.Tests/Views/TextTagValidationWindowTests.cs` — every `dupScan: o => …` / `dupScan: _ => …` becomes `dupScan: (o, _) => …` / `dupScan: (_, _) => …`
- Test: `DialogEditor.Tests/ViewModels/TextTagValidationViewModelTests.cs`

**Interfaces:**
- Consumes: `DuplicateScanOptions.IncludeBaseGame`, `LineRef.FromBaseGame` (Task 3); `VanillaLine` (Task 2).
- Produces:
  ```csharp
  // constructor parameter type change:
  Func<DuplicateScanOptions, IReadOnlyList<VanillaLine>?, DuplicateLineReport>? dupScan = null
  // new trailing constructor parameter:
  Func<CancellationToken, Task<IReadOnlyList<VanillaLine>>>? loadVanilla = null
  public bool CanCompareBaseGame { get; }
  [ObservableProperty] bool IncludeBaseGame;     // part of DuplicateOptions, persisted
  [ObservableProperty] bool IsLoadingBaseGame;
  [ObservableProperty] bool BaseGameLoadFailed;
  public void Cancel();                           // called by the window on close
  internal Task BaseGameScanTask { get; }         // tests await it
  ```

- [ ] **Step 1: Mechanical signature update of existing call sites**

In `TextTagValidationViewModelTests.cs` and `TextTagValidationWindowTests.cs`, change every `dupScan:` lambda to take two parameters: `dupScan: _ =>` → `dupScan: (_, _) =>`, `dupScan: o =>` → `dupScan: (o, _) =>`. In `TextTagValidationViewModelTests.cs` also check line ~114 (`dupScan: _ => ignored is null ? …`) and line ~317. Do not change anything else in those tests.

- [ ] **Step 2: Write the failing tests**

Append to `TextTagValidationViewModelTests.cs` (inside the class):

```csharp
    // ── Base game (issue #14, cross-vanilla) ────────────────────────────────

    private static readonly IReadOnlyList<VanillaLine> OneVanilla =
        [new VanillaLine("v", 1, "the wind howls through the rigging tonight", false)];

    private static DuplicateLineReport OneBaseGameExact() => new(
        [new ExactDuplicateGroup("k", "text",
            [new LineRef("mine", 1, "text"), new LineRef("v", 7, "text", FromBaseGame: true)])],
        []);

    [Fact]
    public void CanCompareBaseGame_FalseWithoutLoader()
    {
        var vm = new TextTagValidationViewModel(scan: () => [], dupScan: (_, _) => new([], []));
        Assert.False(vm.CanCompareBaseGame);
    }

    [Fact]
    public async Task IncludeBaseGame_LoadsOnce_ThenReusesCorpus()
    {
        var loads = 0;
        var seen  = new List<IReadOnlyList<VanillaLine>?>();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: (_, v) => { seen.Add(v); return new([], []); },
            loadVanilla: _ => { loads++; return Task.FromResult(OneVanilla); });
        Assert.True(vm.CanCompareBaseGame);

        vm.IncludeBaseGame = true;
        await vm.BaseGameScanTask;
        vm.NearThreshold = 0.90;
        await vm.BaseGameScanTask;
        vm.RefreshCommand.Execute(null);
        await vm.BaseGameScanTask;

        Assert.Equal(1, loads);
        Assert.Same(OneVanilla, seen[^1]);
    }

    [Fact]
    public async Task IncludeBaseGame_Off_PassesNoCorpus_AndNeverLoads()
    {
        var loads = 0;
        var seen  = new List<IReadOnlyList<VanillaLine>?>();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: (_, v) => { seen.Add(v); return new([], []); },
            loadVanilla: _ => { loads++; return Task.FromResult(OneVanilla); });

        await vm.BaseGameScanTask;

        Assert.Equal(0, loads);
        Assert.All(seen, v => Assert.Null(v));
    }

    [Fact]
    public async Task IsLoadingBaseGame_TrueDuringLoad_FalseAfter()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<VanillaLine>>();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: (_, _) => OneBaseGameExact(),
            loadVanilla: _ => gate.Task);

        vm.IncludeBaseGame = true;
        Assert.True(vm.IsLoadingBaseGame);

        gate.SetResult(OneVanilla);
        await vm.BaseGameScanTask;

        Assert.False(vm.IsLoadingBaseGame);
        Assert.True(vm.HasDuplicates);
    }

    [Fact] // A scan superseded by a newer option change must not overwrite its rows.
    public async Task SupersededScan_ResultDiscarded()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<VanillaLine>>();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: (o, v) => v is null ? new([], []) : OneBaseGameExact(),
            loadVanilla: _ => gate.Task);

        vm.IncludeBaseGame = true;      // generation 1: waiting on the load
        var first = vm.BaseGameScanTask;
        vm.IncludeBaseGame = false;     // generation 2: synchronous, writer-only, empty

        gate.SetResult(OneVanilla);
        await first;

        Assert.False(vm.HasDuplicates);
        Assert.False(vm.IsLoadingBaseGame);
    }

    [Fact]
    public async Task LoadFailure_FlagsIt_ShowsWriterRows_AndRetries()
    {
        var attempt = 0;
        var writerOnly = OneExact();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: (_, v) => v is null ? writerOnly : OneBaseGameExact(),
            loadVanilla: _ => ++attempt == 1
                ? Task.FromException<IReadOnlyList<VanillaLine>>(new IOException("disk"))
                : Task.FromResult(OneVanilla));

        vm.IncludeBaseGame = true;
        await vm.BaseGameScanTask;

        Assert.True(vm.BaseGameLoadFailed);
        Assert.True(vm.HasDuplicates);            // writer-only rows still shown
        Assert.False(vm.IsLoadingBaseGame);

        vm.RefreshCommand.Execute(null);          // retry
        await vm.BaseGameScanTask;

        Assert.Equal(2, attempt);
        Assert.False(vm.BaseGameLoadFailed);
    }

    [Fact] // Closing the window cancels the load; cancellation is silent.
    public async Task Cancel_DuringLoad_IsSilent()
    {
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: (_, _) => new([], []),
            loadVanilla: ct => Task.Delay(Timeout.Infinite, ct)
                .ContinueWith<IReadOnlyList<VanillaLine>>(_ => OneVanilla, ct));

        vm.IncludeBaseGame = true;
        vm.Cancel();
        await vm.BaseGameScanTask;               // must not throw

        Assert.False(vm.BaseGameLoadFailed);
    }

    [Fact]
    public void IncludeBaseGame_RoundTripsThroughOptions()
    {
        DuplicateScanOptions? persisted = null;
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: (_, _) => new([], []),
            duplicateOptions: new DuplicateScanOptions(IncludeBaseGame: false),
            persistDuplicateOptions: o => persisted = o);

        vm.IncludeBaseGame = true;               // no loader: stays on the sync path

        Assert.True(persisted!.IncludeBaseGame);
        Assert.True(vm.DuplicateOptions.IncludeBaseGame);
    }

    [Fact] // Base-game members are labelled so the writer can tell which line is theirs.
    public void BaseGameMember_LabelledInLocations()
    {
        // The plain stub echoes "Duplicate_Source" with no placeholders, which would hide
        // the inner marker; give that one key its real shape.
        Loc.Configure(new EchoStringProvider(new() { ["Duplicate_Source"] = "{0} [{1}]" }));
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: (_, _) => OneBaseGameExact());

        Assert.Contains("[Duplicate_Source_BaseGame]", vm.DuplicateRows[0].Locations);
    }
```

`OneExact()` is the existing helper near the top of the test class; `Locations` is the existing `DuplicateRowViewModel` property (already asserted on at the end of this test file).

Also create the helper `DialogEditor.Tests/Helpers/EchoStringProvider.cs` (used here and in Task 5):

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Helpers;

/// Like StubStringProvider (every key echoes itself), except for the keys given real
/// values — for tests that must see through a composing format such as "{0} [{1}]".
public sealed class EchoStringProvider(Dictionary<string, string> overrides) : IStringProvider
{
    public string Get(string key) => overrides.TryGetValue(key, out var v) ? v : key;
    public bool TryGet(string key, out string value) { value = Get(key); return true; }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TextTagValidationViewModelTests"`
Expected: build FAILS — the `dupScan` two-parameter lambdas don't match the current `Func<DuplicateScanOptions, DuplicateLineReport>` type, and `loadVanilla`, `CanCompareBaseGame`, `IncludeBaseGame`, etc. don't exist.

- [ ] **Step 4: Implement the ViewModel**

In `TextTagValidationViewModel.cs`:

(a) Fields — change `_dupScan`'s type and add the new state next to it:

```csharp
    private readonly Func<DuplicateScanOptions, IReadOnlyList<VanillaLine>?, DuplicateLineReport>? _dupScan;

    // Base-game comparison (issue #14). The corpus is read once per window and reused for
    // every later option change / Refresh — the game's files do not change under us, and
    // the scanner drops the nodes the project owns at scan time, so edits stay correct.
    private readonly Func<CancellationToken, Task<IReadOnlyList<VanillaLine>>>? _loadVanilla;
    private IReadOnlyList<VanillaLine>? _vanilla;
    private readonly CancellationTokenSource _cts = new();
    // Bumped by every duplicate refresh; an async scan whose generation is stale when it
    // finishes has been superseded (e.g. the threshold changed twice) and is discarded.
    private int _duplicateGeneration;
```

(b) Properties, beside `_includeOtherLanguages`:

```csharp
    [ObservableProperty] private bool _includeBaseGame;
    [ObservableProperty] private bool _isLoadingBaseGame;
    [ObservableProperty] private bool _baseGameLoadFailed;

    /// False when no game folder is loaded — there is nothing to compare against.
    public bool CanCompareBaseGame => _loadVanilla is not null;

    /// The in-flight base-game scan, or a completed task. Exposed for tests to await.
    internal Task BaseGameScanTask { get; private set; } = Task.CompletedTask;
```

and extend `DuplicateOptions`:

```csharp
    public DuplicateScanOptions DuplicateOptions =>
        new(NearThreshold, IncludeFemaleText, IncludeOtherLanguages, IncludeBaseGame);
```

(c) Constructor: change the `dupScan` parameter type to `Func<DuplicateScanOptions, IReadOnlyList<VanillaLine>?, DuplicateLineReport>? dupScan = null`, add a trailing parameter `Func<CancellationToken, Task<IReadOnlyList<VanillaLine>>>? loadVanilla = null`, and in the body (beside the other backing-field assignments, before `Refresh()`):

```csharp
        _includeBaseGame         = initial.IncludeBaseGame;
        _loadVanilla             = loadVanilla;
```

(d) Replace `RefreshDuplicates()` with the split below. `FillDuplicateRows` is the body of the old method's first half, unchanged except that it takes the report; `RefreshIgnoredDuplicates` is the old second half, unchanged.

```csharp
    private void RefreshDuplicates()
    {
        var generation = ++_duplicateGeneration;

        // Toggle on and a game loaded: the corpus load and the scan over it take seconds,
        // so they run off the UI thread. Every other case stays synchronous — exactly the
        // pre-#14 behaviour, which is what the existing tests pin.
        if (IncludeBaseGame && _loadVanilla is not null)
        {
            BaseGameScanTask = RefreshDuplicatesWithBaseGameAsync(generation, DuplicateOptions);
        }
        else
        {
            IsLoadingBaseGame  = false;   // a superseded async scan may have left it set
            BaseGameLoadFailed = false;
            FillDuplicateRows(_dupScan?.Invoke(DuplicateOptions, null));
        }
        RefreshIgnoredDuplicates();
    }

    private async Task RefreshDuplicatesWithBaseGameAsync(int generation, DuplicateScanOptions options)
    {
        IsLoadingBaseGame = true;
        try
        {
            var loadFailed = false;
            if (_vanilla is null)
            {
                try
                {
                    _vanilla = await _loadVanilla!(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Left unloaded on purpose, so the next toggle or Refresh retries.
                    AppLog.Error("Base-game duplicate scan: could not read the game's lines", ex);
                    loadFailed = true;
                }
            }

            var corpus = _vanilla;
            var report = _dupScan is null
                ? null
                : await Task.Run(() => _dupScan(options, corpus), _cts.Token);

            if (generation != _duplicateGeneration) return;   // superseded
            BaseGameLoadFailed = loadFailed;
            FillDuplicateRows(report);
        }
        catch (OperationCanceledException)
        {
            // Window closed mid-scan.
        }
        catch (Exception ex)
        {
            // A fire-and-forget task would otherwise swallow this without a trace.
            AppLog.Error("Base-game duplicate scan failed", ex);
        }
        finally
        {
            if (generation == _duplicateGeneration) IsLoadingBaseGame = false;
        }
    }

    /// Cancels an in-flight base-game load or scan. The window calls this on close.
    public void Cancel() => _cts.Cancel();

    private void FillDuplicateRows(DuplicateLineReport? report)
    {
        DuplicateRows.Clear();
        if (report is not null)
        {
            // ← the two existing foreach loops (report.Exact, report.Near), moved verbatim
        }
        HasDuplicates        = DuplicateRows.Count > 0;
        DuplicateSummaryText = DuplicateRows.Count == 0
            ? Loc.Get("Duplicate_NoIssues")
            : Loc.FormatCount("Duplicate_Summary", DuplicateRows.Count);
    }

    private void RefreshIgnoredDuplicates()
    {
        // ← the existing IgnoredDuplicateRows block, moved verbatim
    }
```

Make sure `using DialogEditor.ViewModels.Services;` is present (for `AppLog`, `VanillaLine`).

(e) Option change hook, beside the other three:

```csharp
    partial void OnIncludeBaseGameChanged(bool value)         => OnDuplicateOptionChanged();
```

(f) `Describe` — replace the source switch so base-game lines get a marker:

```csharp
        var source = (r.FromBaseGame, r.Language.Length > 0, r.IsFemale) switch
        {
            (true,  _,     false) => Loc.Get("Duplicate_Source_BaseGame"),
            (true,  _,     true)  => Loc.Get("Duplicate_Source_BaseGameFemale"),
            (false, false, false) => "",
            (false, false, true)  => Loc.Get("Duplicate_Source_Female"),
            (false, true,  false) => r.Language,
            (false, true,  true)  => Loc.Format("Duplicate_Source_LanguageFemale", r.Language),
        };
```

(Base-game lines are always primary-language, so the language flag is irrelevant for them.)

(g) Add the strings to `DialogEditor.Avalonia/Resources/Strings.axaml`, after `Duplicate_Source_LanguageFemale`:

```xml
    <!-- Shown in place of the language marker when a duplicate member is a line from the
         installed game rather than the project (issue #14). -->
    <sys:String x:Key="Duplicate_Source_BaseGame">base game</sys:String>
    <sys:String x:Key="Duplicate_Source_BaseGameFemale">base game, female</sys:String>
```

- [ ] **Step 5: Compile fix in `MainWindowViewModel`**

At `MainWindowViewModel.cs:571-574`, update the delegate to the new shape (full wiring comes in Task 5):

```csharp
        Func<DuplicateScanOptions, IReadOnlyList<VanillaLine>?, DuplicateLineReport> dupScan = (options, vanilla) =>
            _project is null
                ? new DuplicateLineReport([], [])
                : DuplicateLineScanner.Scan(_project, _provider?.Language ?? "", options, vanilla);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TextTagValidation|FullyQualifiedName~MainWindowViewModelDuplicateTests"`
Expected: PASS — the new tests, every pre-existing VM and window test, and the MainWindow duplicate test.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/Helpers/EchoStringProvider.cs DialogEditor.Tests/ViewModels/TextTagValidationViewModelTests.cs DialogEditor.Tests/Views/TextTagValidationWindowTests.cs
git commit -m "feat(duplicates): opt-in async base-game scan in the Validate Text sweep (#14)"
```

---

### Task 5: Wiring, setting, and the window

**Files:**
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (the `TextTagValidationViewModel` construction, ~lines 569-620)
- Modify: `DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml`, `TextTagValidationWindow.axaml.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelDuplicateTests.cs`, `DialogEditor.Tests/Views/TextTagValidationWindowTests.cs`

**Interfaces:**
- Consumes: `VanillaLineCorpus.Load` (Task 2), the VM surface from Task 4.
- Produces: `AppSettings.DuplicateIncludeBaseGame` (bool, default false); window controls `IncludeBaseGameCheckBox`, `BaseGameProgressBar`.

- [ ] **Step 1: Write the failing tests**

Append to `MainWindowViewModelDuplicateTests.cs` (add `using DialogEditor.Core.Models;` is already there):

```csharp
    [Fact] // End to end: the persisted toggle, the real corpus loader, and the real scanner.
    public async Task BaseGame_ToggleOn_ReportsCopyOfVanillaLine()
    {
        const string line = "the wind howls through the rigging tonight";
        var vanillaConv = new Conversation("vanilla_conv",
            [new ConversationNode(7, false, SpeakerCategory.Npc, "spk", "", [], [], [], "Conversation", "None")],
            new StringTable([new StringEntry(7, line, "")]));

        var vm = MakeVm();
        Inject(vm, "_provider", new FakeGameDataProvider("poe2", "en", vanillaConv));
        InjectProject(vm, DialogProject.Empty("T").WithPatch(
            new ConversationPatch("mine", ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                    { ["en"] = [new NodeTranslation(1, line, "")] }
            }));
        AppSettings.DuplicateIncludeBaseGame = true;

        // See EchoStringProvider: the marker is only visible through a real format.
        Loc.Configure(new EchoStringProvider(new() { ["Duplicate_Source"] = "{0} [{1}]" }));

        var sweep = await vm.RequestTextTagValidationAsync();
        Assert.NotNull(sweep);
        Assert.True(sweep!.CanCompareBaseGame);
        await sweep.BaseGameScanTask;

        Assert.True(sweep.HasDuplicates);
        Assert.Contains("[Duplicate_Source_BaseGame]", sweep.DuplicateRows[0].Locations);
    }

    [Fact]
    public async Task BaseGame_NoProvider_CannotCompare()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T").WithPatch(DupPatch()));

        var sweep = await vm.RequestTextTagValidationAsync();

        Assert.False(sweep!.CanCompareBaseGame);
    }

    [Fact]
    public async Task BaseGame_TogglePersistsToSettings()
    {
        var vm = MakeVm();
        Inject(vm, "_provider", new FakeGameDataProvider("poe2", "en"));
        InjectProject(vm, DialogProject.Empty("T").WithPatch(DupPatch()));

        var sweep = await vm.RequestTextTagValidationAsync();
        sweep!.IncludeBaseGame = true;
        await sweep.BaseGameScanTask;

        Assert.True(AppSettings.DuplicateIncludeBaseGame);
    }
```

`EchoStringProvider` is the helper created in Task 4. If `BaseGame_NoProvider_CannotCompare` returns `null` from `RequestTextTagValidationAsync` because it requires a provider, check the method's guard at `MainWindowViewModel.cs:539-541` — it only requires `_project` — and keep the test as written.

Append to `TextTagValidationWindowTests.cs`:

```csharp
    [AvaloniaFact]
    public void Window_BaseGameCheckBox_HasTooltip_AndIsDisabledWithoutLoader()
    {
        var vm = new TextTagValidationViewModel(scan: () => [], dupScan: (_, _) => new([], []));
        var window = new TextTagValidationWindow(vm);
        window.Show();

        var box = window.FindControl<CheckBox>("IncludeBaseGameCheckBox");
        Assert.NotNull(box);
        Assert.False(box!.IsEnabled);
        Assert.NotNull(ToolTip.GetTip(box));
        Assert.NotNull(window.FindControl<ProgressBar>("BaseGameProgressBar"));

        window.Close();
    }

    [AvaloniaFact]
    public void Window_BaseGameCheckBox_EnabledWithLoader()
    {
        var vm = new TextTagValidationViewModel(
            scan: () => [], dupScan: (_, _) => new([], []),
            loadVanilla: _ => Task.FromResult<IReadOnlyList<VanillaLine>>([]));
        var window = new TextTagValidationWindow(vm);
        window.Show();

        Assert.True(window.FindControl<CheckBox>("IncludeBaseGameCheckBox")!.IsEnabled);

        window.Close();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelDuplicateTests|FullyQualifiedName~TextTagValidationWindowTests"`
Expected: build FAILS — `AppSettings.DuplicateIncludeBaseGame` does not exist. (After adding only that, the window tests fail on a null `IncludeBaseGameCheckBox` and the MainWindow test on `CanCompareBaseGame` being false.)

- [ ] **Step 3: Add the setting**

In `AppSettings.cs`, in the settings DTO after `DuplicateIncludeOtherLanguages`:

```csharp
        // Base-game comparison (issue #14). Default false: it reads the whole game, so an
        // upgrading install should not start paying for it unasked.
        public bool DuplicateIncludeBaseGame { get; set; }
```

and the static accessor after `DuplicateIncludeOtherLanguages`:

```csharp
    /// Whether the duplicate sweep also compares against the installed game's lines.
    public static bool DuplicateIncludeBaseGame
    {
        get => Load().DuplicateIncludeBaseGame;
        set { var s = Load(); s.DuplicateIncludeBaseGame = value; Save(s); }
    }
```

- [ ] **Step 4: Wire `MainWindowViewModel`**

Before `return new TextTagValidationViewModel(` add:

```csharp
        // Base-game comparison (issue #14): only offered with a game loaded. The provider
        // is captured now, so a game switch while the window is open cannot change what an
        // in-flight load reads.
        Func<CancellationToken, Task<IReadOnlyList<VanillaLine>>>? loadVanilla = null;
        if (_provider is { } provider)
            loadVanilla = ct => Task.Run(() => VanillaLineCorpus.Load(provider, ct), ct);
```

In the constructor call, change `duplicateOptions` / `persistDuplicateOptions` and add `loadVanilla`:

```csharp
            duplicateOptions: new DuplicateScanOptions(
                AppSettings.NearDuplicateThreshold,
                AppSettings.DuplicateIncludeFemaleText,
                AppSettings.DuplicateIncludeOtherLanguages,
                AppSettings.DuplicateIncludeBaseGame),
            persistDuplicateOptions: o =>
            {
                AppSettings.NearDuplicateThreshold         = o.NearThreshold;
                AppSettings.DuplicateIncludeFemaleText     = o.IncludeFemaleText;
                AppSettings.DuplicateIncludeOtherLanguages = o.IncludeOtherLanguages;
                AppSettings.DuplicateIncludeBaseGame       = o.IncludeBaseGame;
            },
            loadVanilla: loadVanilla);
```

- [ ] **Step 5: Strings**

In `Strings.axaml`, after `Duplicate_IncludeAllLanguagesTooltip`:

```xml
    <sys:String x:Key="Duplicate_IncludeBaseGame">Base game</sys:String>
    <sys:String x:Key="Duplicate_IncludeBaseGameTooltip">Also check your lines against every line in the installed game, to catch a vanilla line you copied verbatim or only lightly reworded. Lines from the base game are marked "base game" in the report. The first time you turn this on, the editor reads the whole game, which can take a few seconds; later changes reuse what it read. Needs a game folder to be loaded. The choice is saved.</sys:String>
    <sys:String x:Key="Duplicate_LoadingBaseGame">Reading the base game…</sys:String>
    <sys:String x:Key="Duplicate_BaseGameLoadFailed">Couldn't read the base game's lines — only your own lines were compared. Details are in the log.</sys:String>
```

- [ ] **Step 6: Window XAML**

In `TextTagValidationWindow.axaml`, inside the scope-toggles `StackPanel` (Grid.Row="8"), after `IncludeOtherLanguagesCheckBox`, add:

```xml
            <CheckBox x:Name="IncludeBaseGameCheckBox"
                      Content="{DynamicResource Duplicate_IncludeBaseGame}"
                      IsChecked="{Binding IncludeBaseGame}"
                      IsEnabled="{Binding CanCompareBaseGame}"
                      FontSize="{DynamicResource FontSize.Small}"
                      ToolTip.Tip="{DynamicResource Duplicate_IncludeBaseGameTooltip}"
                      ToolTip.ShowOnDisabled="True"
                      AutomationProperties.Name="{DynamicResource Duplicate_IncludeBaseGame}"
                      AutomationProperties.HelpText="{DynamicResource Duplicate_IncludeBaseGameTooltip}"/>
```

`ToolTip.ShowOnDisabled` matters here: with no game loaded, the tooltip is the only thing explaining why the box is greyed out.

Then turn that `StackPanel` into a vertical pair so the status line sits under the toggles without widening the row. Replace the `<StackPanel Grid.Row="8" Orientation="Horizontal" …>` opening/closing with:

```xml
        <!-- Scope toggles (issue #14) plus the base-game status line under them. The
             status line lives here, not in a summary, because DuplicateSummaryText is not
             shown in this window. -->
        <StackPanel Grid.Row="8" Spacing="2" Margin="0,0,0,4">
            <StackPanel Orientation="Horizontal" Spacing="14" HorizontalAlignment="Right">
                <!-- …the three CheckBoxes… -->
            </StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="6" HorizontalAlignment="Right"
                        IsVisible="{Binding IsLoadingBaseGame}">
                <ProgressBar x:Name="BaseGameProgressBar" IsIndeterminate="True"
                             Width="80" MinWidth="80" VerticalAlignment="Center"/>
                <TextBlock Text="{DynamicResource Duplicate_LoadingBaseGame}"
                           Foreground="{DynamicResource Brush.Text.Tertiary}"
                           FontSize="{DynamicResource FontSize.Small}"
                           VerticalAlignment="Center"/>
            </StackPanel>
            <TextBlock Text="{DynamicResource Duplicate_BaseGameLoadFailed}"
                       IsVisible="{Binding BaseGameLoadFailed}"
                       Foreground="{DynamicResource Brush.Text.Primary}"
                       FontSize="{DynamicResource FontSize.Small}"
                       TextWrapping="Wrap" HorizontalAlignment="Right"/>
        </StackPanel>
```

Keep the existing XAML comment about why the toggles have their own row; adjust its first line to say "three toggles". The failure line is text, not colour-only (Layer 2.5 rule).

- [ ] **Step 7: Cancel on close**

Replace `TextTagValidationWindow.axaml.cs` with:

```csharp
using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class TextTagValidationWindow : Window
{
    public TextTagValidationWindow() => InitializeComponent();

    public TextTagValidationWindow(TextTagValidationViewModel viewModel) : this()
        => DataContext = viewModel;

    /// A base-game read can take seconds; closing the window must not leave it running
    /// against a VM nobody can see.
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as TextTagValidationViewModel)?.Cancel();
        base.OnClosed(e);
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelDuplicateTests|FullyQualifiedName~TextTagValidationWindowTests|FullyQualifiedName~NoHardcoded|FullyQualifiedName~Localisation"`
Expected: PASS, including the localisation guards (no inline strings in the new XAML/C#).

- [ ] **Step 9: Full test suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS, no new failures.

- [ ] **Step 10: Commit**

```bash
git add DialogEditor.ViewModels/Services/AppSettings.cs DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/MainWindowViewModelDuplicateTests.cs DialogEditor.Tests/Views/TextTagValidationWindowTests.cs
git commit -m "feat(duplicates): Base game toggle in Validate Text, wired to the game folder (#14)"
```

---

### Task 6: End-to-end verification against the real game

**Files:** none changed unless a defect is found (then: red test first, fix, commit).

- [ ] **Step 1: Launch and drive the app** using the `running-the-app` skill against the Deadfire (PoE2) install. Open a project (or create a scratch one) with a new node whose text is a verbatim copy of a known vanilla line, plus one lightly reworded copy of another. Save.
- [ ] **Step 2:** Open **Validate Text…**, tick **Base game**. Screenshot the loading state, then the result.
- [ ] **Step 3: Record timings** — corpus load, and scan at the 0.85 and 0.75 presets (stopwatch the time from ticking to rows appearing; or add a temporary local `Stopwatch` log line that is **not** committed). Report the numbers to the user.
- [ ] **Step 4: Confirm:** the verbatim copy shows as **Exact** with a `… [base game]` member; the reword shows as **Near ~N%**; **Go** jumps to the writer's node; **Ignore** moves it to the ignored pane; the checkbox tooltip shows and the control is found by its UIA Name.
- [ ] **Step 5:** If the 0.75 scan exceeds ~10 s on this machine, stop and report to the user before optimising — the spec's acceptance bar is "seconds-scale".

---

## After the plan

- Update issue #14: tick "Cross-vanilla comparison" with a summary in the style of the other shipped items (what was built, the notable decisions: opt-in, writer-involving only, own-text exclusion, lossless 3-gram prefilter, primary-language vanilla only). Do not paste local paths. The issue then has no open items — close it when the PR merges.
- Open the PR from `feat/cross-vanilla-duplicates`, referencing `Closes #14`.
