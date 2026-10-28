# Condition evaluator and Game State panel — design

Issue: [#305](https://github.com/kjmikkel/PillarsDialogEditor/issues/305). Unblocks #32 (condition-aware path
stats) and feeds #1 (Playtest Mode) later.

**How this was decided.** The owner asked for #305 to be built unattended and reviewed as a PR, so every design
choice below that is not quoted from the issue is **my call** (the implementing agent's), marked _(my call)_.
Review these first.

## 1. Intent

Nothing in the editor decides whether a condition is true. `ConditionNode` is a display-only tree. Writers want to
ask "if the player has done X and Y, which of these lines can fire?" without launching the game. This work adds:

1. a **condition evaluator** in Core that decides true / false / can't tell for a condition list, against an
   assumed game state, using the game's own rules;
2. a **Game State panel** where the writer sets that assumed state, prefilled from what the open conversation
   actually tests;
3. a visible **verdict** in Node Details, so the panel has an effect before Playtest Mode (#1) exists.

### Goals
- Same answer as the game for every function the evaluator supports, including the game's unusual AND/OR rules.
- Unknown functions and unset facts report **can't tell**, never a guess (issue: "never a guess").
- Cover the functions that carry most real conditions (see §3).
- Easy to add more functions later: one small entry per function.

### Non-goals (this PR)
- Playtest Mode (#1) and condition-aware path stats (#32). They consume the evaluator later.
- Canvas styling of links that can't fire _(my call: deferred; it touches card/link rendering, which was just
  reworked in #223, and #32 is the natural home for "what can't fire")_.
- Saving the assumed state with the project or between sessions _(my call: in memory for the session only; it
  avoids a project-format change. Follow-up issue)_.
- Character-dependent checks (skills, attributes, disposition, reputation, race/culture/class, items). They need
  a model of "which character" (speaker, player, party slots, special GUIDs) and are their own piece of work.
- Prefilling globals with their game-defined initial values _(my call: follow-up; the parsers currently keep
  only the names)_.

### Anti-goals
- A verdict that is confidently wrong. If in any doubt about a function's game semantics, it stays unsupported.
- Noise: verdicts are shown only once the writer has set at least one fact.

## 2. How the games combine conditions (from the decompiled code)

A condition list is a flat list of components; each is a call (leaf) or a nested expression (branch). Each
component carries an `Operator` (And/Or) that links it to the **next** component. `Not` exists only on calls
(`ConditionalCall.Not`); `ConditionalExpression` has no `Not` field, so **a branch's `Not` is ignored by both
games** and the evaluator ignores it too _(follows the game)_. An empty list is true.

There is **no precedence**: the game folds left to right with short-circuits, and the two games differ.

**PoE2** (`ScriptManager.EvaluateExpressionWithOwnerObjects`): `prev = Or`, `flag = false`. For each component
`i` with result `r` and its own operator `next`:
- last component: return `prev == And ? r : (r || flag)`;
- otherwise: if `next == And && !r` return false; if `next == Or && r` return true;
- `flag = r; prev = next`.
- (falls through to false only on the impossible path.)

**PoE1** (`FlowChart.EvaluateExpression`): `prev = Or`, `flag = false`. For component `i`:
- last: return `prev == And ? r : (r || flag)`;
- `i == 0`: if `next == And && !r` return false; if `next == Or && r` return true;
- middle: if `prev == And`: `!r` → false, else if `next == Or` → true. If `prev == Or`: `r` → true, else if
  `next == And` → false.
- `flag = r; prev = next`. Falls through to false.

The builder transcribes these two functions **literally** over `bool` results, with tests that pin the
differences (e.g. three components where PoE1 and PoE2 disagree).

### Three-valued results
Each leaf yields `True`, `False` or `Unknown`. To combine without inventing truth tables for the quirky folds
_(my call)_: enumerate every true/false assignment of the Unknown leaves, run the game's bool fold on each, and
answer True if all runs are true, False if all are false, otherwise Unknown. Nested branches are evaluated the
same way recursively (a branch is one component whose value is its own three-valued result; enumeration happens
over leaves of the whole list so correlations are kept). Cap: more than 12 Unknown leaves in one list → Unknown
without enumerating (4096 runs max). `Not` on a leaf flips True/False and leaves Unknown alone.

An `Operator` string other than And/Or (case-insensitive) makes the whole list Unknown.

## 3. Functions supported in v1

Usage counts over the shipped conversations (PoE2 1,130 files; PoE1 1,434 files) put three functions far ahead:
`IsGlobalValue` (9,791 / 7,401), `IsCompanionActiveInParty` (2,637 / 2,503) and `HasConversationNodeBeenPlayed`
(2,384 / 1,710). v1 supports, keyed by method name (the text between the return type and `(` in
`ConditionLeaf.FullName`):

| Function | Games | Fact read | Notes |
|---|---|---|---|
| `IsGlobalValue(name, op, int)` | both | global `name` | compare with the game's `CompareInt` operators |
| `CompareGlobals(name1, op, name2)` | both | globals `name1`, `name2` | Unknown if either unset |
| `HasConversationNodeBeenPlayed(conv, nodeId)` | both | played(conv, nodeId) | PoE2 `conv` is a GUID, PoE1 a name; both used as a string key |
| `IsCompanionActiveInParty(guid)` | both | companion in party | |
| `IsCompanionActiveInPartyOrPresent(guid)` | PoE2 | companion in party | True if in party; otherwise Unknown ("present" is not modelled) |
| `IsAnyCompanionActiveInParty()` | both | all companion facts | True if any companion set in party; False only if the writer said "no companions in party" (§4); else Unknown |
| `IsQuestActive`, `IsQuestCompleted`, `IsQuestFailed`, `HasQuestStarted` (quest) | both | quest status | status → each function, below |
| `IsInCombat()` | both | in combat | |
| `AlwaysTrue()`, `AlwaysFalse()`, `Always(bool)` | as present | — | constants |

Operators for `op` (game's `Operator` enum): `EqualTo`, `GreaterThan`, `LessThan`, `NotEqualTo`,
`GreaterThanOrEqualTo`, `LessThanOrEqualTo`; `Equals` is also accepted as `EqualTo` (catalogue drift, see the
learning on #266). Anything else → Unknown. A parameter that should be an int but doesn't parse → Unknown.

Quest status is one of NotStarted / Active / Completed / Failed _(my call: the editor's simplified model)_:
- `HasQuestStarted`: true for Active, Completed, Failed;
- `IsQuestActive`: Active only; `IsQuestCompleted`: Completed only; `IsQuestFailed`: Failed only.
Checked against the decompiled `QuestManager` (PoE2; PoE1 keys quests by path name and only exposes
`IsQuestFailed` as a condition):
- `HasQuestStarted` -> `Quest.IsStarted()` (state 0 visited): matches "Active, Completed, Failed".
- `IsQuestActive` -> `ActiveQuests.ContainsKey`; a quest is removed when it ends
  (`Quest.DetermineIfQuestHasEnded` -> `EndQuest` when `IsComplete()`). Active -> True; NotStarted and Completed
  -> False. **Changed:** `Failed` -> Unknown, because it is unverified whether a failed quest also leaves `ActiveQuests`.
- `IsQuestCompleted` -> `IsComplete() && !IsQuestFailed()`: Completed only, as mapped.
- `IsQuestFailed` -> `QuestTrackers[id].Failed`: Failed only, as mapped.

Keys: **global names compare case-sensitively** (ordinal). **Changed from the draft:** `GlobalVariables.m_data` is a
default-comparer `Dictionary<string,int>` in PoE2 and a default `Hashtable` in PoE1, so `g` and `G` are different
variables. (An unset global reads -1 in-game, but a global the writer has not set stays "not set" -> Unknown here.)
GUIDs and conversation keys compare case-insensitively.

## 4. Core API (`DialogEditor.Core/Conditions/`)

- `enum Truth { True, False, Unknown }`.
- `sealed class GameState` — the assumed facts, each optional (absent = not set):
  globals `string → int`; companions `guid → bool`; `NoCompanionsInParty` flag; played `(conv, nodeId) → bool`;
  quests `id → QuestStatus`; `InCombat : bool?`. `HasAnyFact`. Raises `Changed`.
- `static class ConditionEvaluator` — `Truth Evaluate(IReadOnlyList<ConditionNode> list, GameState s, string
  gameId)`; `Truth EvaluateLeaf(ConditionLeaf, GameState)`.
- `static class ConditionFacts` — `Collect(IEnumerable<ConditionNode>)` → the distinct facts those conditions
  test (global names, companion GUIDs, played pairs, quest ids, whether combat is tested), in first-seen order.
  The panel uses it to prefill rows for the open conversation (node and link conditions).
- A function table (`SupportedConditions`) mapping method name → evaluator, so adding a function is one entry
  plus tests.

## 5. Game State panel

A new dock tool, **Game State** (Id `"GameState"`, the value is persisted in layout.json and must never change),
tabbed in the right pane beside Node Details and Condition search, opened from **View ▸ Game State**. Saved
layouts from before this change lack it; the View menu command must still bring it up in the right pane.

Content, for the open conversation (rebuilt when the conversation changes or its conditions are edited):
- a one-line explanation and a **Clear all** button;
- sections, each shown only if the conversation tests something in it: **Global variables** (name + integer
  box; empty = not set), **Companions in party** (display name via the existing game-data name lookup, falling
  back to the GUID; a three-way choice: In party / Not in party / Not set), **Conversation nodes played**
  (conversation + node; Played / Not played / Not set), **Quests** (name lookup; Not set / Not started / Active /
  Completed / Failed), **In combat** (Yes / No / Not set);
- a summary line: "In this conversation, n conditions pass, n fail, n can't tell" (node and link condition
  lists that are non-empty).

The state lives on the main view model for the session and is shared across conversations (globals are
game-wide). Values set for a fact the new conversation doesn't test are kept, not shown.

All text in resources; every control has a ToolTip and a UIA name (CLAUDE.md).

## 6. Verdict in Node Details

When `GameState.HasAnyFact` and the node has conditions, the node's condition section shows one line: "With the
assumed game state: passes" / "fails" / "can't tell", styled with the existing success / warning / muted brushes
and a tooltip that explains where the state comes from (View ▸ Game State). Each outgoing link that has
conditions shows the same verdict in its row. Updates live when the state changes.

## 7. Left to the builder
- Exact placement of the verdict lines within the existing Node Details layout.
- Control types for the three-way choices (follow `feedback_ui_picker_and_discoverability`: ComboBox is fine for
  fixed short lists).
- Test layout under `DialogEditor.Tests/Conditions/` mirroring Core.

## 8. Follow-ups (file after merge)
- Persist the assumed state per project.
- Initial values for globals from the game's global-variable definitions.
- Character-dependent conditions.
- Canvas: dim links that can't fire (with #32).
