# Condition evaluator and Game State panel — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Decide true / false / can't tell for a condition list against an assumed game state, let the writer set
that state in a Game State dock tool, and show the verdict in Node Details.

**Architecture:** Pure Core evaluator (`DialogEditor.Core/Conditions/`) transcribing each game's left-to-right
fold, lifted to three values by enumerating unknown leaves. A `GameStateViewModel` owned by `MainWindowViewModel`
holds one session-wide `GameState`; Node Details and the new dock tool read it.

**Tech Stack:** .NET, Avalonia, CommunityToolkit.Mvvm, Dock.Avalonia, xUnit (+ Avalonia.Headless).

**Spec:** `docs/superpowers/specs/2026-10-28-condition-evaluator-design.md` (read it; §2 has the exact folds).

## Global Constraints

- Strict red/green TDD (CLAUDE.md). Tests in `DialogEditor.Tests` mirroring Core (`DialogEditor.Tests/Conditions/`).
- No user-visible string inline in C# or XAML: Core strings in `DialogEditor.Core/Resources/Strings.resx`,
  UI strings in the existing `Strings.axaml` resource dictionaries, read via `Loc.Get` / `{DynamicResource}`.
- Every interactive control has a detailed `ToolTip` and a UIA name (`AutomationProperties.Name` where the
  control has no text of its own).
- Every caught exception in production code is logged with `AppLog.Warn/Error` (no bare `catch { }`).
- Source files carry a UTF-8 BOM; keep it.
- Run tests headless: `dotnet test DialogEditor.Tests --filter "Category!=Gui"` (bare `dotnet test` launches the GUI).
- Game ids are the strings `"poe1"` / `"poe2"` (as `IGameDataProvider.GameId`).
- Dock Id `"GameState"` is persisted in layout.json; never change it once written.

## Review Focus

1. A conversation whose saved layout.json predates this change: View ▸ Game State must still open the tool in
   the left pane (Task 5 test `ShowGameState_WithLayoutLackingTheTool_AddsItToTheLeftPane`).
2. A global box holding non-numeric text or an empty string: treated as "not set", never throws
   (Task 3 test `GlobalRow_NonNumericText_LeavesTheFactUnset`).
3. Operator strings drifting from the enum (`Equals`, lower case `and`): `Equals` = `EqualTo`; And/Or compared
   case-insensitively (Task 1/2 tests).
4. Switching conversation keeps values set earlier (globals are game-wide) (Task 3 test
   `SwitchingConversation_KeepsEarlierValues`).
5. Many unknown leaves (>12) must not hang the UI: result Unknown without enumeration (Task 1 test).

---

### Task 1: Truth, GameState and the two game folds

**Files:**
- Create: `DialogEditor.Core/Conditions/Truth.cs`, `GameState.cs`, `QuestStatus.cs`, `ConditionFold.cs`,
  `ConditionEvaluator.cs`
- Test: `DialogEditor.Tests/Conditions/ConditionFoldTests.cs`, `ConditionEvaluatorTests.cs`

**Interfaces:**
- Produces:
  - `public enum Truth { True, False, Unknown }`
  - `public enum QuestStatus { NotStarted, Active, Completed, Failed }`
  - `public sealed class GameState` with `IDictionary<string,int> Globals` (comparer per Task 2 finding; start
    `StringComparer.OrdinalIgnoreCase`), `IDictionary<string,bool> CompanionsInParty` (OrdinalIgnoreCase),
    `bool NoCompanionsInParty`, `IDictionary<(string Conversation,int NodeId),bool> PlayedNodes` (conversation
    compared OrdinalIgnoreCase — use a custom comparer), `IDictionary<string,QuestStatus> Quests`
    (OrdinalIgnoreCase), `bool? InCombat`, `bool HasAnyFact`, `event EventHandler? Changed`,
    `void NotifyChanged()`, `void Clear()` (clears all and raises Changed).
  - `internal static class ConditionFold { bool Poe1(IReadOnlyList<(bool Result, bool NextIsAnd)> c);
    bool Poe2(...same) }` — literal transcriptions of spec §2. `InternalsVisibleTo` for tests already exists? If
    not, make them `public`.
  - `public static class ConditionEvaluator { Truth Evaluate(IReadOnlyList<ConditionNode> list, GameState s,
    string gameId); }` plus an internal seam `Func<ConditionLeaf, GameState, Truth> LeafEvaluator` defaulting to
    Task 2's table (Task 1 tests inject a stub keyed by `FullName`).

- [x] **Step 1: Write failing fold tests** — `ConditionFoldTests`:
  - `Poe2_AndThenFalse_ReturnsFalse`: `[(true,And),(false,…)]` → false.
  - `Poe2_SingleTrue_ReturnsTrue`; empty handled by evaluator not fold.
  - `Poe1AndPoe2_Disagree_OnAOrBAndC`: components A=false(Or), B=true(And), C=false(last). PoE2: i=0 next=Or,
    r=false → continue; i=1 next=And, r=true → continue; last prev=And → false. PoE1: i=0 next=Or r=false →
    continue; i=1 middle prev=Or, r=true → **true**. Assert Poe1 true, Poe2 false.
  - `Poe2_TrueOrAnything_ShortCircuitsTrue`: `[(true,Or),(false,And),(false,…)]` → true.
  - `Poe1_Middle_AndFalse_ReturnsFalse`: `[(true,And),(false,Or),(true,…)]` → false.
- [x] **Step 2: Run, verify FAIL** — `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConditionFold"`.
- [x] **Step 3: Implement `ConditionFold`** transcribing spec §2 exactly (keep the decompiled shape; add a comment
  naming the game method each mirrors).
- [x] **Step 4: Write failing evaluator tests** — `ConditionEvaluatorTests` (stub leaf evaluator by name: `T()`
  True, `F()` False, `U()` Unknown, `U2()` a second independent Unknown):
  - `EmptyList_IsTrue`.
  - `NotOnLeaf_FlipsTrueAndFalse_LeavesUnknown`.
  - `UnknownAndFalse_IsFalse` (`U() And F()`, poe2) → False; `UnknownOrTrue_IsTrue` → True; `UnknownAndTrue_IsUnknown`.
  - `SameUnknownCallTwice_IsOneVariable`: `U() Or Not U()` → True (two occurrences with equal `FullName` and
    `Parameters` are the same game fact, so the same variable; `Not` applies on top).
  - `DifferentUnknownCalls_AreIndependent`: `U() Or U2()` → Unknown.
  - `BranchNot_IsIgnored`: `ConditionBranch([F()], Not:true, "And")` → False (game ignores branch Not).
  - `NestedBranch_EvaluatesRecursively`: `T() And (F() Or T())` → True.
  - `UnrecognisedOperator_IsUnknown`: leaf operator `"Xor"` → Unknown.
  - `OperatorCaseInsensitive`: `"and"`/`"OR"` accepted.
  - `MoreThanTwelveUnknowns_IsUnknownWithoutEnumerating`: 13 distinct unknown leaves joined by Or plus one
    `T()` first joined by And → Unknown, and the stub's call count ≤ 14 (no 2^13 re-evaluation of leaves: leaves
    are evaluated once each, enumeration is over cached values).
  - `Poe1GameId_UsesPoe1Fold`: the A/B/C list from Step 1 with stub values → True for "poe1", False for "poe2".
  - Unknown gameId (`""`) → use Poe2 fold _(decision)_.
- [x] **Step 5: Run, verify FAIL.**
- [x] **Step 6: Implement `ConditionEvaluator.Evaluate`**: evaluate each distinct leaf once (apply `Not` after),
  collect distinct Unknown variables; if >12 → Unknown; else for each assignment mask build bool results bottom-up
  (branch = fold of its components using the branch's components' operators; a branch component's own
  `Operator` links it to its next sibling) and fold; all true → True, all false → False, else Unknown.
- [x] **Step 7: Run, verify PASS; commit** `feat(core): condition folds and three-valued evaluator (#305)`.

### Task 2: Supported functions and fact collection

**Files:**
- Create: `DialogEditor.Core/Conditions/SupportedConditions.cs`, `ConditionFacts.cs`
- Test: `DialogEditor.Tests/Conditions/SupportedConditionsTests.cs`, `ConditionFactsTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces:
  - `public static class SupportedConditions { Truth Evaluate(ConditionLeaf leaf, GameState s);
    bool IsSupported(string methodName); static string MethodName(ConditionLeaf leaf); }` — `MethodName` is the
    text between the first space and `(` in `FullName` (same rule as `ConditionLeaf.Format`; reuse, don't copy:
    extract a shared helper if needed). `Evaluate` ignores `leaf.Not` (the evaluator applies it).
  - `public enum FactKind { Global, Companion, PlayedNode, Quest, InCombat }`
  - `public sealed record ConditionFact(FactKind Kind, string Key, int NodeId = 0)` (Key: global name, companion
    GUID, conversation key, quest id; empty for InCombat).
  - `public static class ConditionFacts { IReadOnlyList<ConditionFact> Collect(IEnumerable<ConditionNode> nodes); }`
    — distinct, first-seen order, recursing branches; `CompareGlobals` yields both globals.
  - `ConditionEvaluator`'s default leaf evaluator becomes `SupportedConditions.Evaluate`.

- [x] **Step 0: Confirm game semantics** in the decompiled code (paths in the `decompiled-game-code-locations`
  memory / `.trail` learnings): `GlobalVariables.m_data` comparer (both games), and `QuestManager.IsQuestActive /
  IsQuestCompleted / IsQuestFailed / HasQuestStarted` (both games). Adjust the spec §3 mapping if the game
  differs, and note what you found in the spec under §3 (one line each). If a function's meaning can't be
  settled, leave it unsupported.
- [x] **Step 1: Failing tests** — one per row of spec §3, e.g.:
  - `IsGlobalValue_EqualTo_MatchesSetValue` (`["g","EqualTo","2"]`, Globals g=2 → True; g=3 → False).
  - `IsGlobalValue_Unset_IsUnknown`; `IsGlobalValue_EqualsAlias_TreatedAsEqualTo`;
    `IsGlobalValue_BadOperator_IsUnknown`; `IsGlobalValue_NonIntValue_IsUnknown`;
    one test per remaining operator (GreaterThan, LessThan, NotEqualTo, GreaterThanOrEqualTo, LessThanOrEqualTo).
  - `CompareGlobals_EitherUnset_IsUnknown`, `CompareGlobals_Compares`.
  - `HasConversationNodeBeenPlayed_ReadsPlayedPair` (key match case-insensitive), unset → Unknown, bad int → Unknown.
  - `IsCompanionActiveInParty_ReadsCompanion`; `IsCompanionActiveInPartyOrPresent_InParty_True_NotInParty_Unknown`.
  - `IsAnyCompanionActiveInParty_AnyInParty_True`; `_NoCompanionsFlag_False`; `_OnlyNotInParty_Unknown`.
  - Quest functions per status (table-driven `[Theory]`), unset → Unknown.
  - `IsInCombat_ReadsFlag`; `AlwaysTrue/AlwaysFalse/Always(true|false)`.
  - `UnsupportedFunction_IsUnknown` (`Boolean IsSkillValue(Guid, Guid, Operator, Int32)`).
  - `ConditionFactsTests`: `Collect_RecursesBranches_DistinctInOrder`, `Collect_CompareGlobals_YieldsBoth`,
    `Collect_IgnoresUnsupported`, `Collect_IsInCombat_YieldsOneFact`.
- [x] **Step 2: Run, verify FAIL.**
- [x] **Step 3: Implement** as a `Dictionary<string, Func<IReadOnlyList<string>, GameState, Truth>>`
  (OrdinalIgnoreCase); a wrong parameter count → Unknown.
- [x] **Step 4: Run, verify PASS; commit** `feat(core): supported condition functions and fact collection (#305)`.

### Task 3: GameStateViewModel

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/GameStateViewModel.cs` (plus row VMs in the same file or
  `GameStateRowViewModels.cs`)
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (own one `GameStateViewModel GameState`,
  call `GameState.Load(...)` when the open conversation changes and after condition edits)
- Modify: ViewModels string resources (find where `Loc.Get` keys live for ViewModels; follow existing pattern)
- Test: `DialogEditor.Tests/ViewModels/GameStateViewModelTests.cs`

**Interfaces:**
- Consumes: `GameState`, `ConditionFacts`, `ConditionEvaluator`, the existing game-data name lookup used by
  condition parameter rows (`ParameterValueViewModel` / `GameDataNameService` — find via `trail ask`).
- Produces:
  - `GameStateViewModel(GameState state, Func<string,string,string?> nameLookup)` (lookupKind, id → display name).
  - `GameState State { get; }`; `void Load(IEnumerable<ConditionNode> conditions, IReadOnlyList<IReadOnlyList<ConditionNode>> conditionLists, string gameId)` — the first argument feeds `ConditionFacts`, the second is every non-empty node/link list for the summary. (A simpler shape is fine if it carries the same data; keep it Core-model based, not NodeViewModel-based.)
  - Collections: `Globals` (`GlobalRowViewModel { string Name; string ValueText; }`), `Companions`
    (`CompanionRowViewModel { string Guid; string DisplayName; TriState Choice; }`), `PlayedNodes`, `Quests`
    (`QuestRowViewModel { …; QuestChoice Choice }`), `bool TestsCombat`, `TriState CombatChoice`;
    `HasGlobals`/`HasCompanions`/… for section visibility; `string Summary`; `IRelayCommand ClearAllCommand`.
  - `public enum TriState { NotSet, Yes, No }`.
  - Editing a row writes straight into `State` and calls `State.NotifyChanged()`; Summary recomputes on Changed.

- [x] **Step 1: Failing tests**:
  - `Load_BuildsRowsForTestedFactsOnly` (a list with IsGlobalValue g, IsCompanionActiveInParty X → one global row,
    one companion row, no quest rows, `HasQuests == false`).
  - `GlobalRow_SetText_WritesState`; `GlobalRow_EmptyText_RemovesFact`;
    `GlobalRow_NonNumericText_LeavesTheFactUnset` (no throw).
  - `CompanionRow_Choice_WritesState` (Yes → true, No → false, NotSet → removed).
  - `ClearAll_ResetsStateAndRows`.
  - `SwitchingConversation_KeepsEarlierValues` (set g=1, Load another list without g, Load back → row shows "1").
  - `Summary_CountsPassFailUnknown` — exact text from resources with `Loc.FormatCount` pluralisation.
  - `DisplayName_FallsBackToGuid` when lookup returns null.
- [x] **Step 2: Run, verify FAIL.**
- [x] **Step 3: Implement.** Wire into `MainWindowViewModel` (find the conversation-selected path; note the trail
  learning: `conversation` there may be the game file before project edits — use the canvas/edited conditions).
- [x] **Step 4: Run all headless tests, verify PASS; commit** `feat: Game State view model (#305)`.

### Task 4: Verdict in Node Details

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` (+ link row VM if separate),
  `DialogEditor.Avalonia/Views/NodeDetailView.axaml`, string resources.
- Test: `DialogEditor.Tests/ViewModels/NodeDetailVerdictTests.cs`

**Interfaces:**
- Consumes: `GameStateViewModel.State`, `ConditionEvaluator.Evaluate`, game id.
- Produces: on `NodeDetailViewModel`: `bool ShowConditionVerdict`, `string ConditionVerdictText`,
  `Truth ConditionVerdict`; on each outgoing-link row with conditions: the same three properties.

- [x] **Step 1: Failing tests**: `NoFactsSet_HidesVerdict`; `FactSet_NodeConditionsPass_ShowsPasses`;
  `Fails`, `CantTell` texts; `LinkWithConditions_ShowsItsOwnVerdict`; `StateChanged_UpdatesVerdictLive`;
  `NodeWithoutConditions_HidesVerdict`.
- [x] **Step 2: Run, verify FAIL.**
- [x] **Step 3: Implement**; in XAML a `TextBlock` under the conditions summary and in each link row, bound to
  the properties, using existing success/warning/muted brushes, with a ToolTip resource explaining "from View ▸
  Game State". Unsubscribe from `State.Changed` when appropriate (no leaks across Loads).
- [x] **Step 4: Run, verify PASS; commit** `feat: Node Details shows the condition verdict (#305)`.

### Task 5: Game State dock tool and view

**Files:**
- Create: `DialogEditor.Avalonia/Docking/GameStateTool.cs` (copy `ConditionSearchTool` pattern),
  `DialogEditor.Avalonia/Views/GameStateView.axaml(.cs)`
- Modify: `DialogEditor.Avalonia/Docking/EditorDockFactory.cs` (`GameStateId = "GameState"`, add to
  `WrapperIds`, left pane tabbed after Conversations, InitLayout locator), `MainWindow.axaml(.cs)` (View ▸
  Game State → `ShowToolById(EditorDockFactory.GameStateId)`), the view's DataTemplate registration (follow how
  ConditionSearchTool maps to ConditionSearchView), UI strings.
- Test: `DialogEditor.Tests/Docking/EditorDockFactoryTests.cs` (extend), `DialogEditor.Tests/Views/GameStateViewTests.cs`

**Interfaces:**
- Consumes: `GameStateViewModel` (Task 3), `MainWindowViewModel.GameState`.

- [x] **Step 1: Failing tests**:
  - `CreateLayout_RightToolDock_IncludesGameState`.
  - `ShowGameState_WithLayoutLackingTheTool_AddsItToTheRightPane` (restore a layout JSON without the tool, then the
    View command path; assert the tool is visible in RightPane).
  - Headless view test: `GameStateView_EveryInteractiveControlHasATooltip` and `…HasAnAutomationName`;
    `GameStateView_HidesEmptySections`.
- [x] **Step 2: Run, verify FAIL.**
- [x] **Step 3: Implement.** View: explanation line, Clear all button, sections (ItemsControl per collection;
  global = name label + TextBox; tri-state = ComboBox with localised options), summary line. Scrollable.
- [x] **Step 4: Run all headless tests, verify PASS; commit** `feat: Game State dock tool (#305)`.

### Task 6: Verification in the real app (orchestrator)

- [x] Run `dotnet test DialogEditor.Tests --filter "Category!=Gui"` and `--filter "Category=Gui"`.
- [x] Launch with the `running-the-app` skill, open the canonical PoE2 fixture conversation, open View ▸ Game
  State, set a global and a companion, select a conditioned node, screenshot the verdict changing.
- [x] Update spec/plan checkboxes, `trail note` for anything that took digging, open the PR, file the §8
  follow-ups as issues (search first), write the Handoff section.

## Handoff — 2026-10-28

**Done** (branch `feat/305-condition-evaluator`, PR for #305): all six tasks. Core evaluator with both games'
folds (`DialogEditor.Core/Conditions/`), `GameStateViewModel` on `MainWindowViewModel.GameState`, verdict lines in
Node Details (node and each conditioned link), Game State dock tool (left pane, tabbed with Conversations; View ▸
Game State adds it to older saved layouts). Verified in the app against the canonical PoE2 fixture: setting
`canonical_flag_d` to 3 makes node 2 read "passes", 7 makes it "fails", and the panel summary follows.

**Changed after the GUI check:** the tool moved from the right pane (where it hid Node Details) to the left; the
verdict TextBlocks lost a fixed automation name so screen readers read the verdict; DriveApp's settings
backup now covers layout.json.

**Filed:** #342 (Node Details CONDITIONS shows "(none)" — pre-existing on main), #343 (headless MainWindow tests
overwrite the real layout.json), follow-ups #344 (persist state), #345 (global initial values), #346
(character-dependent conditions); canvas dimming noted on #32.

**Next step:** owner reviews the PR, especially the _(my call)_ items in the spec. After merge, #32 can start
from `ConditionEvaluator.Evaluate`.

**Open questions for the owner:** quest-state model (NotStarted/Active/Completed/Failed; `IsQuestActive` on a
failed quest is "can't tell"); session-only state; verdict shown only once a fact is set.

**Don't redo:** the fold transcriptions are checked line by line against the decompiled code (see the trail
learning on And/Or folds); `trail check` flags 20 word-overlap false positives on this branch.
