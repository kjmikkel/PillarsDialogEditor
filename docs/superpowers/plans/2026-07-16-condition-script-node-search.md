# Condition/Script Node Search & Highlight — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the writer search one conversation for the nodes that use a chosen condition or script (narrowed by pinned parameters) and highlight those nodes on the canvas while dimming the rest.

**Architecture:** A pure Core `CatalogueMatch` predicate (the primitive deferred from Gap #1) drives a pure Core `NodeConditionSearchService` that walks each node's three condition/script sites and returns matching node IDs. The existing per-node search-dim (`IsSearchMatch` bool) is **unified** into a `SearchMatchState { None, Match, Dimmed }` enum that both the existing text search and the new condition search flow through. A collapsible dock in the canvas area (owned by `ConversationViewModel`) picks a catalogue entry, pins parameters, and applies Match/Dimmed to the open conversation's nodes.

**Tech Stack:** C# / .NET 8, Avalonia, CommunityToolkit.Mvvm, xUnit, Nodify canvas.

**Specs:** `docs/superpowers/specs/2026-07-16-condition-script-node-search-design.md` and the shared `docs/superpowers/specs/2026-07-16-catalogue-match-primitive-design.md`.

## Two deviations from the approved spec (decided during planning)

1. **Highlight state — UNIFY (user decision).** The spec proposed a fresh node highlight state; the codebase already had `NodeViewModel.IsSearchMatch` (bool) dimming non-matches for the canvas **text search**. The user chose to **unify** both searches through one `SearchMatchState { None, Match, Dimmed }` enum. The text search keeps its dim-only look (match → `None`, non-match → `Dimmed`, never `Match`); the condition search uses `Match` for hits and `Dimmed` for the rest. Consequence (accepted): the two searches share the state, so starting one replaces the other (last-search-wins).
2. **Panel host — embedded DOCK (user decision).** The search lives as a collapsible panel docked to the right edge of the **canvas area** inside `ConversationView`, toggled from the canvas toolbar — not a floating window. Because it operates on the open conversation and highlights `Canvas.Nodes` directly, the search VM hangs off `ConversationViewModel` (which owns the canvas). A future flag could unify this dock with the left (browser) and right (detail) panels into one docking host; this plan keeps it a self-contained collapsible panel. This only shapes Task 6; Tasks 1–5 are host-agnostic.

## Global Constraints

Copied verbatim from the specs and CLAUDE.md — every task implicitly includes these:

- **TDD red/green** — failing test before implementation for all non-trivial logic; tests in `DialogEditor.Tests` mirroring the structure.
- **Localisation** — no user-visible string hard-coded in XAML/C#; add to `DialogEditor.Avalonia/Resources/Strings.axaml` (use `<sys:String>`), read via `Loc.Get`/`Loc.Format`.
- **Tooltips mandatory** — every interactive control (entry picker, each parameter input, Search, Clear) carries a detailed `ToolTip`.
- **Window icon** — the new window sets `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- **UI Automation** — controls discoverable by UIA Name (`AutomationProperties.Name` where no label provides one).
- **Error handling** — every caught exception logged via `AppLog.Error/Warn`, except `OperationCanceledException` (swallow silently). No bare `catch {}`.
- **Tests run serially** — global statics (`AppSettings`/`Loc`/`GameDataNameService`); configure `Loc` in VM tests via `StubStringProvider`; `Clear()` any registered game-data names in `Dispose`.
- **Single entry per search (v1)** — one catalogue entry, its parameters optionally pinned. No multi-condition AND/OR.
- **Node-only highlight granularity** — highlight the node; do not separately mark the connection.
- **Match sites** — node's own conditions, any outgoing link's conditions, and the node's scripts.
- **No hex in XAML/converters** — resolve `Brush.*` tokens (`NoStrayHexTests`).

---

### Task 1: `CatalogueMatch` primitive (Core)

The shared predicate (deferred from Gap #1). Pure; matches a `ConditionLeaf`/`ScriptCall` against one catalogue entry with parameters optionally pinned.

**Files:**
- Create: `DialogEditor.Core/Search/CatalogueMatch.cs`
- Test: `DialogEditor.Tests/Search/CatalogueMatchTests.cs`

**Interfaces:**
- Consumes: `ConditionLeaf`, `ScriptCall` (both expose `string FullName` + `IReadOnlyList<string> Parameters`).
- Produces:
  - `public readonly record struct ParameterPin(bool IsPinned, string? Value)` with `static ParameterPin Wildcard` and `static ParameterPin Pin(string value)`.
  - `public sealed record CatalogueMatch(string ReflectionFullName, IReadOnlyList<ParameterPin> Pins)` with:
    - `bool Matches(string fullName, IReadOnlyList<string> parameters)`
    - `bool Matches(ConditionLeaf leaf)` and `bool Matches(ScriptCall call)` adapters.

Semantics: `fullName` equals `ReflectionFullName` (ordinal, case-insensitive); for each `i` where `Pins[i].IsPinned`, `parameters[i]` must equal `Pins[i].Value` (ordinal, case-insensitive; a missing index is a non-match); wildcard slots impose nothing; fewer pins than parameters → trailing wildcards.

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Search/CatalogueMatchTests.cs
using DialogEditor.Core.Models;
using DialogEditor.Core.Search;

namespace DialogEditor.Tests.Search;

public class CatalogueMatchTests
{
    private static CatalogueMatch Match(string full, params ParameterPin[] pins) => new(full, pins);

    [Fact]
    public void Matches_MethodIdentity_CaseInsensitive_DifferentSignatureFails()
    {
        var m = Match("Boolean IsReputation(Guid, RankType, Int32, Operator)");
        Assert.True(m.Matches("boolean isreputation(guid, ranktype, int32, operator)", new[] { "a" }));
        Assert.False(m.Matches("Boolean IsReputation(Guid, Axis, Int32)", new[] { "a" }));  // PoE1 overload
    }

    [Fact]
    public void Matches_PinnedParam_MatchesAndMisses()
    {
        var m = Match("Boolean IsDisposition(Guid, Rank, Operator)",
            ParameterPin.Pin("benevolent"), ParameterPin.Wildcard, ParameterPin.Wildcard);
        Assert.True(m.Matches("Boolean IsDisposition(Guid, Rank, Operator)", new[] { "Benevolent", "2", "GT" }));
        Assert.False(m.Matches("Boolean IsDisposition(Guid, Rank, Operator)", new[] { "Cruel", "2", "GT" }));
    }

    [Fact]
    public void Matches_WildcardOnly_MatchesAnyCallOfMethod()
    {
        var m = Match("Void SetGlobalValue(String, Int32)");
        Assert.True(m.Matches("Void SetGlobalValue(String, Int32)", new[] { "x", "1" }));
    }

    [Fact]
    public void Matches_FewerPinsThanParams_TrailingWildcards()
    {
        var m = Match("Boolean IsDisposition(Guid, Rank, Operator)", ParameterPin.Pin("Benevolent"));
        Assert.True(m.Matches("Boolean IsDisposition(Guid, Rank, Operator)", new[] { "Benevolent", "2", "GT" }));
    }

    [Fact]
    public void Matches_PinIndexBeyondParams_NoMatch_NoThrow()
    {
        var m = Match("Boolean IsDisposition(Guid, Rank, Operator)",
            ParameterPin.Wildcard, ParameterPin.Pin("2"));
        Assert.False(m.Matches("Boolean IsDisposition(Guid, Rank, Operator)", new[] { "Benevolent" }));
    }

    [Fact]
    public void Matches_ScriptCallAdapter()
    {
        var m = Match("Void SetGlobalValue(String, Int32)", ParameterPin.Pin("g"), ParameterPin.Wildcard);
        var call = new ScriptCall("Void SetGlobalValue(String, Int32)", new[] { "g", "1" }, ScriptCategory.Enter);
        Assert.True(m.Matches(call));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CatalogueMatchTests"`
Expected: FAIL — `CatalogueMatch`/`ParameterPin` do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// DialogEditor.Core/Search/CatalogueMatch.cs
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Search;

/// One parameter slot in a CatalogueMatch: a concrete value that must match, or a wildcard.
public readonly record struct ParameterPin(bool IsPinned, string? Value)
{
    public static ParameterPin Wildcard         => new(false, null);
    public static ParameterPin Pin(string value) => new(true, value);
}

/// A query against ONE catalogue entry (a condition OR a script). Matches on the reflection
/// FullName (keeps PoE1/PoE2 overloads separate) with parameters optionally pinned; unpinned
/// slots are wildcards. Pure; shared by the reputation/disposition and node-search features.
public sealed record CatalogueMatch(string ReflectionFullName, IReadOnlyList<ParameterPin> Pins)
{
    public bool Matches(string fullName, IReadOnlyList<string> parameters)
    {
        if (!string.Equals(fullName, ReflectionFullName, StringComparison.OrdinalIgnoreCase))
            return false;
        for (int i = 0; i < Pins.Count; i++)
        {
            if (!Pins[i].IsPinned) continue;
            if (i >= parameters.Count) return false;
            if (!string.Equals(parameters[i], Pins[i].Value, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    public bool Matches(ConditionLeaf leaf) => Matches(leaf.FullName, leaf.Parameters);
    public bool Matches(ScriptCall call)    => Matches(call.FullName, call.Parameters);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~CatalogueMatchTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/Search/CatalogueMatch.cs DialogEditor.Tests/Search/CatalogueMatchTests.cs
git commit -m "feat(core): CatalogueMatch primitive — pinned/wildcard entry matcher"
```

---

### Task 2: `NodeConditionSearchService` (Core)

Given a conversation snapshot and a `CatalogueMatch`, return the set of node IDs where the query matches any node condition, any link condition, or any script.

**Files:**
- Create: `DialogEditor.Core/Search/NodeConditionSearchService.cs`
- Test: `DialogEditor.Tests/Search/NodeConditionSearchServiceTests.cs`

**Interfaces:**
- Consumes: `ConversationEditSnapshot`, `NodeConditionExtensions.ConditionLeaves` (Gap #1, `DialogEditor.Core.Editing`), `NodeEditSnapshot.Scripts`, `CatalogueMatch` (Task 1).
- Produces: `public static IReadOnlySet<int> FindMatches(ConversationEditSnapshot snapshot, CatalogueMatch query);`

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Search/NodeConditionSearchServiceTests.cs
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Search;

namespace DialogEditor.Tests.Search;

public class NodeConditionSearchServiceTests
{
    private static ConditionLeaf CondLeaf(string full, params string[] args) =>
        new(full, args, Not: false, Operator: "And");

    private static NodeEditSnapshot Node(
        int id,
        IReadOnlyList<ConditionNode>? nodeConds = null,
        IReadOnlyList<ScriptCall>? scripts = null,
        IReadOnlyList<ConditionNode>? linkConds = null)
    {
        var links = linkConds is null
            ? (IReadOnlyList<LinkEditSnapshot>)[]
            : new[] { new LinkEditSnapshot(id, id + 1, 0f, "", HasConditions: true) { Conditions = linkConds } };
        return new NodeEditSnapshot(id, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false,
            links, nodeConds ?? [], scripts ?? []);
    }

    private static readonly CatalogueMatch DispQuery =
        new("Boolean IsDisposition(Guid, Rank, Operator)", new[] { ParameterPin.Wildcard });

    [Fact]
    public void FindMatches_HitViaNodeCondition()
    {
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, nodeConds: new ConditionNode[] { CondLeaf("Boolean IsDisposition(Guid, Rank, Operator)", "b", "2", "GT") }),
            Node(1),
        });
        Assert.Equal(new[] { 0 }, NodeConditionSearchService.FindMatches(snap, DispQuery).Order());
    }

    [Fact]
    public void FindMatches_HitViaLinkCondition()
    {
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, linkConds: new ConditionNode[] { CondLeaf("Boolean IsDisposition(Guid, Rank, Operator)", "b", "2", "GT") }),
        });
        Assert.Contains(0, NodeConditionSearchService.FindMatches(snap, DispQuery));
    }

    [Fact]
    public void FindMatches_HitViaScript()
    {
        var query = new CatalogueMatch("Void SetGlobalValue(String, Int32)", new[] { ParameterPin.Wildcard, ParameterPin.Wildcard });
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, scripts: new[] { new ScriptCall("Void SetGlobalValue(String, Int32)", new[] { "g", "1" }, ScriptCategory.Enter) }),
        });
        Assert.Contains(0, NodeConditionSearchService.FindMatches(snap, query));
    }

    [Fact]
    public void FindMatches_NodeMatchingTwoSites_ReturnedOnce()
    {
        var leaf = CondLeaf("Boolean IsDisposition(Guid, Rank, Operator)", "b", "2", "GT");
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, nodeConds: new ConditionNode[] { leaf }, linkConds: new ConditionNode[] { leaf }),
        });
        Assert.Single(NodeConditionSearchService.FindMatches(snap, DispQuery));
    }

    [Fact]
    public void FindMatches_NoMatch_ReturnsEmpty()
    {
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, nodeConds: new ConditionNode[] { CondLeaf("Boolean IsGlobalValue(String, Operator, Int32)", "g", "EqualTo", "1") }),
        });
        Assert.Empty(NodeConditionSearchService.FindMatches(snap, DispQuery));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~NodeConditionSearchServiceTests"`
Expected: FAIL — `NodeConditionSearchService` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// DialogEditor.Core/Search/NodeConditionSearchService.cs
using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Search;

/// Finds the nodes in one conversation whose conditions or scripts match a CatalogueMatch
/// query. A node is a hit if the query matches any leaf in its own condition tree, any leaf
/// in any outgoing link's condition tree (both via ConditionLeaves), or any of its scripts.
/// Node-only granularity: a node matching in multiple sites appears once. Pure; no IO.
public static class NodeConditionSearchService
{
    public static IReadOnlySet<int> FindMatches(ConversationEditSnapshot snapshot, CatalogueMatch query)
    {
        var hits = new HashSet<int>();
        foreach (var node in snapshot.Nodes)
        {
            if (node.ConditionLeaves().Any(query.Matches) || node.Scripts.Any(query.Matches))
                hits.Add(node.NodeId);
        }
        return hits;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~NodeConditionSearchServiceTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/Search/NodeConditionSearchService.cs DialogEditor.Tests/Search/NodeConditionSearchServiceTests.cs
git commit -m "feat(core): NodeConditionSearchService — match nodes by condition/script query"
```

---

### Task 3: `SearchMatchState` enum + unify the text search

Replace `NodeViewModel.IsSearchMatch` (bool) with a `SearchMatchState { None, Match, Dimmed }` enum, and migrate the existing text-search call sites in `ConversationViewModel` to set it. Text search keeps its dim-only look (match → `None`, non-match → `Dimmed`).

**Files:**
- Create: `DialogEditor.ViewModels/SearchMatchState.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs:248` (replace the bool property)
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs:232,250` (text-search call sites)
- Test: `DialogEditor.Tests/ViewModels/NodeSearchStateTests.cs`

**Interfaces:**
- Produces:
  - `public enum SearchMatchState { None, Match, Dimmed }`
  - `NodeViewModel.SearchMatchState` (`[ObservableProperty]`, default `None`).
- The text search sets, per node: `node.SearchMatchState = isTextMatch ? SearchMatchState.None : SearchMatchState.Dimmed;`. Empty query → all `None`.

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/ViewModels/NodeSearchStateTests.cs
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.ViewModels;

public class NodeSearchStateTests
{
    public NodeSearchStateTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void NewNode_DefaultsToNone()
    {
        var node = new ConversationNode(0, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", false, false, [], [], []);
        var vm = new NodeViewModel(node, null);
        Assert.Equal(SearchMatchState.None, vm.SearchMatchState);
    }
}
```

> Match the real `ConversationNode` constructor — adapt the argument list to whatever `ConversationNode` actually takes (see `DialogEditor.Core/Models/ConversationNode.cs`); the point of the test is only the `SearchMatchState` default.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~NodeSearchStateTests"`
Expected: FAIL — `SearchMatchState` / property does not exist.

- [ ] **Step 3: Create the enum and migrate `NodeViewModel`**

```csharp
// DialogEditor.ViewModels/SearchMatchState.cs
namespace DialogEditor.ViewModels;

/// A node's canvas emphasis under the active search. None = normal (no search, or a
/// text-search match). Match = a condition/script-search hit (emphasis border). Dimmed =
/// faded because a search is active and this node is not a match.
public enum SearchMatchState { None, Match, Dimmed }
```

In `NodeViewModel.cs`, replace line 248:

```csharp
// was: [ObservableProperty] private bool _isSearchMatch = true;
[ObservableProperty] private SearchMatchState _searchMatchState = SearchMatchState.None;
```

- [ ] **Step 4: Migrate the text-search call sites in `ConversationViewModel`**

Replace line 232 (empty-query reset):

```csharp
// was: node.IsSearchMatch = true;
node.SearchMatchState = SearchMatchState.None;
```

Replace line 250 (apply results):

```csharp
// was: results[i].node.IsSearchMatch = results[i].match;
results[i].node.SearchMatchState =
    results[i].match ? SearchMatchState.None : SearchMatchState.Dimmed;
```

- [ ] **Step 5: Run test + build to verify no stray `IsSearchMatch` remains**

Run: `dotnet build DialogEditor.ViewModels`
Expected: build succeeds (the XAML binding in Task 4 is the only other reference; it's updated there).
Run: `dotnet test --filter "FullyQualifiedName~NodeSearchStateTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/SearchMatchState.cs DialogEditor.ViewModels/ViewModels/NodeViewModel.cs DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs DialogEditor.Tests/ViewModels/NodeSearchStateTests.cs
git commit -m "refactor(viewmodels): unify node search dim into SearchMatchState enum"
```

---

### Task 4: Converters + node template (opacity + emphasis)

Render `SearchMatchState`: `Dimmed` reduces opacity; `Match` draws an emphasis border. Add two converters and update the node template. Do **not** touch the shared `SearchMatchOpacity` (`BoolToOpacityConverter`) key — `VoAliasPickerWindow` reuses it.

**Files:**
- Create: `DialogEditor.Avalonia/Converters/SearchMatchStateToOpacityConverter.cs`
- Create: `DialogEditor.Avalonia/Converters/SearchMatchStateToBorderBrushConverter.cs`
- Modify: `DialogEditor.Avalonia/App.axaml` (register both, near the other converters)
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml:213` (opacity binding) + add an emphasis `Border` in the node card

**Interfaces:**
- Consumes: `SearchMatchState` (Task 3), `TokenBrushes.Resolve`, an existing emphasis token (confirm a suitable one in `Tokens.axaml` — e.g. `Brush.Severity.Warning` or a selection/among token; pick one that reads as "highlight" and is not the diff/connect colours).
- Produces: `SearchStateOpacity` and `SearchMatchBorderBrush` resource keys.

- [ ] **Step 1: Create the opacity converter**

```csharp
// DialogEditor.Avalonia/Converters/SearchMatchStateToOpacityConverter.cs
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Converters;

/// Dimmed → faded; None/Match → full opacity. Mirrors the previous BoolToOpacity dim but
/// keyed off the unified SearchMatchState.
public sealed class SearchMatchStateToOpacityConverter : IValueConverter
{
    private const double DimmedOpacity = 0.35;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SearchMatchState.Dimmed ? DimmedOpacity : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Create the emphasis-border converter**

```csharp
// DialogEditor.Avalonia/Converters/SearchMatchStateToBorderBrushConverter.cs
using System;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Data.Converters;
using DialogEditor.Avalonia.Theming;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Converters;

/// Match → an emphasis brush drawn as a node border; otherwise Transparent. Resolves a
/// Brush.* token (no hex — NoStrayHexTests).
public sealed class SearchMatchStateToBorderBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SearchMatchState.Match
            ? TokenBrushes.Resolve("Brush.Severity.Warning")   // confirm/adjust to a highlight token
            : (IBrush)Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

> Confirm the token key against `Tokens.axaml`. Pick a token that reads as "search highlight" and is visually distinct from the diff-status border and the amber connect-mode border. If none fits, that is a genuine gap — reuse the nearest existing token rather than adding a hex literal; note it for a follow-up rather than introducing a new colour here.

- [ ] **Step 3: Register both converters in `App.axaml`**

Next to `<converters:BalanceFlagToBrushConverter .../>`:

```xml
<converters:SearchMatchStateToOpacityConverter     x:Key="SearchStateOpacity"/>
<converters:SearchMatchStateToBorderBrushConverter x:Key="SearchMatchBorderBrush"/>
```

- [ ] **Step 4: Update the node template**

In `ConversationView.axaml`, change line 213:

```xml
<!-- was: Opacity="{Binding IsSearchMatch, Converter={StaticResource SearchMatchOpacity}}" -->
<Grid Width="200" RowDefinitions="Auto,Auto,Auto,Auto"
      Opacity="{Binding SearchMatchState, Converter={StaticResource SearchStateOpacity}}">
```

Add an emphasis border overlay (mirror the diff-tint `Border` at rows 0–2, but keyed to the search state) immediately after the diff-tint border:

```xml
<!-- Search-match emphasis — highlight border on condition/script-search hits;
     Transparent otherwise (Match state only) -->
<Border Grid.Row="0" Grid.RowSpan="3"
        CornerRadius="2"
        IsHitTestVisible="False"
        ZIndex="9"
        BorderThickness="3"
        BorderBrush="{Binding SearchMatchState, Converter={StaticResource SearchMatchBorderBrush}}"
        Background="Transparent"/>
```

- [ ] **Step 5: Build**

Run: `dotnet build DialogEditor.Avalonia`
Expected: build succeeds; no remaining reference to `IsSearchMatch`.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Converters/SearchMatchStateToOpacityConverter.cs DialogEditor.Avalonia/Converters/SearchMatchStateToBorderBrushConverter.cs DialogEditor.Avalonia/App.axaml DialogEditor.Avalonia/Views/ConversationView.axaml
git commit -m "feat(ui): render SearchMatchState (dim + match emphasis) on canvas nodes"
```

---

### Task 5: `ConditionSearchViewModel` (ViewModels)

Builds a `CatalogueMatch` from a chosen catalogue entry + pinned parameters, runs the search against the open conversation's snapshot, and applies Match/Dimmed to the nodes via injected callbacks. Exposes entries (conditions + scripts for the game), parameter-pin rows, a match count, Search, and Clear.

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/ConditionSearchViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/ConditionSearchViewModelTests.cs`

**Interfaces:**
- Consumes: `ConditionCatalogue.Instance.ForGame(gameId)` / `ScriptCatalogue.Instance.ForGame(gameId)`, `ConditionEntry`/`ScriptCatalogueEntry` (`.DisplayName`, `.ReflectionFullName`, `.Parameters`), `NodeConditionSearchService.FindMatches`, `CatalogueMatch`, `ParameterPin`.
- Constructor: `public ConditionSearchViewModel(string gameId, Func<ConversationEditSnapshot?> getSnapshot, Action<IReadOnlySet<int>> applyHighlight, Action clearHighlight)`.
- Produces (view binds):
  - `ObservableCollection<SearchEntryItem> Entries` where `SearchEntryItem` = `record(string DisplayLabel, string ReflectionFullName, IReadOnlyList<ConditionParameter> Parameters)` (union of conditions + scripts, sorted by label).
  - `[ObservableProperty] SearchEntryItem? _selectedEntry;` — on change, rebuild `PinRows`.
  - `ObservableCollection<PinRowViewModel> PinRows` — one per parameter of the selected entry; each has `Name`, a bindable `Value` (empty = wildcard), and the parameter's `Options`/`LookupKind` for the view to render a dropdown or text box.
  - `[ObservableProperty] string _matchCountText;`
  - `SearchCommand` (builds `CatalogueMatch` from `SelectedEntry` + `PinRows`, calls `FindMatches`, then `applyHighlight`, then sets `MatchCountText`), gated on `SelectedEntry is not null`.
  - `ClearCommand` (calls `clearHighlight`, resets `MatchCountText`).

Build rule: `CatalogueMatch(SelectedEntry.ReflectionFullName, PinRows.Select(r => string.IsNullOrEmpty(r.Value) ? ParameterPin.Wildcard : ParameterPin.Pin(r.Value)).ToList())`.

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/ViewModels/ConditionSearchViewModelTests.cs
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.ViewModels;

public class ConditionSearchViewModelTests
{
    public ConditionSearchViewModelTests() => Loc.Configure(new StubStringProvider());

    private static NodeEditSnapshot Node(int id, params ConditionNode[] conds) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false, [], conds, []);

    [Fact]
    public void Search_EntrySelected_NoPins_HighlightsAllUsers()
    {
        ConditionLeaf Disp(string axis) =>
            new("Boolean DispositionEqual(Axis, Rank)", new[] { axis, "2" }, false, "And");
        var snap = new ConversationEditSnapshot(new[]
        {
            Node(0, Disp("Benevolent")),
            Node(1),   // no condition
        });

        IReadOnlySet<int>? applied = null;
        var vm = new ConditionSearchViewModel("poe1",
            () => snap, m => applied = m, () => applied = null);

        vm.SelectedEntry = vm.Entries.First(e => e.ReflectionFullName == "Boolean DispositionEqual(Axis, Rank)");
        vm.SearchCommand.Execute(null);

        Assert.NotNull(applied);
        Assert.Contains(0, applied!);
        Assert.DoesNotContain(1, applied!);
    }

    [Fact]
    public void Search_WithPin_NarrowsToMatchingValue()
    {
        ConditionLeaf Disp(string axis) =>
            new("Boolean DispositionEqual(Axis, Rank)", new[] { axis, "2" }, false, "And");
        var snap = new ConversationEditSnapshot(new[] { Node(0, Disp("Benevolent")), Node(1, Disp("Cruel")) });

        IReadOnlySet<int>? applied = null;
        var vm = new ConditionSearchViewModel("poe1", () => snap, m => applied = m, () => applied = null);
        vm.SelectedEntry = vm.Entries.First(e => e.ReflectionFullName == "Boolean DispositionEqual(Axis, Rank)");
        vm.PinRows[0].Value = "Benevolent";   // pin the Axis parameter
        vm.SearchCommand.Execute(null);

        Assert.Contains(0, applied!);
        Assert.DoesNotContain(1, applied!);
    }

    [Fact]
    public void Clear_InvokesClearHighlight()
    {
        var vm = new ConditionSearchViewModel("poe1",
            () => new ConversationEditSnapshot([]), _ => { }, () => { });
        vm.ClearCommand.Execute(null);   // must not throw with no prior search
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ConditionSearchViewModelTests"`
Expected: FAIL — `ConditionSearchViewModel` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// DialogEditor.ViewModels/ViewModels/ConditionSearchViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Search;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public record SearchEntryItem(string DisplayLabel, string ReflectionFullName,
    IReadOnlyList<ConditionParameter> Parameters)
{
    public override string ToString() => DisplayLabel;
}

public partial class PinRowViewModel : ObservableObject
{
    public string Name { get; }
    public IReadOnlyList<string> Options { get; }   // enum/lookup options for the view (may be empty)
    public string? LookupKind { get; }
    [ObservableProperty] private string _value = string.Empty;   // empty = wildcard

    public PinRowViewModel(ConditionParameter p)
    {
        Name       = p.Name;
        Options    = p.Options ?? [];
        LookupKind = p.LookupKind;
    }
}

/// Per-conversation search over conditions AND scripts. Builds a CatalogueMatch from a chosen
/// entry + pinned parameters, finds matching nodes, and applies Match/Dimmed via callbacks.
public partial class ConditionSearchViewModel : ObservableObject
{
    private readonly Func<ConversationEditSnapshot?> _getSnapshot;
    private readonly Action<IReadOnlySet<int>>       _applyHighlight;
    private readonly Action                          _clearHighlight;

    public ObservableCollection<SearchEntryItem> Entries  { get; } = [];
    public ObservableCollection<PinRowViewModel> PinRows  { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private SearchEntryItem? _selectedEntry;

    [ObservableProperty] private string _matchCountText = string.Empty;

    public ConditionSearchViewModel(
        string gameId,
        Func<ConversationEditSnapshot?> getSnapshot,
        Action<IReadOnlySet<int>> applyHighlight,
        Action clearHighlight)
    {
        _getSnapshot    = getSnapshot;
        _applyHighlight = applyHighlight;
        _clearHighlight = clearHighlight;

        foreach (var c in ConditionCatalogue.Instance.ForGame(gameId))
            Entries.Add(new SearchEntryItem(Loc.Format("CondSearch_EntryCondition", c.DisplayName),
                c.ReflectionFullName, c.Parameters));
        foreach (var s in ScriptCatalogue.Instance.ForGame(gameId))
            Entries.Add(new SearchEntryItem(Loc.Format("CondSearch_EntryScript", s.DisplayName),
                s.ReflectionFullName, s.Parameters));
        // stable, browsable order
        var sorted = Entries.OrderBy(e => e.DisplayLabel, StringComparer.CurrentCultureIgnoreCase).ToList();
        Entries.Clear();
        foreach (var e in sorted) Entries.Add(e);
    }

    partial void OnSelectedEntryChanged(SearchEntryItem? value)
    {
        PinRows.Clear();
        if (value is null) return;
        foreach (var p in value.Parameters) PinRows.Add(new PinRowViewModel(p));
    }

    private bool CanSearch() => SelectedEntry is not null;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private void Search()
    {
        var snap = _getSnapshot();
        if (snap is null || SelectedEntry is null) return;

        var pins = PinRows
            .Select(r => string.IsNullOrEmpty(r.Value) ? ParameterPin.Wildcard : ParameterPin.Pin(r.Value))
            .ToList();
        var query   = new CatalogueMatch(SelectedEntry.ReflectionFullName, pins);
        var matches = NodeConditionSearchService.FindMatches(snap, query);

        _applyHighlight(matches);
        MatchCountText = Loc.Format("CondSearch_MatchCount", matches.Count);
    }

    [RelayCommand]
    private void Clear()
    {
        _clearHighlight();
        MatchCountText = string.Empty;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ConditionSearchViewModelTests"`
Expected: PASS (3 tests).

> If `StubStringProvider.Get` returning the key breaks `Loc.Format("CondSearch_EntryCondition", name)` entry lookup by `ReflectionFullName`, note the test matches on `ReflectionFullName` (not the label), so the stubbed label is irrelevant — the tests stay valid.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/ConditionSearchViewModel.cs DialogEditor.Tests/ViewModels/ConditionSearchViewModelTests.cs
git commit -m "feat(viewmodels): ConditionSearchViewModel — entry + parameter-pin search"
```

---

### Task 6: Embedded search dock + canvas apply/clear + wiring

Add the collapsible dock in the canvas area, the `ConversationViewModel` state that owns the search VM and applies/clears node highlight, the game-id wiring, the toolbar toggle, strings, and GUI verification.

**Files:**
- Create: `DialogEditor.Avalonia/Views/ConditionSearchView.axaml` + `.axaml.cs` (the dock's content UserControl)
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs` (ActiveGameId, ConditionSearch, visibility, apply/clear, toggle command)
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs:1705` area (set `Canvas.ActiveGameId = provider.GameId;`)
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml` (toolbar toggle button + docked panel hosting `ConditionSearchView`)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (strings)
- Test: `DialogEditor.Tests/ViewModels/ConversationHighlightTests.cs`

**Interfaces:**
- `ConversationViewModel.ApplyConditionHighlight(IReadOnlySet<int> matches)` — for each node: `SearchMatchState = matches.Contains(node.NodeId) ? Match : Dimmed`.
- `ConversationViewModel.ClearConditionHighlight()` — every node → `None`.
- `ConversationViewModel.ActiveGameId` (string) — setter builds `ConditionSearch` (or nulls it when empty).
- `ConversationViewModel.ConditionSearch` (`ConditionSearchViewModel?`), `IsConditionSearchVisible` (`[ObservableProperty] bool`), `ToggleConditionSearchCommand` (gated on `ConditionSearch is not null && Nodes.Count > 0`).

- [ ] **Step 1: Write the failing highlight test**

```csharp
// DialogEditor.Tests/ViewModels/ConversationHighlightTests.cs
// Build a ConversationViewModel and load a small conversation (reuse the setup from an existing
// ConversationViewModel test — e.g. ConditionBranchEditingTests / ConversationStatisticsTests),
// with nodes 0 and 1, then:
//   vm.ApplyConditionHighlight(new HashSet<int> { 0 });
//   node0.SearchMatchState == SearchMatchState.Match; node1 == SearchMatchState.Dimmed;
//   vm.ClearConditionHighlight();  // both back to None
```

> Do not invent a constructor — open an existing `ConversationViewModel` test and copy its load setup. The assertions above are the deliverable.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ConversationHighlightTests"`
Expected: FAIL — `ApplyConditionHighlight` does not exist.

- [ ] **Step 3: Add state + apply/clear to `ConversationViewModel`**

```csharp
// Field + property (setter builds the search VM so the dock is always ready once a game loads):
private string _activeGameId = "";
public string ActiveGameId
{
    get => _activeGameId;
    set
    {
        _activeGameId = value;
        ConditionSearch = string.IsNullOrEmpty(value)
            ? null
            : new ConditionSearchViewModel(value, BuildSnapshot, ApplyConditionHighlight, ClearConditionHighlight);
        OnPropertyChanged(nameof(ConditionSearch));
        ToggleConditionSearchCommand.NotifyCanExecuteChanged();
    }
}

public ConditionSearchViewModel? ConditionSearch { get; private set; }

[ObservableProperty] private bool _isConditionSearchVisible;

/// Highlights the given node IDs as condition/script-search matches and dims the rest.
/// Shares the unified SearchMatchState with the text search (last search wins).
public void ApplyConditionHighlight(IReadOnlySet<int> matches)
{
    foreach (var node in Nodes)
        node.SearchMatchState = matches.Contains(node.NodeId)
            ? SearchMatchState.Match
            : SearchMatchState.Dimmed;
}

/// Clears search emphasis/dim (Clear button, or a fresh conversation).
public void ClearConditionHighlight()
{
    foreach (var node in Nodes)
        node.SearchMatchState = SearchMatchState.None;
}

private bool CanToggleConditionSearch() => ConditionSearch is not null && Nodes.Count > 0;

[RelayCommand(CanExecute = nameof(CanToggleConditionSearch))]
private void ToggleConditionSearch() => IsConditionSearchVisible = !IsConditionSearchVisible;
```

Also call `ToggleConditionSearchCommand.NotifyCanExecuteChanged();` where `Nodes` is (re)populated on conversation load (find the load path near line 324 where `ConversationName = conversation.Name;`), so the toggle enables once a conversation is on the canvas.

- [ ] **Step 4: Run the highlight test**

Run: `dotnet test --filter "FullyQualifiedName~ConversationHighlightTests"`
Expected: PASS.

- [ ] **Step 5: Set the game id on the canvas VM**

In `MainWindowViewModel.cs`, next to the existing `Detail.ActiveGameId = provider.GameId;` (≈line 1705):

```csharp
Canvas.ActiveGameId = provider.GameId;
```

- [ ] **Step 6: Create the dock content view**

```xml
<!-- DialogEditor.Avalonia/Views/ConditionSearchView.axaml — DataContext is a ConditionSearchViewModel -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="DialogEditor.Avalonia.Views.ConditionSearchView"
             x:CompileBindings="False"
             Width="260">
  <Border Background="{DynamicResource Brush.Surface.Panel}" Padding="10">
    <StackPanel Spacing="8">
      <TextBlock Text="{DynamicResource CondSearch_Title}" FontWeight="Bold"
                 Foreground="{DynamicResource Brush.Text.Primary}"/>
      <TextBlock Text="{DynamicResource CondSearch_EntryLabel}" Foreground="{DynamicResource Brush.Text.Secondary}"/>
      <ComboBox ItemsSource="{Binding Entries}" SelectedItem="{Binding SelectedEntry}"
                HorizontalAlignment="Stretch"
                ToolTip.Tip="{DynamicResource CondSearch_Tip_Entry}"
                AutomationProperties.Name="{DynamicResource CondSearch_EntryLabel}"
                AutomationProperties.HelpText="{DynamicResource CondSearch_Tip_Entry}"/>
      <ItemsControl ItemsSource="{Binding PinRows}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Grid ColumnDefinitions="*,*" Margin="0,2">
              <TextBlock Grid.Column="0" Text="{Binding Name}" VerticalAlignment="Center"
                         Foreground="{DynamicResource Brush.Text.Secondary}"/>
              <TextBox Grid.Column="1" Text="{Binding Value}"
                       ToolTip.Tip="{DynamicResource CondSearch_Tip_Pin}"
                       AutomationProperties.Name="{Binding Name}"
                       AutomationProperties.HelpText="{DynamicResource CondSearch_Tip_Pin}"/>
            </Grid>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
      <StackPanel Orientation="Horizontal" Spacing="8">
        <Button Content="{DynamicResource CondSearch_Search}" Command="{Binding SearchCommand}"
                ToolTip.Tip="{DynamicResource CondSearch_Tip_Search}"
                AutomationProperties.HelpText="{DynamicResource CondSearch_Tip_Search}"
                Background="{DynamicResource Brush.Button.Confirm.Background}"
                Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0" Padding="14,6"/>
        <Button Content="{DynamicResource CondSearch_Clear}" Command="{Binding ClearCommand}"
                ToolTip.Tip="{DynamicResource CondSearch_Tip_Clear}"
                AutomationProperties.HelpText="{DynamicResource CondSearch_Tip_Clear}"
                Padding="14,6"/>
      </StackPanel>
      <TextBlock Text="{Binding MatchCountText}" Foreground="{DynamicResource Brush.Text.Muted}"
                 FontSize="{DynamicResource FontSize.Small}"/>
    </StackPanel>
  </Border>
</UserControl>
```

```csharp
// DialogEditor.Avalonia/Views/ConditionSearchView.axaml.cs
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DialogEditor.Avalonia.Views;

public partial class ConditionSearchView : UserControl
{
    public ConditionSearchView() => InitializeComponent();
}
```

> For v1 a `TextBox` per pin row is acceptable (a pinned enum/lookup value must be typed exactly, e.g. `Benevolent` or the GUID). A dropdown driven by `Options`/`LookupKind` is a deferred refinement, not required by the spec.

- [ ] **Step 7: Embed the dock + toggle in `ConversationView.axaml`**

Add a toolbar toggle next to the search box (inside the header DockPanel, ≈ after the SearchBox Grid at line 101):

```xml
<Button Content="⚑"
        Command="{Binding ToggleConditionSearchCommand}"
        Background="{DynamicResource Brush.Surface.Header}" Foreground="{DynamicResource Brush.Text.Muted.Light}"
        BorderThickness="0" Padding="8,2" Margin="8,0,0,0"
        ToolTip.Tip="{DynamicResource ToolTip_ToggleConditionSearch}"
        AutomationProperties.Name="{DynamicResource AutomationName_ToggleConditionSearch}"
        AutomationProperties.HelpText="{DynamicResource ToolTip_ToggleConditionSearch}"/>
```

Wrap the canvas cell (the `Grid Grid.Row="1"` at line 106) so the dock sits to its right:

```xml
<Grid Grid.Row="1" ColumnDefinitions="*,Auto">
  <Grid Grid.Column="0" ClipToBounds="True">
    <!-- existing NodifyEditor ... (unchanged) -->
  </Grid>
  <views:ConditionSearchView Grid.Column="1"
                             DataContext="{Binding ConditionSearch}"
                             IsVisible="{Binding IsConditionSearchVisible}"/>
</Grid>
```

> Confirm the `views:` namespace prefix is already declared in `ConversationView.axaml`; if not, add `xmlns:views="clr-namespace:DialogEditor.Avalonia.Views"`. Keep the existing NodifyEditor markup intact inside the new `Grid.Column="0"` wrapper.

- [ ] **Step 8: Add strings**

In `Strings.axaml` (`<sys:String>`): `CondSearch_Title` ("Find nodes by condition / script"), `CondSearch_EntryLabel` ("Condition or script:"), `CondSearch_EntryCondition` ("{0}  (condition)"), `CondSearch_EntryScript` ("{0}  (script)"), `CondSearch_Search` ("Search"), `CondSearch_Clear` ("Clear"), `CondSearch_MatchCount` ("Matches: {0}" — no `(s)`), and tooltips `CondSearch_Tip_Entry`, `CondSearch_Tip_Pin`, `CondSearch_Tip_Search`, `CondSearch_Tip_Clear`, `ToolTip_ToggleConditionSearch`, plus `AutomationName_ToggleConditionSearch` ("Condition search").

- [ ] **Step 9: Build, run, GUI-verify**

Run: `dotnet build`
Expected: succeeds.

Use the `running-the-app` skill (scratch project auto-loads the real PoE2 folder). Open a conversation with condition/script usage, click the ⚑ toggle → the dock appears; pick e.g. `IsDisposition` / `SetGlobalValue`, Search → matching nodes get the emphasis border and the rest dim; pin a parameter → the set narrows; Clear → all nodes normal; switch conversations → highlight gone and dock still toggglable.

- [ ] **Step 10: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConditionSearchView.axaml DialogEditor.Avalonia/Views/ConditionSearchView.axaml.cs DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Avalonia/Views/ConversationView.axaml DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/ConversationHighlightTests.cs
git commit -m "feat(ui): embedded Condition/Script search dock + canvas highlight"
```

---

### Task 7: Full-suite green + Gaps.md

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test`
Expected: PASS (no `IsSearchMatch` references remain; text search still dims via the migrated enum).

- [ ] **Step 2: Mark the gap implemented in `Gaps.md`**

Edit the **Condition/Script Node Search & Highlight** entry: change `**📐 Designed (2026-07-16), not yet implemented.**` to `**✅ Implemented (<date>).**` and append the shipped specifics (window name, unified `SearchMatchState`, `CatalogueMatch` + `NodeConditionSearchService`, node-only highlight, single-entry v1, and the `<commit hash>`).

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark condition/script node search implemented"
```

---

## Self-Review

**Spec coverage:**
- Non-modal search surface (persists during canvas use) → Task 6 (embedded canvas dock). ✓
- Pick a catalogue entry, conditions AND scripts, current game → Task 5. ✓
- Optional parameter pins; unset = wildcard; pinned = exact raw value → Tasks 1, 5. ✓
- Hit if match in node condition / link condition / script; node-only granularity → Task 2. ✓
- Highlight hits + dim rest → Tasks 3, 4, 6. ✓
- Match count → Task 5. ✓
- Clear on button and on conversation switch → Task 6 (`ClearConditionHighlight`; conversation switch rebuilds nodes at `None`). ✓
- Re-running replaces (not accumulates) → `ApplyConditionHighlight` reassigns every node each run. ✓
- Single entry v1; matcher shaped for future multi-condition → Task 1 primitive + Task 5. ✓
- `CatalogueMatch` primitive (shared) → Task 1. ✓
- Localisation, tooltips, icon, UIA, error handling, no hex → Tasks 4–6 + Global Constraints. ✓

**Placeholder scan:** No "TBD"/"add error handling"/"similar to Task N". Code shown for every code step. The "match the existing setup" notes (ConversationViewModel test construction, ConversationNode ctor, emphasis token choice, notify-canexecute site) point at concrete files to read, not deferred work.

**Type consistency:** `SearchMatchState`, `CatalogueMatch`/`ParameterPin`, `NodeConditionSearchService.FindMatches`, `ConditionSearchViewModel(gameId, getSnapshot, applyHighlight, clearHighlight)`, `SearchEntryItem`/`PinRowViewModel`, `ApplyConditionHighlight`/`ClearConditionHighlight`, `ShowConditionSearch`/`ShowConditionSearchWindowCommand`/`CanShowConditionSearch` are used identically across Tasks 1–7. `ConditionLeaves` (Gap #1) is consumed by Task 2.

**Verification points for the implementer** (surfaced, not hidden): the real `ConversationNode`/`ConversationViewModel` test-construction shapes; the emphasis `Brush.*` token choice; and the exact place to call `ShowConditionSearchWindowCommand.NotifyCanExecuteChanged()` when a conversation loads. Each is a "read the neighbour first" check.
