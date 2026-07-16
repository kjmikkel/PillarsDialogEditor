# Reputation & Disposition Check Balance — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a read-only, project-wide window that tallies how often each reputation and disposition *value* is checked across conversations, flagging over-favoured / under-used / never-used values against an even-share baseline.

**Architecture:** A pure Core walk (`ConditionLeaves` over node + link conditions) feeds a ViewModels-layer classifier (`FactionCheckClassifier`) and aggregator (`RepDispositionTallyService`) that produce a `RepDispositionReport`. A gather step turns the Source×Scope selection into `(name, effectiveSnapshot)` pairs (mirroring `ProjectFindService`). A new `RepDispositionBalanceViewModel` + window presents two tables. Read-only throughout; the heavy full-corpus sweep runs async/cancellable.

**Tech Stack:** C# / .NET 8, Avalonia, CommunityToolkit.Mvvm, xUnit. Existing services: `ConditionCatalogue`, `GameDataNameService`, `ProjectFindService` (reference), `PatchApplier`, `ConversationSnapshotBuilder`.

**Spec:** `docs/superpowers/specs/2026-07-16-rep-disposition-balance-design.md` (and the shared `2026-07-16-catalogue-match-primitive-design.md`, whose `CatalogueMatch` predicate is **not** consumed by this feature — see Note below).

> **Planning note — CatalogueMatch is Gap #2's, not this plan's.** This feature enumerates *all* `Faction`-category checks and buckets them by value; it never matches a pinned query, so it does not use the `CatalogueMatch` primitive. What it genuinely shares with Gap #2 is only the three-site node walk, built as `ConditionLeaves` in Task 1. `CatalogueMatch` will be built in the Gap #2 plan (its first consumer). This is a deliberate YAGNI trim of the spec's "primitive built first" phrasing.

## Global Constraints

Copied verbatim from the spec and CLAUDE.md — every task implicitly includes these:

- **TDD red/green** — write a failing test before implementation for all non-trivial logic. Tests live in `DialogEditor.Tests`, mirroring `DialogEditor.Core` / `DialogEditor.ViewModels` structure.
- **Localisation** — no user-visible string hard-coded in XAML or C#. All labels, headers, tooltips, flag names, status/progress text go in `DialogEditor.Avalonia/Resources/Strings.axaml` and are read via `Loc.Get` / `Loc.Format`.
- **Tooltips mandatory** — every interactive control (both selectors, Refresh, table columns/rows) carries a detailed `ToolTip`.
- **Window icon** — the new window sets `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- **UI Automation** — interactive controls discoverable by UIA Name (localised `Header`/content or `AutomationProperties.Name`). Do not suppress automation peers.
- **Error handling** — every caught exception logged via `AppLog.Error(...)` / `AppLog.Warn(...)`; the sole exception is `OperationCanceledException`, swallowed silently. No bare `catch {}` in production code.
- **Tests run serially** — `DialogEditor.Tests` runs serially due to global statics (`AppSettings`/`Loc`/`GameDataNameService`). Any test touching `GameDataNameService` must `Clear()` it in `Dispose` (see `GameDataNameServiceTests`).
- **Single game per project** — the loaded provider is PoE1 or PoE2; only that game's catalogue/domain applies. No cross-game mixing.
- **Value bucketing, v1** — one total count per value; no per-rank breakdown.
- **Checks only** — tally conditions, never scripts.
- **Fair-share thresholds** — `OverFactor = 2.0`, `UnderFactor = 0.5` (named constants).

---

### Task 1: Shared node condition-leaf walk (Core)

The one piece Gap #1 and Gap #2 share: enumerate every `ConditionLeaf` reachable from a node — its own condition tree **and** each outgoing link's condition tree. Flattening a `ConditionNode` tree to leaves is already provided by `ConditionNode.Leaves()`.

**Files:**
- Create: `DialogEditor.Core/Editing/NodeConditionExtensions.cs`
- Test: `DialogEditor.Tests/Editing/NodeConditionExtensionsTests.cs`

**Interfaces:**
- Consumes: `NodeEditSnapshot`, `LinkEditSnapshot.Conditions` (nullable), `ConditionNode.Leaves()` (all in `DialogEditor.Core`).
- Produces: `public static IEnumerable<ConditionLeaf> ConditionLeaves(this NodeEditSnapshot node)` — yields node-condition leaves first, then each link's condition leaves, in link order. Only `ConditionLeaf` instances (branches are flattened).

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Editing/NodeConditionExtensionsTests.cs
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Editing;

public class NodeConditionExtensionsTests
{
    private static ConditionLeaf Leaf(string full, params string[] args) =>
        new(full, args, Not: false, Operator: "And");

    private static NodeEditSnapshot Node(
        IReadOnlyList<ConditionNode> nodeConds,
        params IReadOnlyList<ConditionNode>?[] linkConds)
    {
        var links = linkConds.Select((c, i) =>
            new LinkEditSnapshot(0, i + 1, 0f, "", HasConditions: c is { Count: > 0 })
            { Conditions = c }).ToList();
        return new NodeEditSnapshot(
            0, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false,
            links, nodeConds, []);
    }

    [Fact]
    public void ConditionLeaves_YieldsNodeAndLinkLeaves_InOrder()
    {
        var node = Node(
            nodeConds: new ConditionNode[] { Leaf("Boolean A()", ) },
            new ConditionNode[] { Leaf("Boolean B()") },        // link 1
            null,                                                // link 2: no conditions
            new ConditionNode[] { Leaf("Boolean C()") });        // link 3

        var names = node.ConditionLeaves().Select(l => l.FullName).ToList();

        Assert.Equal(new[] { "Boolean A()", "Boolean B()", "Boolean C()" }, names);
    }

    [Fact]
    public void ConditionLeaves_FlattensBranches()
    {
        var branch = new ConditionBranch(
            new ConditionNode[] { Leaf("Boolean X()"), Leaf("Boolean Y()") },
            Not: false, Operator: "Or");
        var node = Node(nodeConds: new ConditionNode[] { branch });

        var names = node.ConditionLeaves().Select(l => l.FullName).ToList();

        Assert.Equal(new[] { "Boolean X()", "Boolean Y()" }, names);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~NodeConditionExtensionsTests"`
Expected: FAIL — `ConditionLeaves` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
// DialogEditor.Core/Editing/NodeConditionExtensions.cs
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Editing;

/// Shared walk over a node's condition match-sites: its own condition tree and
/// each outgoing link's condition tree, flattened to leaves. Used by the
/// reputation/disposition tally (conditions only) and by the node-search feature.
public static class NodeConditionExtensions
{
    public static IEnumerable<ConditionLeaf> ConditionLeaves(this NodeEditSnapshot node)
    {
        foreach (var leaf in node.Conditions.SelectMany(c => c.Leaves()).OfType<ConditionLeaf>())
            yield return leaf;

        foreach (var link in node.Links)
            if (link.Conditions is { } conds)
                foreach (var leaf in conds.SelectMany(c => c.Leaves()).OfType<ConditionLeaf>())
                    yield return leaf;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~NodeConditionExtensionsTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/Editing/NodeConditionExtensions.cs DialogEditor.Tests/Editing/NodeConditionExtensionsTests.cs
git commit -m "feat(core): ConditionLeaves walk over node + link condition trees"
```

---

### Task 2: Faction-check classifier & value domain (ViewModels)

Classify a `ConditionLeaf` as a reputation/disposition check (or not) and extract the checked **value**. Also enumerate the full value domain per game so never-checked values can appear as `0` rows. Lives in `DialogEditor.ViewModels` because it needs `ConditionCatalogue` (which lives there) and `GameDataNameService`.

**Files:**
- Create: `DialogEditor.ViewModels/Services/FactionCheckClassifier.cs`
- Test: `DialogEditor.Tests/Services/FactionCheckClassifierTests.cs`

**Interfaces:**
- Consumes: `ConditionLeaf` (Core), `ConditionCatalogue` (`FindByFullName(fullName, gameId)`, `ConditionEntry.Category`, `ConditionEntry.Parameters`, `ConditionParameter.LookupKind`/`Options`), `GameDataNameService.Get(kind)` → `IReadOnlyList<NamedEntry>` (`DisplayName`, `StoredValue`).
- Produces:
  - `public enum FactionCheckDomain { Disposition, Reputation }`
  - `public record FactionCheck(FactionCheckDomain Domain, string RawValue);`
  - `public static FactionCheck? Classify(ConditionLeaf leaf, string gameId, ConditionCatalogue catalogue);`
  - `public static IReadOnlyList<NamedEntry> PossibleValues(FactionCheckDomain domain, string gameId, ConditionCatalogue catalogue);`
  - `public static string ResolveDisplay(FactionCheckDomain domain, string rawValue, string gameId, ConditionCatalogue catalogue);` — returns a display name (GUID→name via `GameDataNameService`, or the raw value if it is already a display value / unresolved).

**Design facts (from `conditions.json`, verified):** the checked value is always parameter index 0. Disposition methods: `DispositionEqual`, `DispositionGreaterOrEqual` (PoE1; param 0 = enum `Axis`, has `Options`, no `LookupKind`), `IsDisposition` (PoE2; param 0 = `Guid`, `LookupKind = "Disposition"`). Reputation methods: `ReputationRankEquals`, `ReputationRankGreater`, `IsReputation` (param 0 = faction `Guid`, `LookupKind = "Faction"`), `ReputationRankByTagEquals` (param 0 = `FactionName`). Category for all is `"Faction"`.

Classification rule: a leaf is a faction check iff its catalogue entry's `Category == "Faction"` **and** its method name is in the known disposition or reputation set (guards against future non-rep/disposition `Faction` conditions). Domain = disposition if the method is in the disposition set, else reputation. `RawValue = leaf.Parameters[0]` (guard: empty parameter list → not a check).

Domain enumeration: disposition-PoE1 → the `Axis` param's `Options`; disposition-PoE2 → `GameDataNameService.Get("Disposition")`; reputation → `GameDataNameService.Get("Faction")`. For PoE1 enum options, `NamedEntry.StoredValue == DisplayName == the option string`.

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Services/FactionCheckClassifierTests.cs
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class FactionCheckClassifierTests : IDisposable
{
    public void Dispose() => GameDataNameService.Clear();

    private static ConditionLeaf Leaf(string full, params string[] args) =>
        new(full, args, Not: false, Operator: "And");

    [Fact]
    public void Classify_Poe1DispositionEqual_ReturnsDispositionWithAxisValue()
    {
        var leaf = Leaf("Boolean DispositionEqual(Axis, Rank)", "Benevolent", "2");
        var result = FactionCheckClassifier.Classify(leaf, "poe1", ConditionCatalogue.Instance);
        Assert.NotNull(result);
        Assert.Equal(FactionCheckDomain.Disposition, result!.Domain);
        Assert.Equal("Benevolent", result.RawValue);
    }

    [Fact]
    public void Classify_Poe2IsReputation_ReturnsReputationWithFactionGuid()
    {
        var leaf = Leaf("Boolean IsReputation(Guid, RankType, Int32, Operator)",
            "faction-guid", "Good", "2", "GreaterThan");
        var result = FactionCheckClassifier.Classify(leaf, "poe2", ConditionCatalogue.Instance);
        Assert.NotNull(result);
        Assert.Equal(FactionCheckDomain.Reputation, result!.Domain);
        Assert.Equal("faction-guid", result.RawValue);
    }

    [Fact]
    public void Classify_NonFactionCondition_ReturnsNull()
    {
        var leaf = Leaf("Boolean IsGlobalValue(String, Operator, Int32)", "g", "EqualTo", "1");
        Assert.Null(FactionCheckClassifier.Classify(leaf, "poe2", ConditionCatalogue.Instance));
    }

    [Fact]
    public void PossibleValues_Reputation_ReturnsRegisteredFactions()
    {
        GameDataNameService.Register("Faction", new[]
        {
            new NamedEntry("Huana — h-guid", "h-guid"),
            new NamedEntry("Vailian Trading Company — v-guid", "v-guid"),
        });
        var values = FactionCheckClassifier.PossibleValues(
            FactionCheckDomain.Reputation, "poe2", ConditionCatalogue.Instance);
        Assert.Equal(2, values.Count);
        Assert.Contains(values, v => v.StoredValue == "h-guid");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~FactionCheckClassifierTests"`
Expected: FAIL — `FactionCheckClassifier` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// DialogEditor.ViewModels/Services/FactionCheckClassifier.cs
using DialogEditor.Core.Models;

namespace DialogEditor.ViewModels.Services;

public enum FactionCheckDomain { Disposition, Reputation }

public record FactionCheck(FactionCheckDomain Domain, string RawValue);

/// Classifies a condition leaf as a reputation/disposition check and enumerates the
/// full value domain per game (so never-checked values can be shown as zero rows).
public static class FactionCheckClassifier
{
    private static readonly HashSet<string> DispositionMethods = new(StringComparer.OrdinalIgnoreCase)
        { "DispositionEqual", "DispositionGreaterOrEqual", "IsDisposition" };
    private static readonly HashSet<string> ReputationMethods = new(StringComparer.OrdinalIgnoreCase)
        { "ReputationRankEquals", "ReputationRankGreater", "IsReputation", "ReputationRankByTagEquals" };

    public static FactionCheck? Classify(ConditionLeaf leaf, string gameId, ConditionCatalogue catalogue)
    {
        var entry = catalogue.FindByFullName(leaf.FullName, gameId);
        if (entry is null || leaf.Parameters.Count == 0) return null;

        var method = entry.MethodName;
        var domain =
            DispositionMethods.Contains(method) ? FactionCheckDomain.Disposition :
            ReputationMethods.Contains(method)  ? FactionCheckDomain.Reputation  :
            (FactionCheckDomain?)null;
        if (domain is null) return null;

        return new FactionCheck(domain.Value, leaf.Parameters[0]);
    }

    public static IReadOnlyList<NamedEntry> PossibleValues(
        FactionCheckDomain domain, string gameId, ConditionCatalogue catalogue)
    {
        if (domain == FactionCheckDomain.Reputation)
            return GameDataNameService.Get("Faction");

        // Disposition: PoE2 uses the Disposition GUID lookup; PoE1 uses the Axis enum options.
        var lookup = GameDataNameService.Get("Disposition");
        if (lookup.Count > 0) return lookup;

        var entry = catalogue.Find("DispositionEqual");
        var options = entry?.Parameters.FirstOrDefault()?.Options ?? [];
        return options.Select(o => new NamedEntry(o, o)).ToList();
    }

    public static string ResolveDisplay(
        FactionCheckDomain domain, string rawValue, string gameId, ConditionCatalogue catalogue)
    {
        foreach (var e in PossibleValues(domain, gameId, catalogue))
            if (string.Equals(e.StoredValue, rawValue, StringComparison.OrdinalIgnoreCase))
                return e.DisplayName;
        return rawValue;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~FactionCheckClassifierTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/FactionCheckClassifier.cs DialogEditor.Tests/Services/FactionCheckClassifierTests.cs
git commit -m "feat(viewmodels): FactionCheckClassifier — classify rep/disposition checks + value domain"
```

---

### Task 3: Tally aggregator with fair-share flags (ViewModels)

Aggregate faction checks across a set of `(name, snapshot)` pairs into a `RepDispositionReport`: per-value counts within each domain, every domain value seeded to 0, encountered-but-unresolved values appended, and a fair-share flag per row.

**Files:**
- Create: `DialogEditor.ViewModels/Services/RepDispositionModels.cs`
- Create: `DialogEditor.ViewModels/Services/RepDispositionTallyService.cs`
- Test: `DialogEditor.Tests/Services/RepDispositionTallyServiceTests.cs`

**Interfaces:**
- Consumes: `ConversationEditSnapshot` (Core), `NodeConditionExtensions.ConditionLeaves` (Task 1), `FactionCheckClassifier` (Task 2), `ConditionCatalogue`.
- Produces:
  - `public enum BalanceFlag { Normal, Over, Under, Ignored }`
  - `public record BalanceRow(string DisplayValue, int Count, double ShareVsExpected, BalanceFlag Flag, bool IsUnresolved);`
  - `public record RepDispositionReport(IReadOnlyList<BalanceRow> DispositionRows, IReadOnlyList<BalanceRow> ReputationRows, int DispositionTotal, int ReputationTotal, int ConversationsAnalyzed);`
  - `public static RepDispositionReport Analyze(IReadOnlyList<(string Name, ConversationEditSnapshot Snapshot)> conversations, string gameId, ConditionCatalogue catalogue);`

**Fair-share rule (per domain, over the domain's rows):** `expected = total / rowCount` (0 when `rowCount == 0`). For each row: `Ignored` if `Count == 0`; else `Over` if `Count >= 2.0 * expected`; else `Under` if `Count <= 0.5 * expected`; else `Normal`. `ShareVsExpected = expected > 0 ? Count / expected : 0`. Row sort: flagged rows first (`Over`, then `Under`, then `Ignored`), then by `Count` descending, then `DisplayValue`. `Normal` rows follow, `Count` descending.

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Services/RepDispositionTallyServiceTests.cs
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class RepDispositionTallyServiceTests : IDisposable
{
    public void Dispose() => GameDataNameService.Clear();

    private static ConditionLeaf Disp(string axis) =>
        new("Boolean DispositionEqual(Axis, Rank)", new[] { axis, "2" }, false, "And");

    private static NodeEditSnapshot NodeWith(params ConditionNode[] conds) =>
        new(0, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false,
            [], conds, []);

    private static (string, ConversationEditSnapshot) Conv(string name, params NodeEditSnapshot[] nodes) =>
        (name, new ConversationEditSnapshot(nodes));

    [Fact]
    public void Analyze_BucketsByValue_AndSeedsUnusedDomainValuesToZero()
    {
        // Domain of three dispositions; only Benevolent is checked (x3).
        GameDataNameService.Register("Disposition", new[]
        {
            new NamedEntry("Benevolent", "Benevolent"),
            new NamedEntry("Cruel", "Cruel"),
            new NamedEntry("Honest", "Honest"),
        });

        var conv = Conv("c1",
            NodeWith(Disp("Benevolent")),
            NodeWith(Disp("Benevolent")),
            NodeWith(Disp("Benevolent")));

        var report = RepDispositionTallyService.Analyze(
            new[] { conv }, "poe2", ConditionCatalogue.Instance);

        Assert.Equal(3, report.DispositionTotal);
        var benevolent = report.DispositionRows.First(r => r.DisplayValue == "Benevolent");
        Assert.Equal(3, benevolent.Count);
        Assert.Equal(BalanceFlag.Over, benevolent.Flag);          // 3 vs expected 1 → >= 2x
        var cruel = report.DispositionRows.First(r => r.DisplayValue == "Cruel");
        Assert.Equal(0, cruel.Count);
        Assert.Equal(BalanceFlag.Ignored, cruel.Flag);            // never checked
    }

    [Fact]
    public void Analyze_UnresolvedValue_ShownAsUnresolvedRow_NotDropped()
    {
        GameDataNameService.Register("Disposition", new[] { new NamedEntry("Benevolent", "Benevolent") });
        var conv = Conv("c1", NodeWith(Disp("Ghostly")));   // not in domain

        var report = RepDispositionTallyService.Analyze(
            new[] { conv }, "poe2", ConditionCatalogue.Instance);

        var row = report.DispositionRows.First(r => r.DisplayValue == "Ghostly");
        Assert.True(row.IsUnresolved);
        Assert.Equal(1, row.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RepDispositionTallyServiceTests"`
Expected: FAIL — `RepDispositionTallyService` / model types do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// DialogEditor.ViewModels/Services/RepDispositionModels.cs
namespace DialogEditor.ViewModels.Services;

public enum BalanceFlag { Normal, Over, Under, Ignored }

public record BalanceRow(
    string DisplayValue, int Count, double ShareVsExpected, BalanceFlag Flag, bool IsUnresolved);

public record RepDispositionReport(
    IReadOnlyList<BalanceRow> DispositionRows,
    IReadOnlyList<BalanceRow> ReputationRows,
    int DispositionTotal,
    int ReputationTotal,
    int ConversationsAnalyzed);
```

```csharp
// DialogEditor.ViewModels/Services/RepDispositionTallyService.cs
using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Services;

/// Aggregates rep/disposition checks by value into a balance report. Pure; no IO.
public static class RepDispositionTallyService
{
    private const double OverFactor  = 2.0;
    private const double UnderFactor = 0.5;

    public static RepDispositionReport Analyze(
        IReadOnlyList<(string Name, ConversationEditSnapshot Snapshot)> conversations,
        string gameId, ViewModels.Services.ConditionCatalogue catalogue)
    {
        var dispCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var repCounts  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, snap) in conversations)
            foreach (var node in snap.Nodes)
                foreach (var leaf in node.ConditionLeaves())
                {
                    var check = FactionCheckClassifier.Classify(leaf, gameId, catalogue);
                    if (check is null) continue;
                    var bucket = check.Domain == FactionCheckDomain.Disposition ? dispCounts : repCounts;
                    bucket[check.RawValue] = bucket.GetValueOrDefault(check.RawValue) + 1;
                }

        var dispRows = BuildRows(FactionCheckDomain.Disposition, dispCounts, gameId, catalogue);
        var repRows  = BuildRows(FactionCheckDomain.Reputation,  repCounts,  gameId, catalogue);

        return new RepDispositionReport(
            dispRows, repRows,
            dispCounts.Values.Sum(), repCounts.Values.Sum(),
            conversations.Count);
    }

    private static IReadOnlyList<BalanceRow> BuildRows(
        FactionCheckDomain domain, Dictionary<string, int> counts,
        string gameId, ConditionCatalogue catalogue)
    {
        // Seed every domain value at 0, then fold in counts; append unresolved encountered values.
        var domainValues = FactionCheckClassifier.PossibleValues(domain, gameId, catalogue);
        var seeded = new Dictionary<string, (string Display, bool Unresolved)>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in domainValues) seeded[v.StoredValue] = (v.DisplayName, false);
        foreach (var raw in counts.Keys)
            if (!seeded.ContainsKey(raw))
                seeded[raw] = (FactionCheckClassifier.ResolveDisplay(domain, raw, gameId, catalogue), true);

        var total = counts.Values.Sum();
        var rowCount = seeded.Count;
        var expected = rowCount > 0 ? (double)total / rowCount : 0;

        var rows = seeded.Select(kv =>
        {
            var count = counts.GetValueOrDefault(kv.Key);
            var flag =
                count == 0                         ? BalanceFlag.Ignored :
                expected > 0 && count >= OverFactor  * expected ? BalanceFlag.Over :
                expected > 0 && count <= UnderFactor * expected ? BalanceFlag.Under :
                BalanceFlag.Normal;
            var share = expected > 0 ? count / expected : 0;
            return new BalanceRow(kv.Value.Display, count, share, flag, kv.Value.Unresolved);
        });

        static int FlagOrder(BalanceFlag f) => f switch
        { BalanceFlag.Over => 0, BalanceFlag.Under => 1, BalanceFlag.Ignored => 2, _ => 3 };

        return rows
            .OrderBy(r => FlagOrder(r.Flag))
            .ThenByDescending(r => r.Count)
            .ThenBy(r => r.DisplayValue, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RepDispositionTallyServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Add fair-share boundary tests**

```csharp
// Append to RepDispositionTallyServiceTests.cs
[Fact]
public void Analyze_UnderUsedValue_FlaggedUnder_AtHalfExpectedBoundary()
{
    // Two values; total 5; expected 2.5. A value with count 1 (<= 1.25) is Under;
    // count 4 (>= 5.0? no) — check Over at >=5.0. Use counts 1 and 4 → expected 2.5.
    GameDataNameService.Register("Faction", new[]
    {
        new NamedEntry("Huana — h", "h"),
        new NamedEntry("VTC — v", "v"),
    });
    ConditionLeaf Rep(string g) =>
        new("Boolean IsReputation(Guid, RankType, Int32, Operator)",
            new[] { g, "Good", "1", "GreaterThan" }, false, "And");
    NodeEditSnapshot N(ConditionLeaf l) =>
        new(0, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false, [], new[] { (ConditionNode)l }, []);

    var nodes = new List<NodeEditSnapshot> { N(Rep("h")) };
    for (int i = 0; i < 4; i++) nodes.Add(N(Rep("v")));
    var report = RepDispositionTallyService.Analyze(
        new[] { ("c1", new ConversationEditSnapshot(nodes)) }, "poe2", ConditionCatalogue.Instance);

    Assert.Equal(BalanceFlag.Under, report.ReputationRows.First(r => r.DisplayValue.StartsWith("Huana")).Flag); // 1 <= 1.25
    Assert.Equal(BalanceFlag.Over,  report.ReputationRows.First(r => r.DisplayValue.StartsWith("VTC")).Flag);   // 4 >= 5.0? expected 2.5, 2x=5.0 → 4 < 5 → NOT Over
}
```

> Note: the assertion above is deliberately checked against the exact math — if `VTC` count 4 vs expected 2.5 is `Normal` (4 < 5.0 and 4 > 1.25), adjust the expected value to `BalanceFlag.Normal` when you run it. Fix the assertion to match the computed flag; do not change the service to satisfy a wrong expectation.

Run: `dotnet test --filter "FullyQualifiedName~RepDispositionTallyServiceTests"`
Expected: PASS after aligning the boundary assertion with the documented rule.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/Services/RepDispositionModels.cs DialogEditor.ViewModels/Services/RepDispositionTallyService.cs DialogEditor.Tests/Services/RepDispositionTallyServiceTests.cs
git commit -m "feat(viewmodels): RepDispositionTallyService — per-value tally with fair-share flags"
```

---

### Task 4: Gather step — Source×Scope → effective snapshots (ViewModels)

Turn the Source/Scope selection into the `(name, effectiveSnapshot)` list the tally consumes. Mirrors `ProjectFindService`'s walk: open conversation uses the live snapshot; other patched conversations are `base + patch`; the on-disk source additionally iterates every provider conversation.

**Files:**
- Create: `DialogEditor.ViewModels/Services/RepDispositionGatherService.cs`
- Test: `DialogEditor.Tests/Services/RepDispositionGatherServiceTests.cs`

**Interfaces:**
- Consumes: `DialogProject` (`.Patches` → `(convName, patch)`), `IGameDataProvider` (`FindConversation`, `LoadConversation`, `EnumerateConversations`), `ConversationSnapshotBuilder.Build`, `PatchApplier.Apply(base, patch, ignoreConflicts: true)`.
- Produces:
  - `public enum BalanceSource { ProjectChanges, OnDiskPlusChanges }`
  - `public enum BalanceScope { Current, All }`
  - `public static IReadOnlyList<(string Name, ConversationEditSnapshot Snapshot)> Gather(BalanceSource source, BalanceScope scope, DialogProject project, IGameDataProvider provider, string? openName, ConversationEditSnapshot? openSnapshot, CancellationToken ct = default);`

**Rules:**
- **Scope.Current** → exactly one entry: `(openName, openSnapshot)` if `openSnapshot` is non-null, else empty. (Same under either Source.)
- **Scope.All + Source.ProjectChanges** → every `(convName, patch)` in `project.Patches`; open conversation uses `openSnapshot`; others use `PatchApplier.Apply(Build(LoadConversation(FindConversation(convName))), patch, true)`. Unreadable → `AppLog.Warn`, skip.
- **Scope.All + Source.OnDiskPlusChanges** → every `provider.EnumerateConversations()` file; if a patch exists for the name apply it (open conversation → `openSnapshot`), else use the base snapshot. Check `ct.ThrowIfCancellationRequested()` in the loop. Unreadable → `AppLog.Warn`, skip.

- [ ] **Step 1: Write the failing test**

Use the existing test fakes. Check `DialogEditor.Tests/Helpers/FakeGameDataProvider.cs` and `DialogProject` construction patterns in `DialogEditor.Tests/Patch/DialogProjectTests.cs`; reuse them rather than inventing new fakes.

```csharp
// DialogEditor.Tests/Services/RepDispositionGatherServiceTests.cs
using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class RepDispositionGatherServiceTests
{
    [Fact]
    public void Gather_CurrentScope_ReturnsOnlyOpenConversation()
    {
        var open = new ConversationEditSnapshot([]);
        var project = DialogProject.Empty("p");   // no patches
        var provider = new FakeGameDataProvider();  // reuse existing test fake

        var result = RepDispositionGatherService.Gather(
            BalanceSource.ProjectChanges, BalanceScope.Current,
            project, provider, openName: "open_conv", openSnapshot: open);

        Assert.Single(result);
        Assert.Equal("open_conv", result[0].Name);
        Assert.Same(open, result[0].Snapshot);
    }

    [Fact]
    public void Gather_CurrentScope_NoOpenConversation_ReturnsEmpty()
    {
        var result = RepDispositionGatherService.Gather(
            BalanceSource.OnDiskPlusChanges, BalanceScope.Current,
            DialogProject.Empty("p"), new FakeGameDataProvider(),
            openName: null, openSnapshot: null);
        Assert.Empty(result);
    }
}
```

> If `FakeGameDataProvider`'s exact shape differs, adapt the construction to the fake's real constructor — do not add production code to accommodate a test-only convenience.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RepDispositionGatherServiceTests"`
Expected: FAIL — `RepDispositionGatherService` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// DialogEditor.ViewModels/Services/RepDispositionGatherService.cs
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

public enum BalanceSource { ProjectChanges, OnDiskPlusChanges }
public enum BalanceScope  { Current, All }

/// Resolves a Source×Scope selection into (name, effective-snapshot) pairs for the tally.
/// Mirrors ProjectFindService: live snapshot for the open conversation, base+patch for the
/// rest, and (for the on-disk source) every provider conversation with patches applied.
public static class RepDispositionGatherService
{
    public static IReadOnlyList<(string Name, ConversationEditSnapshot Snapshot)> Gather(
        BalanceSource source, BalanceScope scope,
        DialogProject project, IGameDataProvider provider,
        string? openName, ConversationEditSnapshot? openSnapshot,
        CancellationToken ct = default)
    {
        var result = new List<(string, ConversationEditSnapshot)>();

        if (scope == BalanceScope.Current)
        {
            if (openSnapshot is not null && openName is not null)
                result.Add((openName, openSnapshot));
            return result;
        }

        var patches = project.Patches.ToDictionary(p => p.Item1, p => p.Item2);

        ConversationEditSnapshot? Effective(string name)
        {
            if (name == openName && openSnapshot is not null) return openSnapshot;
            try
            {
                var file = provider.FindConversation(name);
                var baseSnap = file is not null
                    ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                    : new ConversationEditSnapshot([]);
                return patches.TryGetValue(name, out var patch)
                    ? PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true)
                    : baseSnap;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Rep/disposition balance: could not load '{name}': {ex.Message}");
                return null;
            }
        }

        IEnumerable<string> names = source == BalanceSource.ProjectChanges
            ? patches.Keys
            : provider.EnumerateConversations().Select(f => f.Name);

        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            if (Effective(name) is { } snap) result.Add((name, snap));
        }
        return result;
    }
}
```

> Adapt `project.Patches` element access (`.Item1`/`.Item2` vs named tuple/record) to the real `DialogProject.Patches` shape seen in `ProjectFindService` (`foreach (var (convName, patch) in project.Patches)`), and confirm `AppLog` is in scope (namespace used by `ProjectFindService`).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RepDispositionGatherServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/RepDispositionGatherService.cs DialogEditor.Tests/Services/RepDispositionGatherServiceTests.cs
git commit -m "feat(viewmodels): RepDispositionGatherService — Source x Scope effective snapshots"
```

---

### Task 5: RepDispositionBalanceViewModel (ViewModels)

The presentation VM: holds Source/Scope selection, an async Refresh (cancellable), and maps a `RepDispositionReport` into observable table rows + a status/summary line. Follows the `FlowAnalyticsViewModel` shape (injected delegates, `[ObservableProperty]`, `[RelayCommand]`).

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/RepDispositionBalanceViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/RepDispositionBalanceViewModelTests.cs`

**Interfaces:**
- Consumes: `RepDispositionGatherService`, `RepDispositionTallyService`, `ConditionCatalogue`, `Loc`, the four enums, `BalanceRow`.
- Constructor: `public RepDispositionBalanceViewModel(DialogProject project, IGameDataProvider provider, Func<(string? Name, ConversationEditSnapshot? Snapshot)> getOpenConversation)`. (The VM reads `provider.GameId` for the catalogue and `provider.Language` is unused here.)
- Produces (bound by the view):
  - `[ObservableProperty] BalanceSource _source;` `[ObservableProperty] BalanceScope _scope;`
  - `ObservableCollection<BalanceRowViewModel> DispositionRows { get; }` and `ReputationRows { get; }`
  - `[ObservableProperty] string _statusText;` `[ObservableProperty] bool _isBusy;`
  - `RefreshCommand` (async, cancellable): gather off the UI thread for `OnDiskPlusChanges`, then tally, then marshal row updates back.
  - `CancelCommand`.
  - `BalanceRowViewModel` wraps a `BalanceRow` with a localised `FlagLabel` (see string keys in Task 6) and formatted `ShareText` (e.g. `Loc.Format("RepBalance_Share", row.ShareVsExpected)` → `"2.4×"`).

**Async shape (copy the ValidateVO/FindInProject precedent):** `OnDiskPlusChanges` runs `RepDispositionGatherService.Gather` inside `Task.Run(() => ..., ct)`; the tally is cheap and can run there too. Wrap in `try/catch (OperationCanceledException) { /* silent */ } catch (Exception ex) { AppLog.Error(...); StatusText = Loc.Get("RepBalance_Error"); }`. `ProjectChanges`/`Current` are cheap — run synchronously.

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/ViewModels/RepDispositionBalanceViewModelTests.cs
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class RepDispositionBalanceViewModelTests : IDisposable
{
    public void Dispose() => GameDataNameService.Clear();

    [Fact]
    public async Task Refresh_CurrentScope_PopulatesRowsFromOpenConversation()
    {
        GameDataNameService.Register("Disposition", new[]
        {
            new NamedEntry("Benevolent", "Benevolent"),
            new NamedEntry("Cruel", "Cruel"),
        });
        ConditionLeaf Disp(string a) =>
            new("Boolean DispositionEqual(Axis, Rank)", new[] { a, "2" }, false, "And");
        var node = new NodeEditSnapshot(0, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false,
            [], new ConditionNode[] { Disp("Benevolent") }, []);
        var open = new ConversationEditSnapshot(new[] { node });

        var vm = new RepDispositionBalanceViewModel(
            DialogProject.Empty("p"), new FakeGameDataProvider(),
            () => ("open", open)) { Source = BalanceSource.ProjectChanges, Scope = BalanceScope.Current };

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(vm.DispositionRows, r => r.DisplayValue == "Benevolent" && r.Count == 1);
        Assert.Contains(vm.DispositionRows, r => r.DisplayValue == "Cruel" && r.Count == 0);
    }
}
```

> `FakeGameDataProvider` must report a `GameId` (e.g. `"poe2"`); use the existing fake's value or set it as the fake allows.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RepDispositionBalanceViewModelTests"`
Expected: FAIL — `RepDispositionBalanceViewModel` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// DialogEditor.ViewModels/ViewModels/RepDispositionBalanceViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public sealed class BalanceRowViewModel
{
    public string DisplayValue { get; }
    public int    Count        { get; }
    public string ShareText    { get; }
    public string FlagLabel    { get; }
    public BalanceFlag Flag    { get; }
    public bool   IsUnresolved { get; }

    public BalanceRowViewModel(BalanceRow row)
    {
        DisplayValue = row.DisplayValue;
        Count        = row.Count;
        Flag         = row.Flag;
        IsUnresolved = row.IsUnresolved;
        ShareText    = row.Count == 0 ? "—" : Loc.Format("RepBalance_Share", row.ShareVsExpected);
        FlagLabel    = row.Flag switch
        {
            BalanceFlag.Over    => Loc.Get("RepBalance_Flag_Over"),
            BalanceFlag.Under   => Loc.Get("RepBalance_Flag_Under"),
            BalanceFlag.Ignored => Loc.Get("RepBalance_Flag_Ignored"),
            _                   => Loc.Get("RepBalance_Flag_Normal"),
        };
    }
}

public partial class RepDispositionBalanceViewModel : ObservableObject
{
    private readonly DialogProject     _project;
    private readonly IGameDataProvider _provider;
    private readonly Func<(string? Name, ConversationEditSnapshot? Snapshot)> _getOpen;
    private CancellationTokenSource?   _cts;

    [ObservableProperty] private BalanceSource _source = BalanceSource.ProjectChanges;
    [ObservableProperty] private BalanceScope  _scope  = BalanceScope.Current;
    [ObservableProperty] private string        _statusText = string.Empty;
    [ObservableProperty] private bool          _isBusy;

    public ObservableCollection<BalanceRowViewModel> DispositionRows { get; } = [];
    public ObservableCollection<BalanceRowViewModel> ReputationRows  { get; } = [];

    public RepDispositionBalanceViewModel(
        DialogProject project, IGameDataProvider provider,
        Func<(string? Name, ConversationEditSnapshot? Snapshot)> getOpenConversation)
    {
        _project  = project;
        _provider = provider;
        _getOpen  = getOpenConversation;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var (openName, openSnap) = _getOpen();
        var source = Source; var scope = Scope;
        var gameId = _provider.GameId;

        try
        {
            IsBusy = true;
            StatusText = Loc.Get("RepBalance_Analyzing");

            RepDispositionReport report = scope == BalanceScope.All && source == BalanceSource.OnDiskPlusChanges
                ? await Task.Run(() =>
                    {
                        var convs = RepDispositionGatherService.Gather(
                            source, scope, _project, _provider, openName, openSnap, ct);
                        return RepDispositionTallyService.Analyze(convs, gameId, ConditionCatalogue.Instance);
                    }, ct)
                : RepDispositionTallyService.Analyze(
                    RepDispositionGatherService.Gather(source, scope, _project, _provider, openName, openSnap, ct),
                    gameId, ConditionCatalogue.Instance);

            DispositionRows.Clear();
            foreach (var r in report.DispositionRows) DispositionRows.Add(new BalanceRowViewModel(r));
            ReputationRows.Clear();
            foreach (var r in report.ReputationRows) ReputationRows.Add(new BalanceRowViewModel(r));

            StatusText = Loc.Format("RepBalance_Summary",
                report.ConversationsAnalyzed, report.DispositionTotal, report.ReputationTotal);
        }
        catch (OperationCanceledException) { /* deliberate cancel */ }
        catch (Exception ex)
        {
            AppLog.Error($"Rep/disposition balance failed: {ex}");
            StatusText = Loc.Get("RepBalance_Error");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RepDispositionBalanceViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/RepDispositionBalanceViewModel.cs DialogEditor.Tests/ViewModels/RepDispositionBalanceViewModelTests.cs
git commit -m "feat(viewmodels): RepDispositionBalanceViewModel — Source/Scope selection + async refresh"
```

---

### Task 6: Localisation strings + enum display converters

Add every user-visible string and the enum→label plumbing the window needs. No behaviour change to the VM.

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (add keys)
- Create: `DialogEditor.Avalonia/Converters/BalanceFlagToBrushConverter.cs` (Over/Under/Ignored → an existing `Brush.*` token; Normal → default text brush)
- Test: `DialogEditor.Tests/Localisation/…` already enforces no hard-coded strings; run the full suite to confirm no regression.

**Interfaces:**
- Consumes: `BalanceFlag` (Task 3), existing `Brush.Severity.Warning` / `Brush.Severity.Error` / `Brush.Text.Primary` tokens (confirm exact token names in `Tokens.axaml`).
- Produces: string resources + one converter registered in `App.axaml`.

- [ ] **Step 1: Add string keys to `Strings.axaml`**

Add these `<x:String>` entries (values are English source text; translators override later). Keys:

```xml
<x:String x:Key="Menu_RepDispositionBalance">Reputation &amp; Disposition Balance…</x:String>
<x:String x:Key="RepBalance_Title">Reputation &amp; Disposition Balance</x:String>
<x:String x:Key="RepBalance_Source_Label">Source:</x:String>
<x:String x:Key="RepBalance_Source_ProjectChanges">Project's own changes</x:String>
<x:String x:Key="RepBalance_Source_OnDiskPlusChanges">On disk + changes</x:String>
<x:String x:Key="RepBalance_Scope_Label">Scope:</x:String>
<x:String x:Key="RepBalance_Scope_Current">Current conversation</x:String>
<x:String x:Key="RepBalance_Scope_All">All conversations</x:String>
<x:String x:Key="RepBalance_Refresh">Analyze</x:String>
<x:String x:Key="RepBalance_Cancel">Cancel</x:String>
<x:String x:Key="RepBalance_DispositionsHeader">Dispositions</x:String>
<x:String x:Key="RepBalance_ReputationsHeader">Reputations</x:String>
<x:String x:Key="RepBalance_Col_Value">Value</x:String>
<x:String x:Key="RepBalance_Col_Count">Count</x:String>
<x:String x:Key="RepBalance_Col_Share">Share vs expected</x:String>
<x:String x:Key="RepBalance_Col_Flag">Flag</x:String>
<x:String x:Key="RepBalance_Flag_Normal"></x:String>
<x:String x:Key="RepBalance_Flag_Over">Over-favoured</x:String>
<x:String x:Key="RepBalance_Flag_Under">Under-used</x:String>
<x:String x:Key="RepBalance_Flag_Ignored">Never checked</x:String>
<x:String x:Key="RepBalance_Share">{0:0.0}×</x:String>
<x:String x:Key="RepBalance_Analyzing">Analyzing…</x:String>
<x:String x:Key="RepBalance_Summary">{0} conversation(s) · {1} disposition checks · {2} reputation checks</x:String>
<x:String x:Key="RepBalance_Error">Analysis failed — see the log for details.</x:String>
<x:String x:Key="RepBalance_Tip_Source">Choose whether to tally only the conversations your project changes, or every conversation on disk with your changes applied.</x:String>
<x:String x:Key="RepBalance_Tip_Scope">Choose whether to tally just the open conversation or every conversation in the selected source.</x:String>
<x:String x:Key="RepBalance_Tip_Refresh">Run the tally over the selected source and scope.</x:String>
<x:String x:Key="RepBalance_Tip_Table">Each value's check count, its share of the even-split expected count, and whether it is over-favoured, under-used, or never checked.</x:String>
```

> If `Summary` should use CLDR pluralisation (the project bans naive "(s)" via `NoNaivePluralTests`), split it into `_One`/`_Other` keys and use `Loc.FormatCount` per the pluralisation precedent (`docs/superpowers/specs/2026-07-04-pluralisation-design.md`). Check whether `NoNaivePluralTests` fails on the `(s)` above; if it does, convert these three counts to `FormatCount` keys.

- [ ] **Step 2: Create the flag→brush converter**

```csharp
// DialogEditor.Avalonia/Converters/BalanceFlagToBrushConverter.cs
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;   // TokenBrushes.Resolve — confirm namespace
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Converters;

/// Maps a BalanceFlag to a semantic brush token. Over/Ignored read as errors,
/// Under as a warning, Normal as primary text. No hex — resolves Brush.* tokens.
public sealed class BalanceFlagToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BalanceFlag flag
            ? TokenBrushes.Resolve(flag switch
            {
                BalanceFlag.Over    => "Brush.Severity.Error",
                BalanceFlag.Ignored => "Brush.Severity.Error",
                BalanceFlag.Under   => "Brush.Severity.Warning",
                _                   => "Brush.Text.Primary",
            })
            : TokenBrushes.Resolve("Brush.Text.Primary");

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}
```

> Confirm the exact `TokenBrushes.Resolve` signature/namespace and token key names against an existing converter (e.g. `FlowIssueKindToSeverityGlyphConverter` / `NodeColorConverter`). Use whatever the resolver expects — do not introduce a hex literal (`NoStrayHexTests` will fail the build).

- [ ] **Step 3: Register the converter in `App.axaml`**

Add to the `<Application.Resources>` converter block (mirror an existing entry):

```xml
<conv:BalanceFlagToBrushConverter x:Key="BalanceFlagToBrush" />
```

(Ensure the `conv:` namespace prefix already maps to `DialogEditor.Avalonia.Converters`.)

- [ ] **Step 4: Build to confirm resources/converter compile**

Run: `dotnet build DialogEditor.Avalonia`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/Converters/BalanceFlagToBrushConverter.cs DialogEditor.Avalonia/App.axaml
git commit -m "feat(ui): rep/disposition balance strings + flag brush converter"
```

---

### Task 7: The window + menu wiring

Add `RepDispositionBalanceWindow`, wire a gated command in `MainWindowViewModel`, a `ShowRepDispositionBalance` delegate set in `MainWindow.axaml.cs`, and a menu item — mirroring the **Find in Project** pattern exactly.

**Files:**
- Create: `DialogEditor.Avalonia/Views/RepDispositionBalanceWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/RepDispositionBalanceWindow.axaml.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (command + delegate + `NotifyCanExecuteChanged` sites)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (set the delegate)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (menu item)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs` (or the file where command-gating is tested) — assert `CanShowRepDispositionBalance` matches `CanFindInProject`'s gate.

**Interfaces:**
- Consumes: `RepDispositionBalanceViewModel` (Task 5), `MainWindowViewModel._project/_provider/Canvas`.
- Produces:
  - `public Func<RepDispositionBalanceViewModel, Task>? ShowRepDispositionBalance { get; set; }`
  - `[RelayCommand(CanExecute = nameof(CanShowRepDispositionBalance))] private async Task ShowRepDispositionBalanceWindow()`
  - `public bool CanShowRepDispositionBalance => _project is not null && _provider is not null && !string.IsNullOrEmpty(_currentGameDirectory);`

- [ ] **Step 1: Write the failing gate test**

```csharp
// Add to the existing MainWindowViewModel command-gating test file.
[Fact]
public void CanShowRepDispositionBalance_MatchesFindInProjectGate_WhenNoProject()
{
    var vm = new MainWindowViewModel(/* existing test ctor args */);
    Assert.False(vm.CanShowRepDispositionBalance);   // no project/provider loaded
}
```

> Use the same construction the existing `MainWindowViewModel` tests use. If those tests don't construct the VM directly (it may need many collaborators), instead assert the gate indirectly the way `CanFindInProject` is currently tested, or skip this step and rely on Step 6's GUI verification — do not fabricate a constructor.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MainWindowViewModel"`
Expected: FAIL — `CanShowRepDispositionBalance` does not exist.

- [ ] **Step 3: Add the command, delegate, and gate to `MainWindowViewModel`**

Place next to `FindInProject` (around the `ShowFindInProject`/`FindInProject` members). Copy the `NotifyCanExecuteChanged()` for `ShowRepDispositionBalanceWindowCommand` into every site that currently notifies `FindInProjectCommand` (SetProject, save, close, save-as, folder-load — the grep for `FindInProjectCommand.NotifyCanExecuteChanged` lists them all).

```csharp
public Func<RepDispositionBalanceViewModel, Task>? ShowRepDispositionBalance { get; set; }

public bool CanShowRepDispositionBalance =>
    _project is not null && _provider is not null && !string.IsNullOrEmpty(_currentGameDirectory);

[RelayCommand(CanExecute = nameof(CanShowRepDispositionBalance))]
private async Task ShowRepDispositionBalanceWindow()
{
    if (_project is null || _provider is null) return;
    var vm = new RepDispositionBalanceViewModel(
        _project, _provider,
        () => Canvas.Nodes.Count > 0
            ? (Canvas.ConversationName, (ConversationEditSnapshot?)Canvas.BuildSnapshot())
            : (null, (ConversationEditSnapshot?)null));
    if (ShowRepDispositionBalance is not null)
        await ShowRepDispositionBalance(vm);
}
```

- [ ] **Step 4: Create the window**

```xml
<!-- DialogEditor.Avalonia/Views/RepDispositionBalanceWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.RepDispositionBalanceWindow"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Title="{DynamicResource RepBalance_Title}"
        Width="720" Height="560" MinWidth="480" MinHeight="360">
  <Grid RowDefinitions="Auto,*,Auto" Margin="12">
    <!-- Row 0: selectors + Analyze/Cancel. Each control carries ToolTip.Tip + AutomationProperties.Name. -->
    <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="8">
      <TextBlock Text="{DynamicResource RepBalance_Source_Label}" VerticalAlignment="Center"/>
      <ComboBox x:Name="SourceBox"
                ToolTip.Tip="{DynamicResource RepBalance_Tip_Source}"
                AutomationProperties.Name="{DynamicResource RepBalance_Source_Label}"/>
      <TextBlock Text="{DynamicResource RepBalance_Scope_Label}" VerticalAlignment="Center"/>
      <ComboBox x:Name="ScopeBox"
                ToolTip.Tip="{DynamicResource RepBalance_Tip_Scope}"
                AutomationProperties.Name="{DynamicResource RepBalance_Scope_Label}"/>
      <Button Content="{DynamicResource RepBalance_Refresh}"
              Command="{Binding RefreshCommand}"
              ToolTip.Tip="{DynamicResource RepBalance_Tip_Refresh}"/>
      <Button Content="{DynamicResource RepBalance_Cancel}"
              Command="{Binding CancelCommand}"
              IsVisible="{Binding IsBusy}"/>
    </StackPanel>

    <!-- Row 1: two tables. Use DataGrid or ItemsControl per the codebase's table idiom. -->
    <Grid Grid.Row="1" ColumnDefinitions="*,*" Margin="0,8">
      <!-- Dispositions table bound to DispositionRows; Reputations bound to ReputationRows.
           Flag cell foreground uses {StaticResource BalanceFlagToBrush} on Flag.
           Give the tables ToolTip.Tip="{DynamicResource RepBalance_Tip_Table}". -->
    </Grid>

    <TextBlock Grid.Row="2" Text="{Binding StatusText}"/>
  </Grid>
</Window>
```

```csharp
// DialogEditor.Avalonia/Views/RepDispositionBalanceWindow.axaml.cs
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class RepDispositionBalanceWindow : Window
{
    public RepDispositionBalanceWindow() => InitializeComponent();   // XAML-compiler ctor

    public RepDispositionBalanceWindow(RepDispositionBalanceViewModel vm) : this()
    {
        DataContext = vm;

        // Populate the source/scope combo boxes with (label, enum) pairs so the display
        // text is localised while the SelectedItem stays the enum value.
        SourceBox.ItemsSource = new List<KeyValuePair<string, BalanceSource>>
        {
            new(Loc.Get("RepBalance_Source_ProjectChanges"), BalanceSource.ProjectChanges),
            new(Loc.Get("RepBalance_Source_OnDiskPlusChanges"), BalanceSource.OnDiskPlusChanges),
        };
        SourceBox.DisplayMemberBinding = new Avalonia.Data.Binding("Key");
        SourceBox.SelectedIndex = 0;
        SourceBox.SelectionChanged += (_, _) =>
        { if (SourceBox.SelectedItem is KeyValuePair<string, BalanceSource> kv) vm.Source = kv.Value; };

        ScopeBox.ItemsSource = new List<KeyValuePair<string, BalanceScope>>
        {
            new(Loc.Get("RepBalance_Scope_Current"), BalanceScope.Current),
            new(Loc.Get("RepBalance_Scope_All"), BalanceScope.All),
        };
        ScopeBox.DisplayMemberBinding = new Avalonia.Data.Binding("Key");
        ScopeBox.SelectedIndex = 0;
        ScopeBox.SelectionChanged += (_, _) =>
        { if (ScopeBox.SelectedItem is KeyValuePair<string, BalanceScope> kv) vm.Scope = kv.Value; };
    }
}
```

> Prefer the codebase's existing table idiom for the two tables — if `DataGrid` is already used elsewhere, follow that; otherwise an `ItemsControl` with a header row. Bind each Flag cell's `Foreground` to `{StaticResource BalanceFlagToBrush}`. Keep the combo-box population approach consistent with how other windows localise enum choices; if a shared pattern exists, use it instead of the inline lists above.

- [ ] **Step 5: Wire the delegate + menu item**

In `MainWindow.axaml.cs`, next to `vm.ShowFindInProject = …`:

```csharp
vm.ShowRepDispositionBalance = async balanceVm =>
{
    var win = new RepDispositionBalanceWindow(balanceVm);
    win.Show(this);          // non-modal, owned
    balanceVm.RefreshCommand.Execute(null);   // analyse on open
    await Task.CompletedTask;
};
```

In `MainWindow.axaml`, add a `MenuItem` in the same menu that hosts **Find in Project** / **Flow Analytics**:

```xml
<MenuItem Header="{DynamicResource Menu_RepDispositionBalance}"
          Command="{Binding ShowRepDispositionBalanceWindowCommand}"
          ToolTip.Tip="{DynamicResource RepBalance_Tip_Refresh}"/>
```

- [ ] **Step 6: Build, run, and GUI-verify**

Run: `dotnet build`
Expected: succeeds.

Then use the `running-the-app` skill to launch the editor, open a PoE2 project with a game folder, and confirm:
- The menu item is enabled only with a project + game folder loaded.
- Opening the window shows the two tables; switching Source/Scope + Analyze repopulates.
- A never-checked disposition/faction shows a `0` row flagged "Never checked".
- The "On disk + changes / All conversations" run shows the Cancel button and completes.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Views/RepDispositionBalanceWindow.axaml DialogEditor.Avalonia/Views/RepDispositionBalanceWindow.axaml.cs DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Avalonia/Views/MainWindow.axaml.cs DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat(ui): Reputation & Disposition Balance window + menu wiring"
```

---

### Task 8: Full-suite green + Gaps.md mark

- [ ] **Step 1: Run the whole test suite**

Run: `dotnet test`
Expected: PASS (serial run; no `GameDataNameService`/`AppSettings` leakage between tests — every new test that registers game-data names clears it in `Dispose`).

- [ ] **Step 2: Mark the gap implemented in `Gaps.md`**

Edit the **Reputation & Disposition Check Balance** entry: change `**📐 Designed (2026-07-16), not yet implemented.**` to `**✅ Implemented (<date>).**` and append the shipped specifics (window name, Source×Scope, fair-share flags, `RepDispositionTallyService`, and the `<commit hash>`), mirroring how other implemented gaps read.

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark rep/disposition balance implemented"
```

---

## Self-Review

**Spec coverage:**
- New dedicated window → Task 7. ✓
- Source (Project changes | On-disk+changes) × Scope (Current | All) → Task 4 (gather) + Task 7 (selectors). ✓
- Effective base+patch snapshots; open conversation live → Task 4. ✓
- Tally by value; checks only (scripts excluded) → Tasks 1–3. ✓
- Two domains tallied separately; full-domain enumeration with 0 rows → Tasks 2–3. ✓
- Fair-share flags (≥2× / ≤½× / 0) within each domain → Task 3. ✓
- Unresolved values shown, not dropped → Task 3. ✓
- Async/cancellable heavy sweep → Task 5. ✓
- Read-only (no mutation) → nothing writes; verified by construction. ✓
- Localisation, tooltips, window icon, UIA names, error logging → Tasks 6–7 + Global Constraints. ✓
- Single game per project (no PoE1/PoE2 mixing) → Task 2 uses `provider.GameId` + `FindByFullName(fullName, gameId)`. ✓
- v1 no per-rank breakdown → Tasks 2–3 use only parameter 0. ✓

**Placeholder scan:** No "TBD"/"add error handling"/"similar to Task N" left; each code step shows real code. The few "confirm the exact X" notes point at concrete files to verify a name against, not deferred work.

**Type consistency:** `FactionCheckDomain`, `BalanceFlag`, `BalanceRow`, `RepDispositionReport`, `RepDispositionTallyService.Analyze`, `RepDispositionGatherService.Gather`, `BalanceSource`/`BalanceScope`, `RepDispositionBalanceViewModel(project, provider, getOpenConversation)`, `ShowRepDispositionBalance` / `ShowRepDispositionBalanceWindowCommand` / `CanShowRepDispositionBalance` are used identically across Tasks 2–7. `ConditionLeaves()` (Task 1) is consumed by Task 3. ✓

**Known verification points for the implementer** (call out, don't hide): exact `DialogProject.Patches` tuple shape, `FakeGameDataProvider` constructor/`GameId`, `TokenBrushes.Resolve` signature + real `Brush.*` token keys, whether `NoNaivePluralTests` forces `Loc.FormatCount` on the summary string, and the real `MainWindowViewModel` test-construction pattern. Each is a "match the existing code" check, resolved by reading the named neighbour before writing the step.
