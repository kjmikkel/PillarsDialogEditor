# Condition-aware path stats and canvas — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Path stats, the fork tree and the per-ending figures skip links that cannot fire under the assumed
Game State, and the canvas fades those links.

**Architecture:** One Core predicate, `LinkFiring.CannotFire`, encodes the game's rule (link conditions False or
target node conditions False; quiet while no fact is set). `PathStatsService` filters its single `LinksOf` through
it; `ConnectionViewModel` exposes it for the canvas. The session `GameState` from `MainWindowViewModel.GameState`
is handed to the Flow Analytics VM and to the canvas VM.

**Tech Stack:** .NET 10, Avalonia, CommunityToolkit.Mvvm, Nodify, xUnit (+ Avalonia.Headless).

**Spec:** `docs/superpowers/specs/2026-10-29-condition-aware-path-stats-design.md` (read it first).

## Global Constraints

- Strict red/green TDD (CLAUDE.md): a failing test first, then the minimum code.
- No user-visible string inline in C# or XAML: UI strings in `DialogEditor.Avalonia/Resources/Strings.axaml`
  (read via `Loc.Get` / `Loc.Format` / `{DynamicResource}`). Check whether other `Strings.*.axaml` locale files
  exist and need the same keys (follow what the #305 commits did).
- Every interactive control has a detailed `ToolTip` and a UIA name.
- Source files carry a UTF-8 BOM; keep it.
- Tests headless only: `dotnet test DialogEditor.Tests --filter "Category!=Gui"` — never bare `dotnet test`.
- Game ids are `"poe1"` / `"poe2"`.
- Only `Truth.False` prunes. `Unknown` stays reachable.

## Review Focus

1. No fact set ⇒ every figure identical to today (Task 2 test `NoFacts_FiguresUnchanged`).
2. Unknown never prunes (Task 1 test `Unknown_DoesNotBlock`).
3. Target node False blocks the link even when the link has no conditions (Task 1).
4. A node whose every link fails becomes an ending (Task 2).
5. The Flow Analytics VM unsubscribes from `GameState.Changed` on `Detach()` (Task 3).

---

### Task 1: `LinkFiring.CannotFire`

**Files:** create `DialogEditor.Core/Conditions/LinkFiring.cs`, `DialogEditor.Tests/Conditions/LinkFiringTests.cs`.

API: `public static bool CannotFire(IReadOnlyList<ConditionNode> linkConditions,
IReadOnlyList<ConditionNode> targetConditions, GameState state, string gameId)`.
`false` if `!state.HasAnyFact`; otherwise `Evaluate(link) == False || Evaluate(target) == False`.

- [ ] Tests: no facts + `AlwaysFalse` link → false; link False → true; target False with empty link list → true;
      link Unknown and target Unknown → false; both True → false. Use an `IsGlobalValue` leaf with a set/unset
      global (see `ConditionEvaluatorTests` for how leaves are built).
- [ ] Implement; green.

### Task 2: `PathStatsService` with an assumed state

**Files:** `DialogEditor.Core/Analytics/PathStatsService.cs`, `PathStatsModels.cs`,
`DialogEditor.Tests/Analytics/PathStatsServiceTests.cs`.

- [ ] Both `Analyze` overloads take optional `GameState? state = null, string gameId = ""`.
- [ ] `LinksOf(u)` drops links where `LinkFiring.CannotFire(link.Conditions ?? [], target.Conditions, …)`.
- [ ] Endings: "no live link and no handoff" (use the filtered links, not `Links.Count`).
- [ ] `PathStatsReport.BlockedLinkCount` (init property, default 0): links in the graph that cannot fire.
- [ ] Tests: blocked branch drops out of longest/shortest; blocked choice leaves the fork tree; node with all
      links blocked is an ending; Unknown keeps the link; `NoFacts_FiguresUnchanged` (state with no facts and
      an `AlwaysFalse` link equals the no-state report); `BlockedLinkCount`; handoffs not pruned; root kept even
      if its own conditions are False.

### Task 3: Flow Analytics window

**Files:** `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`,
`DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`, `…/MainWindow.axaml.cs` (`FlowAnalytics_Click`),
`Strings.axaml`, `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs`.

- [ ] Ctor optional `GameState? assumedState = null` (game id: existing `gameId`). Subscribe to `Changed` →
      refresh; `public void Detach()` unsubscribes; window `Closed` calls it.
- [ ] `HasAssumedState` (state has a fact), `[ObservableProperty] bool UseAssumedState = true` (change → refresh),
      `PrunedLinksText` / `HasPrunedLinks` (only while used and count > 0).
- [ ] `RefreshPathStats` passes the state only when `UseAssumedState && HasAssumedState`.
- [ ] XAML: checkbox in the header grid (new column), visible on `HasAssumedState`, tooltip + UIA name;
      muted pruned-count line below the header. Strings: `PathStats_UseAssumedState`,
      `PathStats_UseAssumedState_Tip`, `PathStats_PrunedLinks` (pluralised like other counts — check
      `Loc.FormatCount`).
- [ ] Tests: state change re-runs and changes the longest figure; unchecking restores the upper bound; Detach
      stops refreshes; HasAssumedState false with empty state.

### Task 4: Canvas fades links that cannot fire

**Files:** `ConnectionViewModel.cs`, `ConversationViewModel.cs`, `MainWindowViewModel.cs`,
`ConversationView.axaml`, `LegendWindow.axaml`, `Strings.axaml`, tests in `DialogEditor.Tests/ViewModels/`.

- [ ] `ConnectionViewModel.CannotFire` (uses `_assumedState`, `_verdictGameId`, `Target.Owner?.Conditions`) and
      `CannotFireToolTip` (string or null); both raised from `RefreshVerdict()`.
- [ ] `ConversationViewModel.AssumedState` (GameState?): on set, subscribe to `Changed`; call
      `SetVerdictContext(state, ActiveGameId)` on every connection, on connections added later, and when
      `ActiveGameId` changes; on a node's `Conditions` change, `RefreshVerdict()` the connections into it; on
      `Changed`, refresh all.
- [ ] `MainWindowViewModel` ctor: `Canvas.AssumedState = GameState.State`.
- [ ] XAML: `Classes.cannotFire="{Binding CannotFire}"`, `ToolTip.Tip="{Binding CannotFireToolTip}"`, style
      `nodify|Connection.cannotFire` → `Opacity 0.35`. Legend row with a faded swatch + help text.
- [ ] Tests: connection with False link conditions → CannotFire after state change; target node conditions
      edited to False → CannotFire; connection added after the state was set gets it; no facts → false.

### Task 5: Verification (orchestrator)

- [ ] Headless suite green.
- [ ] Run the app (`running-the-app`), set a global in Game State so a link fails, see the faded link and the
      changed Flow Analytics figures; screenshots.
