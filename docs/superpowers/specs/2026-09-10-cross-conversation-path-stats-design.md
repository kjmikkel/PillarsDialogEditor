# Cross-Conversation Path Stats — Design

**Date:** 2026-09-10
**Issue:** #14 — *Analytics scope extensions ▸ Path-based writing stats ▸ Cross-conversation paths*
**Builds on:** `docs/superpowers/specs/2026-07-13-path-based-writing-stats-design.md`
**Status:** Design approved; implementation pending.

## Problem

Playthrough stats stop at the conversation file boundary. A writer whose scene hands off to
another conversation gets a "longest playthrough" that measures the first file only — not the
read the player actually experiences. Every figure in the Playthrough-stats section has this
limit: header extremes, forks, endings, and words-per-speaker alike.

Conversations do not link to each other. In both PoE1 and PoE2 a handoff is a **script call**
on a node — `StartConversation` or `StartConversationFacingListener` — naming a target
conversation and an entry node id. `NodeLink` is `(FromNodeId, ToNodeId)`, two ints scoped
inside one conversation, and has no way to express this.

## Engine semantics: a jump is a spawn

Established from the decompiled sources (`PoE2 Code/Assembly-CSharp/Game/`), because the
arithmetic depends on it:

- `Scripts.StartConversation` (`Scripts.cs:1515`) delegates to
  `ConversationManager.StartConversation`, which at `ConversationManager.cs:676-694`
  constructs a **new `FlowChartPlayer` and adds it to `ActiveConversationPlayers`**.
- It does **not** stop the current player, and there is **no return path**.
- There is no "stop conversation" script in the catalogue at all; a conversation ends only by
  reaching a node with no outgoing links.

So a jump is a **spawn**, not a call and not a replace. The ubiquitous authoring pattern —
a terminal node whose exit script hands off — is the case where spawn and "continue"
coincide. A node that both links onward *and* starts another conversation really does run
both concurrently, and is almost certainly an authoring mistake.

## Scope decisions (settled during brainstorming)

1. **Boundary: project-patched conversations only.** A jump is followed only when the target
   is a conversation this project patches. A handoff to an unpatched conversation is reported
   as a labelled leaf contributing no words. This bounds traversal by project size instead of
   by the size of the game, and keeps the report about the writer's own content — the same
   scope rule duplicate-line detection already uses.
2. **Arithmetic: additive, faithful to the spawn.** A jump contributes its target's figures
   *in addition to* whatever the source node's own links contribute — in `Longest` **and** in
   `Shortest`, because a spawn is not optional. In the common handoff case (no links, one
   jump) this collapses to "the conversation continues".
3. **A node that both continues and hands off is reported as a defect.** New
   `FlowIssueKind.ConversationJumpWhileContinuing`, raised by `FlowAnalysisService`
   independently of the jump toggle — it is a graph-shape defect, not a stats setting.
4. **Presentation: opt-in toggle, off by default.** A "Follow conversation jumps" checkbox
   beside the Playthrough-stats header, persisted in settings — the pattern the reading-speed
   preset picker established in #23. No existing figure changes until the writer asks. When
   on, the header states how many conversations were spanned.
5. **All four metrics span jumps** — header extremes, fork tree, endings, and
   words-per-speaker. Half-spanning would make sections disagree with each other by
   construction. Words-per-speaker benefits most: "how much does this companion say across
   the whole arc" is unanswerable per-file.
6. **An ending is a node with no links *and* no handoff.** This refines the #14 rule ("a node
   with no outgoing links"): a node that hands off is a way the conversation *continues*, not
   a way it finishes. Consequence: with the toggle on, a node that previously reported as an
   ending may stop doing so. The UI hint says this in words.

## Layering: why the resolver is separate

`DialogEditor.Core.csproj` has **no project references**. `PathStatsService` therefore cannot
see `DialogProject`, `PatchApplier`, or the script catalogue, and must stay pure and IO-free.
The split is forced, not chosen:

- **`ConversationJumpResolver`** (`DialogEditor.ViewModels/Services/`) owns all I/O, patch
  application, and GUID resolution, and emits a pure graph.
- **`PathStatsService`** (`DialogEditor.Core/Analytics/`) consumes that graph and stays free
  of every one of those concerns.

## Discovery and resolution

**Discovery — whitelist the verb, resolve the arguments generically.**

The verb comes from a small closed list, held in Core beside `BarkConstants` so that both
`FlowAnalysisService` (detection only) and the resolver (detection plus resolution) share it:

    StartConversation, StartConversationFacingListener

Matching *only* on the catalogue's `lookupKind: "Conversation"` would over-match:
`MarkConversationNodeAsRead(Guid, Int32)` and `ClearConversationNodeAsRead(Guid, Int32)` have
an identical parameter shape — a `"Conversation"` lookup kind plus a `"Conversation Node ID"`
— and start nothing. The verb list is what excludes them, and a regression test pins this.

Arguments *are* located by catalogue metadata, never by hard-coded index. The catalogue entry
is fetched with `ScriptCatalogue.FindByFullName` (exact reflection signature, so the correct
per-game variant); the conversation argument is the parameter whose `LookupKind` is
`"Conversation"`, and the entry node is the `Int32` parameter named `"Conversation Node ID"`.

**Resolution branches on that parameter's declared `Type`:**

- `Guid` (PoE2) — look up `GameDataNameService.Get("Conversation")` by `Id` to get the name.
  That registry is populated at `Poe2GameDataProvider.cs:182-197`, which parses each bundle's
  root `ID` and pairs it with the filename.
- `String` (PoE1) — the parameter value *is* the name. `Poe1GameDataProvider` registers no
  `"Conversation"` entries and needs none.

Then name → `ConversationFile` via `provider.FindConversation`. Note that `ConversationFile.Name`
is `GetFileNameWithoutExtension` and `FindConversation` is a `FirstOrDefault`, so two bundles
sharing a filename in different folders are indistinguishable by name; this is a pre-existing
provider limitation, and an unresolvable target degrades to a reported leaf rather than a guess.

**Materialising a conversation** follows `ProjectFindService.cs:26-46` exactly: the open
conversation uses its live snapshot (unsaved edits included); every other uses
`LoadConversation` → `ConversationSnapshotBuilder.Build` → `PatchApplier.Apply(base, patch,
ignoreConflicts: true)`. A failure is `AppLog.Warn` plus a `LoadFailed` leaf, never a throw —
per the CLAUDE.md error-handling rule.

**Text fix-up is the resolver's responsibility.** `NodeEditSnapshot.DefaultText` and
`FemaleText` are `[property: JsonIgnore]`, so nodes the writer *added* come back from a patch
empty. The resolver rewrites them from `patch.Translations[primaryLanguage]` before handing the
snapshot over — the same fallback `ProjectFindService.cs:59-63` performs. Without this, every
patch-loaded conversation would silently count **zero words** and nothing would throw.

**Traversal** is a worklist from the open conversation, so only conversations actually reached
by jumps are loaded — not every patched conversation. The visited set terminates jump cycles.

## Pure types (`DialogEditor.Core/Analytics/`)

```csharp
public readonly record struct NodeRef(string Conversation, int NodeId);

public record JumpEdge(NodeRef From, NodeRef To);

public enum UnfollowedReason { NotPatched, Unresolved, LoadFailed }
public record UnfollowedJump(NodeRef From, string TargetLabel, UnfollowedReason Reason);

public record MultiConversationGraph(
    string RootConversation,
    IReadOnlyDictionary<string, ConversationEditSnapshot> Conversations,
    IReadOnlyList<JumpEdge>       Jumps,
    IReadOnlyList<UnfollowedJump> Unfollowed);
```

`Jumps` is a flat edge list, not an adjacency map: `PathStatsService` groups it by `From` once
at the start of `Analyze` to obtain `jumps(u)`. Keeping the transport type flat means the
resolver never has to think about lookup shape, and the grouping is one line at the consumer.

`TargetLabel` is what the UI shows for an unfollowed handoff: the resolved conversation name
when one was found (`NotPatched`, `LoadFailed`), and a localised "unknown target" placeholder
when resolution itself failed (`Unresolved`). It is display text, never an identifier.

`ConversationsSpanned` is `graph.Conversations.Count` — the conversations actually materialised,
so it counts the root plus every conversation genuinely reached, and never counts a handoff that
was reported but not followed.

## Path algorithm

**Two edge sets, not one.** A node's outgoing edges stay split into `links(u)` (alternatives —
the player picks one) and `jumps(u)` (spawns — all happen). Merging them into a single edge
list would destroy exactly the distinction decision 2 depends on. The DAG becomes `dagLinks`
plus `dagJumps`.

**`int` → `NodeRef` throughout.** `nodeById`, both DAG maps, `preds`, `forkStack`, and the
`(int, bool)` memo tuples all become `NodeRef`-keyed. The root is `NodeRef(openConversation, 0)`.

**Back-edge cut** runs the DFS over links ∪ jumps, so a cyclic handoff (A → B → A) is cut by
the same rule that already handles a hub loop — "a loop back to an earlier line is counted
once" now covers loops back to an earlier *conversation*.

**The arithmetic**, the one deviation from the existing service:

    Longest(u)  = Weight(u) + (links(u).Any() ? links(u).Max(Longest)  : 0) + jumps(u).Sum(Longest)
    Shortest(u) = Weight(u) + (links(u).Any() ? links(u).Min(Shortest) : 0) + jumps(u).Sum(Shortest)

**`ReachableSum` and `ChoiceFrontier`** walk links ∪ jumps as a plain union — both are
set-based, so the alternative/additive distinction does not apply. A jump whose entry node is a
player choice therefore places a fork *in another conversation* into the tree. `MaxForkDepth`
stays 10. Fork ordering is `(conversation == root ? 0 : 1, conversation, nodeId)`, so the open
conversation's forks stay on top and ordering stays deterministic.

**Endings** are nodes with neither links nor jumps (decision 6).

**Known imprecision — accepted deliberately.** `LongestTo` / `ShortestTo` (the root-to-ending
figures) walk predecessors taking max / min. Under additive spawn semantics this slightly
under-counts in the concurrent case: arriving at an ending in conversation B also entails
reading whatever conversation A continued on to, and a max-over-predecessors walk misses those
sibling words. Making it exact would require tracking spawn sets per path — a real complexity
jump for a case decision 3 already reports as a defect. `EndingStat`'s doc comment, which
already explains that per-ending figures need not reconcile with the header, is extended to
cover this second reason.

**API — one code path:**

```csharp
public static PathStatsReport Analyze(MultiConversationGraph graph);      // real implementation
public static PathStatsReport Analyze(ConversationEditSnapshot snapshot); // wraps into a
                                                                          // one-conversation,
                                                                          // no-jump graph
```

Single-conversation mode is the degenerate case of the new one, so the two cannot drift. An
equivalence test pins this.

The snapshot overload wraps into a graph whose `RootConversation` is **the empty string** — a
`ConversationEditSnapshot` carries no name, and inventing one would be a lie. Every `NodeRef` in
that mode is therefore `("", nodeId)`, so existing tests read `b.Choice.NodeId` and never need a
name. `IsInAnotherConversation` is consequently always false there, and no row shows a tag.

**Report changes:** `BranchStat.ChoiceNodeId: int` → `Choice: NodeRef`; `EndingStat.NodeId: int`
→ `Node: NodeRef`; `PathStatsReport` gains `IReadOnlyList<UnfollowedJump> Unfollowed` and
`int ConversationsSpanned`. `SpeakerWordCount` is unchanged — keyed by `SpeakerGuid`, it simply
sums across the union.

## ViewModel, settings, diagnostic

`AppSettings` gains `bool FollowConversationJumps` (default `false`) plus the static property
pair, mirroring `ReadingWordsPerMinute` (`AppSettings.cs:82`, `:257-262`).

`FlowAnalyticsViewModel` gains four optional, defaulted constructor parameters, so every
existing call site and test keeps compiling:

```csharp
Func<MultiConversationGraph>? resolveGraph                 = null,
bool                          followConversationJumps      = false,
Action<bool>?                 persistFollowJumps           = null,
Action<string, int>?          navigateToNodeInConversation = null
```

`RefreshPathStats` selects the overload:

    report = (FollowConversationJumps && _resolveGraph is not null)
           ? PathStatsService.Analyze(_resolveGraph())
           : PathStatsService.Analyze(snapshot);

`partial void OnFollowConversationJumpsChanged(bool)` persists through the callback and re-runs
`Refresh()`, mirroring `OnWordsPerMinuteChanged` — header and branch strings are baked into
plain strings at analysis time, so a setting change only reaches the UI by re-running.

The VM takes the setting value in and reports changes out, and never reads the settings file
itself — the rule stated at `FlowAnalyticsViewModel.cs:170-172`, which matters because this
suite runs serially owing to `AppSettings`/`Loc` global-state races.

`_navigateToNode` is `Action<int>` and reaches only the open conversation. Rows whose
`NodeRef.Conversation` differs from the root use `navigateToNodeInConversation`, wired to
`MainWindowViewModel.NavigateToFoundNode`, which already performs cross-conversation navigation
with the dirty guard for Find in Project and duplicate detection.

`PathBranchRowViewModel` and `PathEndingRowViewModel` gain `ConversationName` and
`IsInAnotherConversation`, and display the conversation only when it is not the root — so the
common single-file report looks exactly as it does today.

`FlowIssueKind` gains `ConversationJumpWhileContinuing`, raised for any node with both a
start-conversation script and at least one outgoing link. It joins the existing `Issues` list
and is a warning (`SeverityLabel` reserves error for `Unreachable`).

## Window

A "Follow conversation jumps" checkbox beside the reading-speed picker in the Playthrough-stats
header strip, carrying a `ToolTip.Tip` mirrored to `AutomationProperties.HelpText` that explains
both what it follows and that figures will grow.

- "Spanning N conversations" appears only when N > 1 — what makes a suddenly larger figure
  self-explaining.
- Cross-conversation rows carry a textual conversation tag; same-conversation rows do not.
- A **Handoffs not followed** section lists each unfollowed jump with its reason in words
  (*not patched by this project* / *target not found* / *could not be loaded*), so a
  project-patched-only boundary never silently drops content from a report.
- The endings hint gains extra text when the toggle is on, stating that a handoff node is no
  longer listed as an ending.

All strings are `Loc` keys — no inline text (CLAUDE.md). No colour-only encoding: every tag,
reason and severity is textual (Layer 2.5).

`MainWindowViewModel` wiring passes `resolveGraph`, the `AppSettings` value and its persist
callback, and `NavigateToFoundNode`.

## Testing (TDD, red first)

`PathStatsServiceTests` — existing assertions change only where they reach for a node id
(`b.ChoiceNodeId == 1` → `b.Choice.NodeId == 1`); the local `Node(...)` helper gains a `scripts`
parameter. That the rest stands unchanged is itself the evidence the single-conversation path
did not move. New:

- Handoff: terminal node + jump → longest = source + target's longest.
- **Concurrency is additive, not max** — a node with both a link and a jump. This test is what
  distinguishes the chosen arithmetic from the rejected one; it carries a comment saying so, so
  that folding jumps into the ordinary edge list cannot pass.
- `Shortest` sums jumps too.
- Jump cycle A → B → A terminates, counted once.
- A node with a jump is not an ending.
- A fork inside a jumped-to conversation appears with the correct `NodeRef`.
- `WordsPerSpeaker` sums one speaker across conversations.
- `ConversationsSpanned` counts distinct conversations.
- **Equivalence:** `Analyze(snapshot)` equals `Analyze(one-conversation graph)`. This pins
  "one code path" and is the load-bearing test of the whole approach.

`ConversationJumpResolverTests` (new) — on the existing `FakeGameDataProvider` /
`StubGameDataProvider`, no game folder, no disk:

- PoE2 `Guid` resolution and PoE1 `String` resolution, same node, two games.
- **`MarkConversationNodeAsRead` is not a jump** — the regression guard for the over-match trap.
- Unpatched → `NotPatched`; unresolvable GUID → `Unresolved`; throwing load → `LoadFailed`,
  warned not thrown.
- **Text fix-up** — an added node with empty `DefaultText` counts words from
  `patch.Translations`. Guards a failure that is silent, not loud.
- Only jump-reachable conversations are loaded, not every patched one.

`FlowAnalysisServiceTests` — `ConversationJumpWhileContinuing` fires for a node with a link and
a start-conversation script; not for a clean handoff; not for `MarkConversationNodeAsRead`.

`FlowAnalyticsViewModelTests` — toggle off routes to the snapshot overload, on routes to
`resolveGraph`; toggling persists and refreshes; toggle on with a null resolver falls back
safely; a cross-conversation row navigates through the two-argument delegate while a root row
uses the original. All values arrive by constructor; no test touches the real `settings.json`.

`AppSettings` round-trip for the new flag. `FlowAnalyticsWindowTests` smoke test that the window
constructs with jump rows and unfollowed leaves.

## Deferred (YAGNI)

- Following handoffs into conversations the project does not patch (bounded-hop or capped
  traversal over vanilla). Reported as leaves instead.
- Exact root-to-ending figures under concurrency — see the accepted imprecision above.
- Conversation-graph visualisation (a map of which conversations hand off to which). This
  design produces the edges; drawing them is a separate feature.
- Condition-aware pruning of jumps, as of links — part of #32, blocked on #1.
