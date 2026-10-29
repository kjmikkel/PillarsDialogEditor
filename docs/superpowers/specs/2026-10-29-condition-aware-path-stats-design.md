# Condition-aware path stats and canvas — design

Issue: [#32](https://github.com/kjmikkel/PillarsDialogEditor/issues/32). Builds on #305 (condition evaluator and
assumed Game State, spec `2026-10-28-condition-evaluator-design.md`) and on the path-stats work of #14 / #31
(specs `2026-07-13-path-based-writing-stats-design.md`, `2026-09-10-cross-conversation-path-stats-design.md`).

**How this was decided.** The owner asked for #32 to be built unattended and reviewed as a PR, so every design
choice below that is not quoted from the issue or the #305 spec is **my call** (the implementing agent's), marked
_(my call)_. Review these first.

## 1. Intent

Playthrough stats (longest / shortest, the fork tree, per-ending figures) walk every link, so they are upper
bounds. With an assumed game state set in View ▸ Game State, the writer wants the same figures for a playthrough
that can actually happen under that state, and to see on the canvas which links cannot fire.

### Goals
- Path stats and the fork tree skip links that **cannot** fire under the assumed state.
- The canvas draws links that cannot fire differently (the piece #305 deferred here).
- One definition of "cannot fire", shared by stats and canvas, taken from the game's own code.

### Non-goals
- Saving the assumed state (still session-only, as in #305).
- First-match shadowing between NPC links (§2, last paragraph) and node persistence (`OnceEver` already played).
- Dimming node cards. Only links change on the canvas _(my call: card rendering was just reworked in #223; a dim
  link into a node already says the node is cut off from that side)_.

### Anti-goals
- Pruning on a guess. **Only `False` prunes; `Unknown` stays reachable** (brief). A figure may still be an upper
  bound, never an under-count caused by an unsupported function.
- Changing anything for a writer who never touches Game State: figures stay byte-identical while no fact is set.

## 2. When a link "cannot fire" (from the decompiled code)

Both games choose the next node in `FlowChart.GetNextNode` / `Conversation.GetPlayerResponses` (PoE2
`Assembly-CSharp/Game/FlowChart.cs`, `Conversation.cs`; PoE1 `Assembly-CSharp/FlowChart.cs`, `Conversation.cs`).
A link `u → v` is followed only if:

- **PoE2:** `PassesConditionals(link)` **and** `PassesConditionals(v)` — the link's own conditions and the
  target node's conditions;
- **PoE1:** `PassesConditionals(v)` only. PoE1's `FlowChartLink` has no conditionals, and the editor's PoE1 parser
  already gives every link an empty list, so applying the PoE2 rule to both games gives the PoE1 answer.

So: a link **cannot fire** when `Evaluate(link.Conditions) == False` **or** `Evaluate(target.Conditions) ==
False` (each with `ConditionEvaluator.Evaluate(…, state, gameId)`). This is a pure Core helper,
`LinkFiring.CannotFire(linkConditions, targetConditions, state, gameId)` in `DialogEditor.Core/Conditions/`.
It returns `false` whenever `!state.HasAnyFact` _(my call: matches #305's "verdicts are quiet until a fact is
set"; without it an `AlwaysFalse()` link would change today's figures the moment this ships)_.

Not modelled _(my call)_: the game also takes the **first** NPC link that passes, so a definitely-true earlier
link shadows later ones. That rule applies with no game state at all and today's stats ignore it too; it is a
separate question for path stats in general, not one of assumed state. Follow-up issue.

## 3. Path stats

`PathStatsService.Analyze(graph, GameState? state = null, string gameId = "")` (and the snapshot overload with
the same optional pair). With a state, `LinksOf(u)` drops every link that cannot fire. Because every walk goes
through `LinksOf` — DAG cut, longest/shortest, reachable content, choice frontier, endings — all playthrough
figures follow from that one filter.

- **Handoffs** (StartConversation jumps) are never pruned _(my call: they are scripts on a node already reached;
  the target conversation's own links are filtered as usual)_.
- **The root** (node 0) is always the start, whatever its own conditions _(my call: the game decides whether to
  start a conversation elsewhere, and a writer analysing a conversation means "if it starts")_.
- **Endings** become nodes with no **live** link and no handoff _(my call: when every link out of a node fails,
  the game's `GetNextNode` returns null and the conversation ends there — that is the game's own behaviour)_.
- **Fork tree:** a choice whose way in cannot fire is not offered, so it leaves the tree with its sub-forks
  _(my call: removed, not greyed — the checkbox in §4 brings the full tree back for comparison)_.
- **Totals and words per speaker** are unchanged _(my call: they count everything written, not a playthrough)_.
- New report field `BlockedLinkCount`: links in the graph that cannot fire (0 without a state).

## 4. Flow Analytics window

Replacement, not a second column _(my call: a second column doubles every row in an already dense panel)_. When
the assumed state has a fact, a checkbox **"Use assumed game state"** appears in the Playthrough stats header,
**checked by default**; unchecking it shows today's upper bound. Not persisted _(my call: the state it refers
to is not persisted either)_. While it is checked and links were pruned, one muted line under the header reads
"{n} links can't fire under the assumed game state and are skipped." The window re-runs the analysis when the
state changes, so editing Game State with the window open updates the figures.

The view model gets the state and game id through its constructor (as it already gets `gameId`), subscribes to
`GameState.Changed`, and unsubscribes in a `Detach()` the window calls on close.

## 5. Canvas

`ConnectionViewModel.CannotFire` (via `LinkFiring`, target = `Target.Owner.Conditions`). The canvas
(`ConversationViewModel`) gets `AssumedState`; it points every connection at it (existing and added ones) and
refreshes them when the state changes or a node's `Conditions` change. A connection that cannot fire carries the
class `cannotFire`: **opacity 0.35**, keeping its stroke, dash and highlight _(my call: opacity composes with the
existing `always` / `never` / `highlighted` classes, so no style matrix and no new brushes)_. It also gets a
tooltip saying it cannot fire under the assumed state and where that is set; other links keep no tooltip.

The Legend window gains a row in CONNECTIONS for the faded link.

## 6. Left to the builder
- Exact placement of the checkbox and the pruned-count line within the existing header grid.
- Test names and layout (`DialogEditor.Tests/Conditions/LinkFiringTests.cs`, existing analytics/VM test files).

## 7. Follow-ups (file after merge)
- First-match shadowing of later NPC links by a link that definitely passes (§2).
- Persist the assumed state per project (carried over from #305).
