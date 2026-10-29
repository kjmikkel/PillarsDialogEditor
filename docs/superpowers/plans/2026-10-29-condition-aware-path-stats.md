# Condition-aware path stats and canvas — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

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

- [x] Tests: no facts + `AlwaysFalse` link → false; link False → true; target False with empty link list → true;
      link Unknown and target Unknown → false; both True → false. Use an `IsGlobalValue` leaf with a set/unset
      global (see `ConditionEvaluatorTests` for how leaves are built).
- [x] Implement; green.

### Task 2: `PathStatsService` with an assumed state

**Files:** `DialogEditor.Core/Analytics/PathStatsService.cs`, `PathStatsModels.cs`,
`DialogEditor.Tests/Analytics/PathStatsServiceTests.cs`.

- [x] Both `Analyze` overloads take optional `GameState? state = null, string gameId = ""`.
- [x] `LinksOf(u)` drops links where `LinkFiring.CannotFire(link.Conditions ?? [], target.Conditions, …)`.
- [x] Endings: "no live link and no handoff" (use the filtered links, not `Links.Count`).
- [x] `PathStatsReport.BlockedLinkCount` (init property, default 0): links in the graph that cannot fire.
- [x] Tests: blocked branch drops out of longest/shortest; blocked choice leaves the fork tree; node with all
      links blocked is an ending; Unknown keeps the link; `NoFacts_FiguresUnchanged` (state with no facts and
      an `AlwaysFalse` link equals the no-state report); `BlockedLinkCount`; handoffs not pruned; root kept even
      if its own conditions are False.

### Task 3: Flow Analytics window

**Files:** `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`,
`DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`, `…/MainWindow.axaml.cs` (`FlowAnalytics_Click`),
`Strings.axaml`, `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs`.

- [x] Ctor optional `GameState? assumedState = null` (game id: existing `gameId`). Subscribe to `Changed` →
      refresh; `public void Detach()` unsubscribes; window `Closed` calls it.
- [x] `HasAssumedState` (state has a fact), `[ObservableProperty] bool UseAssumedState = true` (change → refresh),
      `PrunedLinksText` / `HasPrunedLinks` (only while used and count > 0).
- [x] `RefreshPathStats` passes the state only when `UseAssumedState && HasAssumedState`.
- [x] XAML: checkbox in the header grid (new column), visible on `HasAssumedState`, tooltip + UIA name;
      muted pruned-count line below the header. Strings: `PathStats_UseAssumedState`,
      `PathStats_UseAssumedState_Tip`, `PathStats_PrunedLinks` (pluralised like other counts — check
      `Loc.FormatCount`).
- [x] Tests: state change re-runs and changes the longest figure; unchecking restores the upper bound; Detach
      stops refreshes; HasAssumedState false with empty state.

### Task 4: Canvas fades links that cannot fire

**Files:** `ConnectionViewModel.cs`, `ConversationViewModel.cs`, `MainWindowViewModel.cs`,
`ConversationView.axaml`, `LegendWindow.axaml`, `Strings.axaml`, tests in `DialogEditor.Tests/ViewModels/`.

- [x] `ConnectionViewModel.CannotFire` (uses `_assumedState`, `_verdictGameId`, `Target.Owner?.Conditions`) and
      `CannotFireToolTip` (string or null); both raised from `RefreshVerdict()`.
- [x] `ConversationViewModel.AssumedState` (GameState?): on set, subscribe to `Changed`; call
      `SetVerdictContext(state, ActiveGameId)` on every connection, on connections added later, and when
      `ActiveGameId` changes; on a node's `Conditions` change, `RefreshVerdict()` the connections into it; on
      `Changed`, refresh all.
- [x] `MainWindowViewModel` ctor: `Canvas.AssumedState = GameState.State`.
- [x] XAML: `Classes.cannotFire="{Binding CannotFire}"`, `ToolTip.Tip="{Binding CannotFireToolTip}"`, style
      `nodify|Connection.cannotFire` → `Opacity 0.35`. Legend row with a faded swatch + help text.
- [x] Tests: connection with False link conditions → CannotFire after state change; target node conditions
      edited to False → CannotFire; connection added after the state was set gets it; no facts → false.

### Task 5: Verification (orchestrator)

- [x] Headless suite green.
- [x] Run the app (`running-the-app`), set a global in Game State so a link fails, see the faded link and the
      changed Flow Analytics figures; screenshots.

## Handoff — 2026-10-29

**Done** (branch `feat/32-condition-aware-path-stats`; the PR closes #32):
- Spec + plan (`75306986`).
- Core: `LinkFiring.CannotFire`, plus `PathStatsService.Analyze(…, GameState? state, string gameId)` with `BlockedLinkCount` (`1ba305af`), and the blocked verdict memoised per link (`879f38ef`).
- Flow Analytics: a "Use assumed game state" checkbox (shown only with a fact set, on by default, not persisted) and a pruned-links line. It re-runs on `GameState.Changed`, and the window's `Closed` calls `Detach()` (`11a1a22a`).
- Canvas: `ConnectionViewModel.CannotFire` / `CannotFireToolTip`; `ConversationViewModel.AssumedState` keeps every connection pointed at the session state; `nodify|Connection.cannotFire` fades the link to opacity 0.35; there is a new Legend row (`f2b10546`).
- Headless suite green. GUI check on the canonical PoE2 fixture: setting `canonical_flag_d = 9` (node 2 needs `< 5`) faded link 0→2. Flow Analytics then showed "1 link can't fire…", and "Draw from the bank" left the fork tree.
- Follow-up filed: #353 (first-passing-NPC-link rule).

**Remaining:** nothing in this plan. Review the _(my call)_ items in the spec.

**Next step for whoever picks this up:** review and merge the PR. If #351 or #352 (Game State panel button, saving the state with the project) merge first, rebase. This branch does not touch `GameStateViewModel`, so no conflict is expected. Both PRs replace or fill the same `GameState` instance, and every consumer here listens to `Changed`. Check that the loaded state calls `NotifyChanged()` so that the canvas and Flow Analytics refresh.

**Open questions:** none blocking. The spec's _(my call)_ list is the review surface: replace with a toggle instead of a second column, endings defined as "no live link", node cards not dimmed, pruning only once a fact is set.

**Do not redo:** the checkbox originally sat beside the header. It moved to its own line because `NoClippedTextTests` failed, so don't move it back.
