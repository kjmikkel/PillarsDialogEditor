# First-passing-link rule (link shadowing) — design

Issue: [#353](https://github.com/kjmikkel/PillarsDialogEditor/issues/353). Follow-up to #32 (spec
`2026-10-29-condition-aware-path-stats-design.md`, which left this rule out in §2) and builds on the condition
evaluator of #305 (`2026-10-28-condition-evaluator-design.md`).

**How this was decided.** The owner settled in conversation: hidden links change path stats **and** get a Flow
Analytics warning, and the canvas fades them like #32's links that cannot fire. Everything else below that is
not taken from the game code is **my call** (the designing agent's), marked _(my call)_.

## 1. Intent

The game does not treat a node's links as a set of alternatives. It walks them **in order** and the first one
that passes decides what happens. A link that always passes therefore hides the links after it, and a writer
gets no hint of that today: path stats count the hidden content, the canvas draws it as normal, and Flow
Analytics says nothing. In the shipped PoE2 conversations, with no game state at all, 390 nodes have an
unconditional first-match that hides 802 later links.

### Goals
- Path stats (with and without an assumed game state) follow the game's first-passing-link rule.
- Flow Analytics warns about each link that can never fire because an earlier link always passes.
- The canvas fades those links, with a tooltip naming the link that hides them.
- One Core implementation of the rule, shared by stats, warnings and canvas, taken from the game's code.

### Non-goals
- Modelling Random / Cycle play types, bank play rules, or question-node expansion. These **never hide**
  anything (§2); they are not otherwise simulated.
- Editing the new node fields (`PlayType`, `IsQuestionNode`). They are read for analysis only.
- Persistence-driven reachability across visits (`OnceEver` already played, etc.).
- A command to reorder a node's links (none exists today). Follow-up issue _(my call)_.

### Anti-goals
- **Hiding on a guess.** Only a link that passes **for certain** hides anything. Unknown verdicts, banks,
  hidden question nodes, once-only targets and non-Normal play types hide nothing. Figures may stay upper
  bounds; they must never under-count.
- Warnings that change with the assumed game state. The warning describes the writing, not a playthrough.

## 2. What the game does (decompiled code)

PoE2 `Game/FlowChart.cs` (`GetNextNode`, `GetNextNodeInBankNode`), `Game/Conversation.cs` (`GetNextNode`
override, `GetResponseNodes`, `PassesPersistence`, `ShouldDisplayQuestionNodeText`), `Game.UI/UIBaseConversationWindow.cs`
(`AdvancePlayerResponse`). PoE1 `FlowChart.cs` / `Conversation.cs` have the same structure; PoE1 links carry no
conditionals (the editor's PoE1 parser already gives them an empty list).

On leaving a node the UI first asks `GetResponseNodes`, and only if that is empty calls `GetNextNode`:

- `GetResponseNodes` walks the links in order. If the first passing link goes to an NPC/script node it returns
  an empty list, so player responses after it are **not** shown. Once a player response has passed, later
  non-response links are skipped and later passing responses are collected.
- `GetNextNode` (source `PlayType == Normal`) returns the first link, in order, whose link conditions and
  target conditions pass, skipping player-response targets.

So **the kind of the first passing link decides the mode**: an NPC/script target → the conversation goes there;
a player response → the menu shows every passing response.

A node's conditions "pass" via `PassesConditionals(node)`, which in `Conversation` also requires
`PassesPersistence(node)`: a `OncePerConversation` or `OnceEver` target fails once played. `None` and
`MarkAsRead` always pass persistence.

Not first-match, so never hiding:
- Source `PlayType` `Random` / `CycleRandom` (weighted random over passing links) and `CycleLoop` / `CycleStop`
  (rotating start position). This is a conservative simplification: `GetResponseNodes` ignores `PlayType`, so
  NPC-vs-response mode hiding still happens there in the game. Hiding nothing only over-counts.
- A source that is a **bank** _(added after the final review)_. A bank's links are walked using the `PlayType`
  of the bank child that just played, which the editor cannot know.
- A target that is a **bank** (its own play rules; its children may be player responses).
- A target **question node** whose text is hidden: it expands to its player responses
  (`QuestionNodeTextDisplay` `ShowNever`, or `ShowOnce` after the first visit).

## 3. The rule (Core)

A pure helper, `DialogEditor.Core/Conditions/LinkShadowing.cs` (`Describe` builds one link's verdict and
kind; `HiddenBy` walks a node's links), takes one source node's links **in order** together with, per link, its
verdict and the kind of its target, and returns which links are hidden and, for each, the index of the link
that hides it.

Per link:
- **Verdict** — `False` if `LinkFiring`'s test says it cannot fire; `True` if the link's conditions and the
  target's conditions both evaluate `True` **and** the target's persistence is `None` or `MarkAsRead` (or empty);
  otherwise `Unknown`.
- **Kind** — `Npc` (any non-player-choice target), `Response` (player-choice target), or `Opaque` (target is a
  bank, `ScriptKind == Bank`; or target `IsQuestionNode`) _(my call: every question node is opaque, not just
  hidden-text ones — on a revisit a `ShowOnce` node hides its text, and path stats count a loop once, so
  treating it as certain could under-count)_. An Opaque link is never `True`, and it counts as a possible
  response (it may yield player responses).
- A dangling link (no target) is ignored: it neither hides nor is hidden.

Walk the links in order with two flags: *maybeResponse* (an earlier non-`False` `Response` or `Opaque` link
exists) and the hider found so far.
- If the source node's `PlayType` is not `Normal`, or the source is a bank, nothing is hidden. Empty
  `PlayType` counts as `Normal` (the default value); an unrecognised value does not _(my call: the safe side)_.
- A `False` link is skipped (it is already pruned by #32 when a state is set; it neither hides nor is hidden
  here).
- The first `True` **Npc** link hides every later link — except later `Response` and `Opaque` links when
  *maybeResponse* is set at that point (an earlier response might pass, which puts the game in response mode,
  where later responses — including those a bank or question node yields — are collected).
- The first `True` **Response** link hides every later `Npc` link; later `Response` / `Opaque` links stay.
- Only the **first** `True` link (Npc or Response) is a hider. Later `True` links are hidden or kept by the
  two rules above, and never hide anything themselves. The first hider is the link the tooltip and warning
  name.

The verdict uses the assumed `GameState` when one is given, else an empty `GameState` _(my call: with no
facts a link with no conditions is `True` and almost everything else is `Unknown`, which is the structural
answer the warning needs; `False` pruning stays gated on facts, exactly as #32 decided)_.

## 4. Path stats

`PathStatsService.LinksOf(u)` drops hidden links as well as blocked ones. All playthrough figures already go
through `LinksOf`, so longest / shortest, reachable content, the fork tree and endings follow. **This applies
with no game state too**, so today's figures change for every writer whose conversations contain hiding links;
that is the game's behaviour, not a guess. Endings need no change: the hider itself is always live, so hiding
never leaves a node with no live link.

- Report field `ShadowedLinkCount` (graph-wide, under the state in use), shown in Flow Analytics as one muted
  line like #32's pruned-links line: "{n} links are skipped because an earlier link from the same line is sure
  to pass." _(my call; "sure to pass", not "always passes", because with a state set the certainty can come
  from the assumed facts)_ Shown whether or not a game state is set.
- The "Use assumed game state" checkbox (#32) still only toggles the state; hiding by certain links with no
  conditions always applies _(my call: there is no "upper bound" that ignores link order worth comparing
  against — it is not a playthrough the game can produce)_.
- Handoffs are never hidden (they are script actions, as in #32).

## 5. Flow Analytics warning

New `FlowIssueKind.LinkShadowed`, reported on the **source** node, one issue per hidden link. `FlowIssue`
gains two optional node ids, `RelatedNodeId` (the hidden link's target) and `HiderNodeId` (the hiding link's
target), so the message can say which link: "Link to node {to} never fires: the link to node {hider} before it
always passes." Clicking the issue goes to the source node. Computed with the empty state only
(§1 anti-goal). Severity: warning, like everything except `Unreachable`.

`Unreachable` also follows the rule _(my call)_: FlowAnalysisService's reachability walk skips hidden links,
so a node whose every way in is hidden is reported unreachable. Again with the empty state.

## 6. Canvas

`ConnectionViewModel` gets `ShadowedByNodeId` / `IsShadowed`, and the canvas binds `IsFaded` (cannot fire or
hidden) and `FadedToolTip`. The hidden tooltip reads "This link never fires: the link to node {hider} comes
before it and is sure to pass (under the game state assumed in View ▸ Game State, if one is set). The game
takes the first link that passes, in link order." It is computed with the canvas's assumed state when it has a fact, else the empty state, once per
source node (see §7a for when it refreshes).

Drawn exactly like #32's `cannotFire`: opacity 0.35, keeping stroke, dash and highlight _(my call: one visual
for "this link won't be taken"; the tooltip says why)_. If a link is both blocked and hidden, the
cannot-fire tooltip wins (blocked links are skipped before hiding is decided, so this only happens when the
state changes under it). The Legend's existing faded-link row gets its help text widened to cover both
reasons rather than a second identical swatch _(my call)_.

## 7. New node fields

`PlayType` (string, names from `OEIFormats` `PlayType`: `Normal`, `Random`, `CycleLoop`, `CycleStop`,
`CycleRandom`; PoE1 has no `CycleRandom`) and `IsQuestionNode` (bool) on `ConversationNode` and
`NodeEditSnapshot`, read by both parsers (PoE1 XML elements `PlayType` / `IsQuestionNode`; PoE2 JSON ints
via `Poe2EnumMaps`, like `Persistence`). Read only: saving must keep emitting whatever the file had (PoE2
keeps them as pass-through properties today; check PoE1 round-trips too). Not persisted in the project format
_(my call: same reasoning as `ScriptKind`, re-read from the game file)_, so mark them `[JsonIgnore]`, and
default to `""` / `false` so existing callers and snapshots compile and behave as Normal / not-a-question.

## 7a. Answers from the builder check _(my calls)_

- **Link order.** The editor has no link-reorder command. Order is file order, and a new link is appended to the
  end. No reorder UI in this change: it is a follow-up issue, and with this rule it becomes more useful. The
  canvas therefore recomputes on links added or removed, not on "reorder". **Pin order preservation** with tests:
  a deleted link restored by undo goes back to its original index (fix it if it doesn't), and parse → snapshot →
  serialize keeps link order.
- **Where the fields flow.** Every path that builds a `NodeEditSnapshot` / `ConversationNode` carries
  `PlayType` / `IsQuestionNode`: the parsers, `NodeViewModel` ↔ snapshot (including undo/redo and duplicate),
  and the multi-conversation graph loader. A node created in the editor is `Normal`, not a question node, which
  is the game's own default, so `""` / `false` are correct defaults there. Projects reload by re-parsing the
  game file (as for `ScriptKind`), so `[JsonIgnore]` loses nothing. No nullable tri-state.
- **Canvas structure.** Compute per **source node**: one call to the Core helper per node, whose results are
  pushed into its outgoing `ConnectionViewModel`s (`IsShadowed`, hider id). Don't compute per connection. A
  refresh for a node runs when its links are added or removed or a link's conditions change, when any of its
  targets' conditions / persistence / player-choice change (refresh the sources of the links into that target),
  and for all nodes when the assumed state or the game id changes. `PlayType`, question-ness and bank-ness are
  read-only, so they never trigger one. Loading a conversation refreshes once at the end, not per link. Hook the
  state change in the same place `ConversationViewModel` already refreshes `CannotFire` (#32), not through Node
  Details. As built, undo of a node deletion also reinserts its links at their original indices.
- **PoE1 values.** PoE1 XML has `<PlayType>Normal</PlayType>` (enum names, same casing as OEIFormats) and
  `<IsQuestionNode>false</IsQuestionNode>`. A missing element means `Normal` / `false`. Make sure the PoE1
  serializer writes back the values it read, or leaves the elements untouched.
- **Handoffs in flow analysis.** `FlowAnalysisService` is single-conversation and walks links only. The
  `Unreachable` change applies to links; jumps are out of scope there.

## 8. Left to the builder
- Exact signature of the Core helper and how callers supply target kind / verdict.
- Where the canvas recomputes (which events trigger a sibling refresh).
- String keys, test names and file layout.

## 9. Localisation and UI rules
All new strings in `DialogEditor.Avalonia/Resources/Strings.axaml` (and any other locale files the #32
commits touched); the shadowed-count line uses the same pluralised formatting as `PathStats_PrunedLinks`. No
new interactive controls are planned; if one appears it needs a tooltip and a UIA name.

## 10. Follow-ups
- A command to reorder a node's links: [#359](https://github.com/kjmikkel/PillarsDialogEditor/issues/359).
