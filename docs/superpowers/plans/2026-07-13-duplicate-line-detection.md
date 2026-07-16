# Duplicate / Near-Duplicate Line Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Report exact and near-duplicate lines among the writer's own edited/added dialog, with a content-keyed ignore allowlist, as new panes in the Validate Text… sweep.

**Architecture:** A pure `DuplicateLineScanner` reads the writer's Default text from `DialogProject.Patches[*].Translations[primary]`, groups exact duplicates by normalized text and finds near-duplicates by length-blocked Levenshtein ratio ≥ 0.85, filtering out entries on the project's `IgnoredDuplicates` allowlist. The existing `TextTagValidationViewModel` gains duplicate + ignored panes fed by scan/ignore/unignore delegates wired in `MainWindowViewModel`. Ignores persist in the `.dialogproject` as editor metadata.

**Tech Stack:** C# / .NET, Avalonia, CommunityToolkit.Mvvm, System.Text.Json, xUnit. Spec: `docs/superpowers/specs/2026-07-13-duplicate-line-detection-design.md`.

## Global Constraints

- **TDD:** red → green → refactor; no implementation before a failing test (per `CLAUDE.md`).
- **Localisation:** no user-visible string hard-coded in C#/XAML; every label/tooltip/summary/tier badge is a `{DynamicResource}` / `Loc.Get` / `Loc.FormatCount` key in `Strings.axaml`.
- **Tooltips + automation:** every interactive control carries a detailed `ToolTip.Tip` mirrored into `AutomationProperties.HelpText`; action buttons carry `AutomationProperties.Name` where the glyph/label isn't self-describing. Enforced by `AutomationNameTests` / `AutomationHelpTextTests`.
- **Window icon:** every `<Window>` keeps `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"` (this task edits an existing window; keep it).
- **Error handling:** the scanner is pure and throws nothing user-facing; any production catch logs via `AppLog`. No bare `catch {}`.
- **No stray hex / named colours / static string or fontsize resources:** reuse existing `Brush.*` and `FontSize.*` tokens only (enforced by `NoStrayHexTests`, `NoNamedColourForegroundTests`, `NoStaticStringResourceTests`, `NoStaticFontSizeResourceTests`).
- **Tests run serially** (AppSettings/Loc global state); do not add parallelism.
- **Threshold `0.85`, min words `4`** — hard-coded constants, no UI knob.

---

## File Structure

- Create `DialogEditor.Patch/IgnoredDuplicate.cs` — `DuplicateKind` enum + `IgnoredDuplicate` record.
- Modify `DialogEditor.Patch/DialogProject.cs` — new `IgnoredDuplicates` field + With/Without helpers.
- Create `DialogEditor.ViewModels/Services/DuplicateLineScanner.cs` — report models + the pure scan.
- Modify `DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs` — duplicate + ignored panes.
- Modify `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` — wire the four delegates + navigate.
- Modify `DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml` — two new sections.
- Modify `DialogEditor.Avalonia/Resources/Strings.axaml` — new keys.
- Tests: `DialogEditor.Tests/Patch/IgnoredDuplicateTests.cs`, `DialogEditor.Tests/Services/DuplicateLineScannerTests.cs`, extend `DialogEditor.Tests/ViewModels/TextTagValidationViewModelTests.cs`, `DialogEditor.Tests/ViewModels/MainWindowViewModelDuplicateTests.cs`.

---

## Task 1: `IgnoredDuplicate` type + `DialogProject.IgnoredDuplicates`

**Files:**
- Create: `DialogEditor.Patch/IgnoredDuplicate.cs`
- Modify: `DialogEditor.Patch/DialogProject.cs`
- Test: `DialogEditor.Tests/Patch/IgnoredDuplicateTests.cs`

**Interfaces:**
- Produces:
  - `enum DuplicateKind { Exact, Near }`
  - `record IgnoredDuplicate(DuplicateKind Kind, IReadOnlyList<string> Keys, string DisplayText)`
  - `DialogProject.IgnoredDuplicates : IReadOnlyList<IgnoredDuplicate>?` (new trailing optional ctor param)
  - `DialogProject WithIgnoredDuplicate(IgnoredDuplicate entry)`
  - `DialogProject WithoutIgnoredDuplicate(IgnoredDuplicate entry)`

- [ ] **Step 1: Write the type file**

Create `DialogEditor.Patch/IgnoredDuplicate.cs`:

```csharp
namespace DialogEditor.Patch;

/// Which tier a duplicate finding belongs to.
public enum DuplicateKind { Exact, Near }

/// An entry on the project's duplicate-ignore allowlist. Content-keyed so it
/// survives node renumbering and re-scans:
///   • Exact → Keys = [normalizedText] (one key).
///   • Near  → Keys = the two normalized texts, sorted (two keys).
/// DisplayText is a human-readable label built at ignore time, shown in the
/// Ignored-duplicates pane even after the underlying lines are edited apart.
/// Editor metadata only — never written to game files.
public record IgnoredDuplicate(DuplicateKind Kind, IReadOnlyList<string> Keys, string DisplayText);
```

- [ ] **Step 2: Write the failing tests**

Create `DialogEditor.Tests/Patch/IgnoredDuplicateTests.cs`:

```csharp
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class IgnoredDuplicateTests
{
    private static IgnoredDuplicate Exact(string key) =>
        new(DuplicateKind.Exact, [key], key);

    [Fact]
    public void WithIgnoredDuplicate_AddsEntry()
    {
        var p = DialogProject.Empty("P").WithIgnoredDuplicate(Exact("the wind howls tonight"));
        Assert.Single(p.IgnoredDuplicates!);
        Assert.Equal("the wind howls tonight", p.IgnoredDuplicates![0].Keys[0]);
    }

    [Fact]
    public void WithIgnoredDuplicate_DedupesOnKindAndKeys()
    {
        var p = DialogProject.Empty("P")
            .WithIgnoredDuplicate(Exact("same"))
            .WithIgnoredDuplicate(Exact("same"));
        Assert.Single(p.IgnoredDuplicates!);
    }

    [Fact]
    public void WithoutIgnoredDuplicate_RemovesMatching_AndNullsWhenEmpty()
    {
        var p = DialogProject.Empty("P").WithIgnoredDuplicate(Exact("gone"));
        var p2 = p.WithoutIgnoredDuplicate(Exact("gone"));
        Assert.Null(p2.IgnoredDuplicates);
    }

    [Fact]
    public void Serialization_RoundTrips_IgnoredDuplicates()
    {
        var near = new IgnoredDuplicate(DuplicateKind.Near, ["line a text here", "line b text here"], "«a» ~ «b»");
        var p = DialogProject.Empty("P").WithIgnoredDuplicate(near);

        var json = DialogProjectSerializer.Serialize(p);
        var back = DialogProjectSerializer.Deserialize(json);

        var entry = Assert.Single(back.IgnoredDuplicates!);
        Assert.Equal(DuplicateKind.Near, entry.Kind);
        Assert.Equal(["line a text here", "line b text here"], entry.Keys);
        Assert.Equal("«a» ~ «b»", entry.DisplayText);
    }

    [Fact]
    public void Deserialize_OldFileWithoutField_LoadsAsNull()
    {
        // A project JSON with no IgnoredDuplicates property (back-compat).
        var json = DialogProjectSerializer.Serialize(DialogProject.Empty("P"));
        Assert.DoesNotContain("IgnoredDuplicates", DialogProjectSerializer.Serialize(
            DialogProject.Empty("P") with { }));   // sanity: empty project may still emit null
        var back = DialogProjectSerializer.Deserialize(json);
        Assert.Null(back.IgnoredDuplicates);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~IgnoredDuplicateTests`
Expected: FAIL — `WithIgnoredDuplicate` / `IgnoredDuplicates` do not exist.

- [ ] **Step 4: Add the field + helpers to `DialogProject`**

In `DialogEditor.Patch/DialogProject.cs`, add a trailing optional param to the record header (after `Annotations`):

```csharp
public record DialogProject(
    string Name,
    int SchemaVersion,
    IReadOnlyDictionary<string, ConversationPatch> Patches,
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, LayoutPoint>>? Layouts = null,
    IReadOnlyList<string>? NewConversations = null,
    IReadOnlyDictionary<string, IReadOnlyList<AnnotationSnapshot>>? Annotations = null,
    // Duplicate-line ignore allowlist — editor metadata, never in game files.
    // Nullable for back-compat with projects saved before this field existed.
    IReadOnlyList<IgnoredDuplicate>? IgnoredDuplicates = null)
{
```

Then add the two helpers (place them next to `WithAnnotations`):

```csharp
public DialogProject WithIgnoredDuplicate(IgnoredDuplicate entry)
{
    var existing = IgnoredDuplicates ?? [];
    if (existing.Any(e => e.Kind == entry.Kind && e.Keys.SequenceEqual(entry.Keys)))
        return this;
    return this with { IgnoredDuplicates = [.. existing, entry] };
}

public DialogProject WithoutIgnoredDuplicate(IgnoredDuplicate entry)
{
    if (IgnoredDuplicates is null) return this;
    var kept = IgnoredDuplicates
        .Where(e => !(e.Kind == entry.Kind && e.Keys.SequenceEqual(entry.Keys)))
        .ToList();
    return this with { IgnoredDuplicates = kept.Count > 0 ? kept : null };
}
```

`DialogProject.cs` already has `using System.Linq;` semantics via the existing LINQ in `MergeWith`; if the file lacks a `using System.Linq;` at the top, add it.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~IgnoredDuplicateTests`
Expected: PASS (5). If the `Deserialize_OldFileWithoutField` assertion about the emitted JSON is brittle (the serializer emits `"IgnoredDuplicates": null` with `DefaultIgnoreCondition = Never`), simplify that test to only assert the round-trip loads null — delete the `Assert.DoesNotContain` line. The back-compat guarantee is: a JSON *missing* the property deserializes to null, which System.Text.Json satisfies via the optional ctor param default.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Patch/IgnoredDuplicate.cs DialogEditor.Patch/DialogProject.cs \
        DialogEditor.Tests/Patch/IgnoredDuplicateTests.cs
git commit -m "feat(duplicates): IgnoredDuplicate type + DialogProject allowlist field"
```

---

## Task 2: `DuplicateLineScanner`

**Files:**
- Create: `DialogEditor.ViewModels/Services/DuplicateLineScanner.cs`
- Test: `DialogEditor.Tests/Services/DuplicateLineScannerTests.cs`

**Interfaces:**
- Consumes: `DialogProject` (`.Patches`, `.IgnoredDuplicates`), `ConversationPatch` (`.Translations`, `.AddedNodes`, `.IsEmpty`), `IgnoredDuplicate` / `DuplicateKind` (Task 1).
- Produces:
  - `record LineRef(string ConversationName, int NodeId, string Text)`
  - `record ExactDuplicateGroup(string Key, string SampleText, IReadOnlyList<LineRef> Members)`
  - `record NearDuplicatePair(IReadOnlyList<string> Key, LineRef A, LineRef B, int SimilarityPercent)`
  - `record DuplicateLineReport(IReadOnlyList<ExactDuplicateGroup> Exact, IReadOnlyList<NearDuplicatePair> Near)`
  - `static DuplicateLineReport DuplicateLineScanner.Scan(DialogProject project, string primaryLanguage)`

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Services/DuplicateLineScannerTests.cs`:

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class DuplicateLineScannerTests
{
    private const string L = "the wind howls through the rigging tonight"; // 7 words

    private static ConversationPatch PatchWith(string conv, params (int Id, string Text)[] lines)
    {
        var entries = lines.Select(l => new NodeTranslation(l.Id, l.Text, "")).ToList();
        return new ConversationPatch(conv, ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>> { ["en"] = entries }
        };
    }

    private static DialogProject Project(params ConversationPatch[] patches)
    {
        var p = DialogProject.Empty("P");
        foreach (var patch in patches) p = p.WithPatch(patch);
        return p;
    }

    [Fact] // Exact across two conversations; case/whitespace normalized
    public void Exact_AcrossConversations_Grouped()
    {
        var project = Project(
            PatchWith("c1", (1, L)),
            PatchWith("c2", (2, "  THE WIND   howls through the rigging tonight ")));

        var report = DuplicateLineScanner.Scan(project, "en");

        var group = Assert.Single(report.Exact);
        Assert.Equal(2, group.Members.Count);
        Assert.Empty(report.Near);
    }

    [Fact] // One-word change stays above 0.85 → near pair
    public void Near_OneWordChange_Flagged()
    {
        var project = Project(PatchWith("c1",
            (1, L),
            (2, "the wind howls through the rigging today")));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
        var pair = Assert.Single(report.Near);
        Assert.True(pair.SimilarityPercent >= 85);
    }

    [Fact] // Clearly different long lines → nothing
    public void Near_BelowThreshold_NotFlagged()
    {
        var project = Project(PatchWith("c1",
            (1, "the wind howls through the rigging tonight"),
            (2, "a merchant counts his coins beneath the lantern")));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // Fewer than 4 words → excluded from both tiers
    public void ShortLines_Excluded()
    {
        var project = Project(PatchWith("c1", (1, "hello there friend"), (2, "hello there friend")));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // A candidate in an exact cluster is never also a near pair
    public void ExactMembers_NotAlsoNear()
    {
        var project = Project(PatchWith("c1",
            (1, L),
            (2, L),
            (3, "the wind howls through the rigging today")));  // near to L, but L is an exact cluster

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Single(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // Ignored exact key filtered from the active report
    public void IgnoredExact_Filtered()
    {
        var norm = "the wind howls through the rigging tonight";
        var project = Project(PatchWith("c1", (1, L), (2, L)))
            .WithIgnoredDuplicate(new IgnoredDuplicate(DuplicateKind.Exact, [norm], norm));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
    }

    [Fact] // Ignored near key-pair filtered
    public void IgnoredNear_Filtered()
    {
        var a = "the wind howls through the rigging tonight";
        var b = "the wind howls through the rigging today";
        var keys = new[] { a, b }.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var project = Project(PatchWith("c1", (1, a), (2, b)))
            .WithIgnoredDuplicate(new IgnoredDuplicate(DuplicateKind.Near, keys, "«a» ~ «b»"));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Near);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~DuplicateLineScannerTests`
Expected: FAIL — `DuplicateLineScanner` does not exist.

- [ ] **Step 3: Implement the scanner**

Create `DialogEditor.ViewModels/Services/DuplicateLineScanner.cs`:

```csharp
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

    private static string NearKey(string a, string b) => a + " " + b;   // a,b already sorted

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
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~DuplicateLineScannerTests`
Expected: PASS (7).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/DuplicateLineScanner.cs \
        DialogEditor.Tests/Services/DuplicateLineScannerTests.cs
git commit -m "feat(duplicates): DuplicateLineScanner (exact + near tiers, ignore filter)"
```

---

## Task 3: `TextTagValidationViewModel` duplicate + ignored panes

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/TextTagValidationViewModelTests.cs` (extend)

**Interfaces:**
- Consumes: `DuplicateLineReport` / `ExactDuplicateGroup` / `NearDuplicatePair` / `LineRef` (Task 2); `IgnoredDuplicate` / `DuplicateKind` (Task 1); `Loc`.
- Produces (new members on `TextTagValidationViewModel`, all additive):
  - ctor optional params `Func<DuplicateLineReport>? dupScan = null, Func<IReadOnlyList<IgnoredDuplicate>>? ignoredList = null, Action<IgnoredDuplicate>? ignore = null, Action<IgnoredDuplicate>? unignore = null, Action<string,int>? navigate = null`
  - `ObservableCollection<DuplicateRowViewModel> DuplicateRows`
  - `ObservableCollection<IgnoredDuplicateRowViewModel> IgnoredDuplicateRows`
  - `bool HasDuplicates`, `string DuplicateSummaryText`, `bool HasIgnoredDuplicates`, `string IgnoredSummaryText`
  - `DuplicateRowViewModel` with `TierLabel`, `Text`, `Locations`, `NavigateCommand`, `IgnoreCommand`
  - `IgnoredDuplicateRowViewModel` with `TierLabel`, `DisplayText`, `RestoreCommand`

- [ ] **Step 1: Write the failing tests (extend the existing file)**

Add to `DialogEditor.Tests/ViewModels/TextTagValidationViewModelTests.cs` (keep existing tests; add these + any missing `using`s: `DialogEditor.Patch;`, `DialogEditor.ViewModels.Services;`):

```csharp
    private static LineRef Ref(string conv, int id, string text) => new(conv, id, text);

    private static DuplicateLineReport OneExact() =>
        new([new ExactDuplicateGroup("the wind howls through the rigging tonight",
                "The wind howls through the rigging tonight",
                [Ref("c1", 1, "The wind howls through the rigging tonight"),
                 Ref("c2", 2, "the wind howls through the rigging tonight")])],
            []);

    [Fact]
    public void DupScan_PopulatesDuplicateRows()
    {
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: OneExact);

        Assert.True(vm.HasDuplicates);
        var row = Assert.Single(vm.DuplicateRows);
        Assert.Contains("wind howls", row.Text);
    }

    [Fact]
    public void IgnoreCommand_CallsDelegate_AndRefreshes()
    {
        IgnoredDuplicate? ignored = null;
        var report = OneExact();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: () => ignored is null ? report : new DuplicateLineReport([], []),
            ignore: e => ignored = e);

        vm.DuplicateRows[0].IgnoreCommand.Execute(null);

        Assert.NotNull(ignored);
        Assert.Equal(DuplicateKind.Exact, ignored!.Kind);
        Assert.False(vm.HasDuplicates);   // re-scanned; delegate now filters it out
    }

    [Fact]
    public void IgnoredList_PopulatesPane_AndRestoreCallsDelegate()
    {
        IgnoredDuplicate? restored = null;
        var entry = new IgnoredDuplicate(DuplicateKind.Exact, ["k"], "the ignored line here");
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            ignoredList: () => [entry],
            unignore: e => restored = e);

        Assert.True(vm.HasIgnoredDuplicates);
        var row = Assert.Single(vm.IgnoredDuplicateRows);
        Assert.Equal("the ignored line here", row.DisplayText);

        row.RestoreCommand.Execute(null);
        Assert.Equal(entry, restored);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~TextTagValidationViewModelTests`
Expected: FAIL — `dupScan` param / `DuplicateRows` don't exist.

- [ ] **Step 3: Add the row VMs and pane logic**

In `DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs`:

(a) add `using DialogEditor.Patch;` and `using DialogEditor.ViewModels.Services;` if not present (the latter is already used for `TextTagIssueRow`).

(b) add two row VM classes at the top of the file (beside `TextTagRowViewModel`):

```csharp
/// One row in the Duplicate-lines pane: an exact group or a near pair.
public sealed partial class DuplicateRowViewModel : ObservableObject
{
    private readonly Action _navigate;
    private readonly Action _ignore;

    public string TierLabel { get; }
    public string Text      { get; }
    public string Locations { get; }

    public DuplicateRowViewModel(string tierLabel, string text, string locations,
        Action navigate, Action ignore)
    {
        TierLabel = tierLabel;
        Text      = text;
        Locations = locations;
        _navigate = navigate;
        _ignore   = ignore;
    }

    [RelayCommand] private void Navigate() => _navigate();
    [RelayCommand] private void Ignore()   => _ignore();
}

/// One row in the Ignored-duplicates pane.
public sealed partial class IgnoredDuplicateRowViewModel : ObservableObject
{
    private readonly Action _restore;

    public string TierLabel   { get; }
    public string DisplayText { get; }

    public IgnoredDuplicateRowViewModel(string tierLabel, string displayText, Action restore)
    {
        TierLabel   = tierLabel;
        DisplayText = displayText;
        _restore    = restore;
    }

    [RelayCommand] private void Restore() => _restore();
}
```

(c) add fields + collections + observable props to `TextTagValidationViewModel`:

```csharp
    private readonly Func<DuplicateLineReport>? _dupScan;
    private readonly Func<IReadOnlyList<IgnoredDuplicate>>? _ignoredList;
    private readonly Action<IgnoredDuplicate>? _ignore;
    private readonly Action<IgnoredDuplicate>? _unignore;
    private readonly Action<string, int>? _navigate;

    public ObservableCollection<DuplicateRowViewModel> DuplicateRows { get; } = [];
    public ObservableCollection<IgnoredDuplicateRowViewModel> IgnoredDuplicateRows { get; } = [];

    [ObservableProperty] private bool   _hasDuplicates;
    [ObservableProperty] private string _duplicateSummaryText = string.Empty;
    [ObservableProperty] private bool   _hasIgnoredDuplicates;
    [ObservableProperty] private string _ignoredSummaryText = string.Empty;
```

(d) extend the constructor signature (append the five optional params) and assign the fields:

```csharp
    public TextTagValidationViewModel(
        Func<IReadOnlyList<TextTagIssueRow>> scan,
        Action<string>? addWord = null,
        Func<bool, IReadOnlyList<StaleDataRow>>? staleScan = null,
        Action<IReadOnlyList<StaleDataRow>>? prune = null,
        bool canCheckGameFiles = false,
        string primaryLanguage = "",
        Func<DuplicateLineReport>? dupScan = null,
        Func<IReadOnlyList<IgnoredDuplicate>>? ignoredList = null,
        Action<IgnoredDuplicate>? ignore = null,
        Action<IgnoredDuplicate>? unignore = null,
        Action<string, int>? navigate = null)
    {
        _scan             = scan;
        _addWord          = addWord;
        _staleScan        = staleScan;
        _prune            = prune;
        CanCheckGameFiles = canCheckGameFiles;
        _primaryLanguage  = primaryLanguage;
        _dupScan          = dupScan;
        _ignoredList      = ignoredList;
        _ignore           = ignore;
        _unignore         = unignore;
        _navigate         = navigate;

        CleanUpStaleCommand        = new RelayCommand(() => IsStaleCleanUpArmed = true,
                                                      () => HasStaleData && ConfirmedCount > 0 && !IsStaleCleanUpArmed);
        ConfirmCleanUpStaleCommand = new RelayCommand(ExecuteStaleCleanUp, () => IsStaleCleanUpArmed);
        CancelCleanUpStaleCommand  = new RelayCommand(() => IsStaleCleanUpArmed = false, () => IsStaleCleanUpArmed);

        Refresh();
    }
```

(e) call `RefreshDuplicates()` from `Refresh()` (add the line at the end of `Refresh`, after `RefreshStale();`):

```csharp
        RefreshStale();
        RefreshDuplicates();
```

(f) add `RefreshDuplicates`:

```csharp
    private void RefreshDuplicates()
    {
        DuplicateRows.Clear();
        if (_dupScan is not null)
        {
            var report = _dupScan();

            foreach (var g in report.Exact)
            {
                var entry     = new IgnoredDuplicate(DuplicateKind.Exact, [g.Key], g.SampleText);
                var primary   = g.Members[0];
                var locations = string.Join(", ", g.Members.Select(
                    m => Loc.Format("Duplicate_Location", m.ConversationName, m.NodeId)));
                DuplicateRows.Add(new DuplicateRowViewModel(
                    Loc.Get("Duplicate_Tier_Exact"), g.SampleText, locations,
                    () => _navigate?.Invoke(primary.ConversationName, primary.NodeId),
                    () => { _ignore?.Invoke(entry); Refresh(); }));
            }

            foreach (var p in report.Near)
            {
                var display   = Loc.Format("Duplicate_NearDisplay", p.A.Text, p.B.Text);
                var entry     = new IgnoredDuplicate(DuplicateKind.Near, p.Key, display);
                var locations = Loc.Format("Duplicate_Location", p.A.ConversationName, p.A.NodeId)
                              + ", " + Loc.Format("Duplicate_Location", p.B.ConversationName, p.B.NodeId);
                DuplicateRows.Add(new DuplicateRowViewModel(
                    Loc.Format("Duplicate_Tier_Near", p.SimilarityPercent), display, locations,
                    () => _navigate?.Invoke(p.A.ConversationName, p.A.NodeId),
                    () => { _ignore?.Invoke(entry); Refresh(); }));
            }
        }
        HasDuplicates        = DuplicateRows.Count > 0;
        DuplicateSummaryText = DuplicateRows.Count == 0
            ? Loc.Get("Duplicate_NoIssues")
            : Loc.FormatCount("Duplicate_Summary", DuplicateRows.Count);

        IgnoredDuplicateRows.Clear();
        if (_ignoredList is not null)
        {
            foreach (var e in _ignoredList())
            {
                var tier = e.Kind == DuplicateKind.Exact
                    ? Loc.Get("Duplicate_Tier_Exact")
                    : Loc.Get("Duplicate_Tier_NearShort");
                IgnoredDuplicateRows.Add(new IgnoredDuplicateRowViewModel(
                    tier, e.DisplayText, () => { _unignore?.Invoke(e); Refresh(); }));
            }
        }
        HasIgnoredDuplicates = IgnoredDuplicateRows.Count > 0;
        IgnoredSummaryText   = IgnoredDuplicateRows.Count == 0
            ? Loc.Get("Duplicate_Ignored_NoIssues")
            : Loc.FormatCount("Duplicate_Ignored_Summary", IgnoredDuplicateRows.Count);
    }
```

- [ ] **Step 4: Add the Loc keys the VM references (so tests using `StubStringProvider` resolve — the stub echoes keys, but `Loc.Format`/`FormatCount` must not throw). Add to `DialogEditor.Avalonia/Resources/Strings.axaml` now (Task 4 adds the rest):**

```xml
    <sys:String x:Key="Duplicate_Tier_Exact">Exact</sys:String>
    <sys:String x:Key="Duplicate_Tier_NearShort">Near</sys:String>
    <sys:String x:Key="Duplicate_Tier_Near">Near ~{0}%</sys:String>
    <sys:String x:Key="Duplicate_Location">{0} · {1}</sys:String>
    <sys:String x:Key="Duplicate_NearDisplay">«{0}» ~ «{1}»</sys:String>
    <sys:String x:Key="Duplicate_NoIssues">No duplicate lines.</sys:String>
    <sys:String x:Key="Duplicate_Summary_One">{0} duplicate</sys:String>
    <sys:String x:Key="Duplicate_Summary_Other">{0} duplicates</sys:String>
    <sys:String x:Key="Duplicate_Ignored_NoIssues">Nothing ignored.</sys:String>
    <sys:String x:Key="Duplicate_Ignored_Summary_One">{0} ignored</sys:String>
    <sys:String x:Key="Duplicate_Ignored_Summary_Other">{0} ignored</sys:String>
```

(The unit tests use `StubStringProvider`, which returns the key or a formatted echo, so exact wording doesn't affect them — but the keys must exist for the app. Note `Duplicate_Tier_Near` and `Duplicate_Location` are `Loc.Format` with args, not `FormatCount`.)

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~TextTagValidationViewModelTests`
Expected: PASS (existing + 3 new).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs \
        DialogEditor.Avalonia/Resources/Strings.axaml \
        DialogEditor.Tests/ViewModels/TextTagValidationViewModelTests.cs
git commit -m "feat(duplicates): duplicate + ignored panes on TextTagValidationViewModel"
```

---

## Task 4: Window sections + `MainWindowViewModel` wiring

**Files:**
- Modify: `DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelDuplicateTests.cs`

**Interfaces:**
- Consumes: `TextTagValidationViewModel` duplicate/ignored members (Task 3); `DuplicateLineScanner.Scan` (Task 2); `DialogProject.WithIgnoredDuplicate`/`WithoutIgnoredDuplicate`/`IgnoredDuplicates` (Task 1); existing `NavigateToFoundNode`, `_project`, `_provider`, `IsModified`.
- Produces: the wired window; an integration test over `RequestTextTagValidationAsync`.

- [ ] **Step 1: Add the remaining strings**

Add to `DialogEditor.Avalonia/Resources/Strings.axaml` (beside the Task-3 keys):

```xml
    <sys:String x:Key="Duplicate_SectionHeader">Duplicate lines</sys:String>
    <sys:String x:Key="Duplicate_Ignored_SectionHeader">Ignored duplicates</sys:String>
    <sys:String x:Key="Button_Ignore">Ignore</sys:String>
    <sys:String x:Key="ToolTip_Duplicate_Ignore">Mark this duplicate as intentional. It moves to the Ignored list and stops being reported (until you restore it).</sys:String>
    <sys:String x:Key="Button_Restore">Restore</sys:String>
    <sys:String x:Key="ToolTip_Duplicate_Restore">Remove this entry from the ignore list. If the lines are still duplicates, they will be reported again.</sys:String>
    <sys:String x:Key="Button_GoToNode">Go</sys:String>
    <sys:String x:Key="ToolTip_Duplicate_GoToNode">Jump to the first node of this duplicate (switches conversation if needed).</sys:String>
```

- [ ] **Step 2: Add the two window sections**

In `DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml`:

(a) grow `RowDefinitions` on the root `Grid` to add four rows before the focus-hint bar. Change:

```xml
    <Grid RowDefinitions="Auto,Auto,Auto,*,Auto,Auto,Auto,Auto" Margin="14,12,14,12">
```

to:

```xml
    <Grid RowDefinitions="Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto" Margin="14,12,14,12">
```

(b) move the focus-hint bar to the last row — change `<shared:FocusHintBar Grid.Row="7" .../>` to `Grid.Row="11"`.

(c) insert these four sections **between** the stale clean-up bar (`Grid.Row="6"`) and the focus-hint bar:

```xml
        <!-- Duplicate lines section header -->
        <TextBlock Grid.Row="7"
                   Text="{DynamicResource Duplicate_SectionHeader}"
                   Foreground="{DynamicResource Brush.Text.Primary}"
                   FontSize="{DynamicResource FontSize.Body}"
                   FontWeight="Bold" Margin="0,4,0,2"/>

        <!-- Duplicate rows -->
        <ScrollViewer Grid.Row="8" MaxHeight="150" Margin="0,0,0,4">
            <Panel>
                <TextBlock Text="{DynamicResource Duplicate_NoIssues}"
                           Foreground="{DynamicResource Brush.Text.Disabled}" FontStyle="Italic"
                           HorizontalAlignment="Center" VerticalAlignment="Center"
                           IsVisible="{Binding !HasDuplicates}"/>
                <ItemsControl ItemsSource="{Binding DuplicateRows}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="vm:DuplicateRowViewModel">
                            <Grid ColumnDefinitions="70,*,Auto,Auto" Margin="0,3">
                                <TextBlock Grid.Column="0" Text="{Binding TierLabel}"
                                           Foreground="{DynamicResource Brush.Text.Tertiary}"
                                           FontSize="{DynamicResource FontSize.Small}" FontWeight="Bold"
                                           VerticalAlignment="Center"/>
                                <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                    <TextBlock Text="{Binding Text}"
                                               Foreground="{DynamicResource Brush.Text.Secondary}"
                                               FontSize="{DynamicResource FontSize.Label}" TextWrapping="Wrap"/>
                                    <TextBlock Text="{Binding Locations}"
                                               Foreground="{DynamicResource Brush.Text.Muted}"
                                               FontSize="{DynamicResource FontSize.Small}" TextWrapping="Wrap"/>
                                </StackPanel>
                                <Button Grid.Column="2" Content="{DynamicResource Button_GoToNode}"
                                        Command="{Binding NavigateCommand}"
                                        FontSize="{DynamicResource FontSize.Small}" Padding="8,2" Margin="6,0,0,0"
                                        VerticalAlignment="Center"
                                        AutomationProperties.Name="{DynamicResource Button_GoToNode}"
                                        ToolTip.Tip="{DynamicResource ToolTip_Duplicate_GoToNode}"
                                        AutomationProperties.HelpText="{DynamicResource ToolTip_Duplicate_GoToNode}"/>
                                <Button Grid.Column="3" Content="{DynamicResource Button_Ignore}"
                                        Command="{Binding IgnoreCommand}"
                                        FontSize="{DynamicResource FontSize.Small}" Padding="8,2" Margin="6,0,0,0"
                                        VerticalAlignment="Center"
                                        ToolTip.Tip="{DynamicResource ToolTip_Duplicate_Ignore}"
                                        AutomationProperties.HelpText="{DynamicResource ToolTip_Duplicate_Ignore}"/>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Panel>
        </ScrollViewer>

        <!-- Ignored duplicates section header (hidden when empty) -->
        <TextBlock Grid.Row="9"
                   Text="{DynamicResource Duplicate_Ignored_SectionHeader}"
                   Foreground="{DynamicResource Brush.Text.Primary}"
                   FontSize="{DynamicResource FontSize.Body}"
                   FontWeight="Bold" Margin="0,4,0,2"
                   IsVisible="{Binding HasIgnoredDuplicates}"/>

        <!-- Ignored duplicate rows -->
        <ScrollViewer Grid.Row="10" MaxHeight="120" Margin="0,0,0,4"
                      IsVisible="{Binding HasIgnoredDuplicates}">
            <ItemsControl ItemsSource="{Binding IgnoredDuplicateRows}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="vm:IgnoredDuplicateRowViewModel">
                        <Grid ColumnDefinitions="70,*,Auto" Margin="0,3">
                            <TextBlock Grid.Column="0" Text="{Binding TierLabel}"
                                       Foreground="{DynamicResource Brush.Text.Tertiary}"
                                       FontSize="{DynamicResource FontSize.Small}" FontWeight="Bold"
                                       VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="1" Text="{Binding DisplayText}"
                                       Foreground="{DynamicResource Brush.Text.Secondary}"
                                       FontSize="{DynamicResource FontSize.Label}" TextWrapping="Wrap"
                                       VerticalAlignment="Center"/>
                            <Button Grid.Column="2" Content="{DynamicResource Button_Restore}"
                                    Command="{Binding RestoreCommand}"
                                    FontSize="{DynamicResource FontSize.Small}" Padding="8,2" Margin="6,0,0,0"
                                    VerticalAlignment="Center"
                                    ToolTip.Tip="{DynamicResource ToolTip_Duplicate_Restore}"
                                    AutomationProperties.HelpText="{DynamicResource ToolTip_Duplicate_Restore}"/>
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
```

(d) bump the window's default height so the new sections fit: change `Height="440"` to `Height="600"` and `MinHeight="300"` to `MinHeight="420"` on the `<Window>` element.

- [ ] **Step 3: Wire the delegates in `MainWindowViewModel.RequestTextTagValidationAsync`**

In `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`, inside `RequestTextTagValidationAsync`, before the `return new TextTagValidationViewModel(...)`, add:

```csharp
        Func<DuplicateLineReport> dupScan = () =>
            _project is null
                ? new DuplicateLineReport([], [])
                : DuplicateLineScanner.Scan(_project, _provider?.Language ?? "");

        Func<IReadOnlyList<IgnoredDuplicate>> ignoredList = () =>
            _project?.IgnoredDuplicates ?? [];

        // Ignore/un-ignore are metadata-only edits: mutate _project directly and mark
        // dirty (persists on next save, like annotations) — no SetProject side effects.
        Action<IgnoredDuplicate> ignore = e =>
        {
            if (_project is null) return;
            _project = _project.WithIgnoredDuplicate(e);
            IsModified = true;
        };
        Action<IgnoredDuplicate> unignore = e =>
        {
            if (_project is null) return;
            _project = _project.WithoutIgnoredDuplicate(e);
            IsModified = true;
        };
```

and extend the constructor call to pass them (append after `primaryLanguage:`):

```csharp
        return new TextTagValidationViewModel(
            () => _project is null
                ? []
                : ProjectTextTagScanner.Scan(
                    _project, _activeGameId, _provider?.Language ?? "", spell: spell),
            addWord: store is null ? null : store.AddWord,
            staleScan: staleScan,
            prune: prune,
            canCheckGameFiles: _provider is not null,
            primaryLanguage: _provider?.Language ?? "",
            dupScan: dupScan,
            ignoredList: ignoredList,
            ignore: ignore,
            unignore: unignore,
            navigate: NavigateToFoundNode);
```

Add `using DialogEditor.ViewModels.Services;` if not already present (it is — `ProjectTextTagScanner` lives there), and the `IgnoredDuplicate` type resolves via the existing `using DialogEditor.Patch;`.

- [ ] **Step 4: Write the integration test**

Create `DialogEditor.Tests/ViewModels/MainWindowViewModelDuplicateTests.cs`:

```csharp
using System.Reflection;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// The duplicate panes wired through MainWindowViewModel.RequestTextTagValidationAsync.
public class MainWindowViewModelDuplicateTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowViewModelDuplicateTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_dup_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void Inject(MainWindowViewModel vm, string field, object value) =>
        typeof(MainWindowViewModel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, value);

    private static void InjectProject(MainWindowViewModel vm, DialogProject project) =>
        typeof(MainWindowViewModel).GetMethod("SetProject", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, [project]);

    private static ConversationPatch DupPatch()
    {
        const string line = "the wind howls through the rigging tonight";
        return new ConversationPatch("c1", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, line, ""), new NodeTranslation(2, line, "")]
            }
        };
    }

    [Fact]
    public async Task DuplicatesSurface_AndIgnorePersistsToProject()
    {
        var vm = MakeVm();
        Inject(vm, "_provider", new FakeGameDataProvider("poe2", "en"));
        InjectProject(vm, DialogProject.Empty("T").WithPatch(DupPatch()));

        var sweep = await vm.RequestTextTagValidationAsync();
        Assert.NotNull(sweep);
        Assert.True(sweep!.HasDuplicates);

        // Ignore the duplicate → project gets the entry, dirty flips, row moves.
        sweep.DuplicateRows[0].IgnoreCommand.Execute(null);

        Assert.True(vm.IsModified);
        Assert.False(sweep.HasDuplicates);
        Assert.True(sweep.HasIgnoredDuplicates);

        // Restore → active duplicate returns.
        sweep.IgnoredDuplicateRows[0].RestoreCommand.Execute(null);
        Assert.True(sweep.HasDuplicates);
        Assert.False(sweep.HasIgnoredDuplicates);
    }
}
```

- [ ] **Step 5: Run to verify failure then pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~MainWindowViewModelDuplicateTests`
Expected first (before Step 3 compiles): FAIL. After Step 3: PASS (1).

- [ ] **Step 6: Build the app + run the full suite (structural enforcers)**

Run: `dotnet build DialogEditor.Avalonia`  → expect Build succeeded.
Run: `dotnet test DialogEditor.Tests`  → expect all pass, including `AutomationNameTests`, `AutomationHelpTextTests`, `NoStrayHexTests`, `NoNamedColourForegroundTests`, `NoStaticStringResourceTests`, `NoStaticFontSizeResourceTests`, `NoNaivePluralTests`. If `NoNaivePluralTests` flags the `Duplicate_Summary`/`Duplicate_Ignored_Summary` keys, confirm both `_One` and `_Other` variants exist (they do) — the count strings must never use "(s)".

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml \
        DialogEditor.Avalonia/Resources/Strings.axaml \
        DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs \
        DialogEditor.Tests/ViewModels/MainWindowViewModelDuplicateTests.cs
git commit -m "feat(duplicates): Validate Text window panes + MainWindowViewModel wiring"
```

---

## Task 5: Live verification + Gaps.md

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Drive the app end-to-end**

Use the `running-the-app` skill. Arrange a scratch project **with a patch that contains duplicate lines** (write it via the app's serializer in the driver script, or open an existing project), then via `Test ▸ Validate Text…`:
1. The **Duplicate lines** section lists the exact duplicate (and any near pair), with tier labels.
2. **Go** jumps to the node; **Ignore** moves the row to **Ignored duplicates** and it disappears from the active list.
3. **Restore** in the ignored pane brings it back to the active list.
4. Saving the project persists the ignore (reopen → the ignored entry is still ignored).
Screenshot the window with both panes populated.

- [ ] **Step 2: Update `Gaps.md`**

In the **Smaller Writer/UX Backlog** section, replace the Duplicate-detection bullet with a `✓ Implemented (2026-07-13)` entry: two-tier (exact + near ≥0.85, length-blocked) detection over the writer's own edited/added Default text (`DuplicateLineScanner`), surfaced as Duplicate-lines + Ignored-duplicates panes in the Validate Text sweep, with a content-keyed ignore allowlist persisted in the `.dialogproject`. Read-only otherwise. Cite the spec.

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark duplicate line detection implemented"
```

---

## Self-Review

**Spec coverage:**
- My-lines-only candidate set from `patch.Translations[primary]` + defensive AddedNodes → Task 2. ✓
- Default-text-only, short-line (<4 words) suppression → Task 2. ✓
- Exact tier (normalized grouping) + Near tier (Levenshtein ≥0.85, length-blocked) → Task 2. ✓
- Exact-cluster members excluded from near → Task 2 (`exactNorms`). ✓
- Content-keyed ignore (exact = normalized text; near = sorted pair) + persistence in `.dialogproject` → Task 1 + Task 2 filtering. ✓
- Validate Text sweep home, Duplicate + Ignored panes, navigable, Ignore/Restore → Tasks 3–4. ✓
- Ignore marks project dirty, persists on save; live re-scan → Task 4 delegates + Task 3 Refresh. ✓
- Read-only otherwise (no bulk auto-remove) → no such command added. ✓
- Localisation / tooltips / automation → Tasks 3–4 strings. ✓
- Testing (scanner, project round-trip/back-compat, VM panes, wiring integration) → Tasks 1–4. ✓
- Live verification → Task 5. ✓

**Placeholder scan:** none — all code shown in full. Task 1 Step 5 flags one potentially-brittle assertion with a concrete fallback.

**Type consistency:** `DuplicateKind`/`IgnoredDuplicate` (Task 1) used unchanged in Tasks 2–4. `DuplicateLineReport`/`ExactDuplicateGroup(Key, SampleText, Members)`/`NearDuplicatePair(Key, A, B, SimilarityPercent)`/`LineRef` (Task 2) match their use in Task 3's `RefreshDuplicates` and the tests. `TextTagValidationViewModel` new members (`DuplicateRows`, `IgnoredDuplicateRows`, `HasDuplicates`, `HasIgnoredDuplicates`, the row VMs' `NavigateCommand`/`IgnoreCommand`/`RestoreCommand`) are consistent across Task 3 (definition), Task 4 (XAML bindings), and the tests. The constructor's five new trailing optional params match the call site in Task 4 Step 3. `DialogProject.WithIgnoredDuplicate`/`WithoutIgnoredDuplicate`/`IgnoredDuplicates` (Task 1) match Task 4's delegates.

**Deviations from spec (intentional, noted):** navigation is a per-row **Go** button rather than double-click/Enter — the Validate Text window uses a flat `ItemsControl` with command-bound buttons and an empty code-behind, so a button matches the window's established idiom (double-click would need selection + code-behind this window doesn't have). Same navigate target (`NavigateToFoundNode`).
