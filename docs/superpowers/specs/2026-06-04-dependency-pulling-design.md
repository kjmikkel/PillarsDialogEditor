# Automatic Dependency-Pulling for Selective Apply — Design

**Date:** 2026-06-04
**Status:** Approved (brainstorming complete; ready for implementation plan)

## Goal

When the user ticks a changed node to bring in (selective apply), automatically
tick the **added** nodes it links to, transitively, so a brought-in node's links
don't point at nodes that were never created. Closes the last selective-apply
follow-up in `Gaps.md`:

> "One follow-up remains intentionally: automatic dependency-pulling."

## Background

Selective apply ("bring in") cherry-picks changed nodes from a source project
version into the working copy. `NodeApplyBuilder.Apply(target, source, selected)`
takes a flat set of `(conversation, nodeId)` selections; for each selected node
the target's contribution becomes the source's. The UI is a checkbox tree:
`DiffViewModel.Groups` of `ConversationChangeViewModel`, each holding
`NodeChangeViewModel`s (`NodeId`, `Kind`, `IsSelected`).

Two distinct "incomplete bring-in" failure modes exist:
- **Deleted target** — a link to a node deleted by the same patch. Detected by
  `NodeLinkAnalyzer` and surfaced by the dangling-link panel ("warn, but allow").
- **Un-pulled added target** — you tick an *added* node A that links to an
  *added* node B, bring in A but not B, so A's link points at a node that was
  never created. `NodeLinkAnalyzer` does **not** flag this (it only inspects
  deleted targets). Dependency-pulling *prevents* it at selection time.

This feature addresses the second mode.

## Decisions (from brainstorming)

- **Dependency =** outgoing link targets that are **Added** in the diff, pulled
  transitively. Modified/unchanged targets already exist (no pull); Removed
  targets can't be satisfied by pulling (left to the dangling panel). Intra-
  conversation (links are node IDs within the conversation).
- **Trigger:** live — ticking a node immediately auto-ticks its added
  dependencies (transparent in the checkbox tree). Unticking does **not**
  auto-untick dependencies.
- **Control:** a default-on toggle ("Also bring in linked nodes") so power users
  can disable it for single-node picks.
- **No retroactive pull:** switching the toggle on affects only nodes ticked
  afterward, not the current selection.

## Design

### Component 1 — `DependencyClosure` (new, pure)

Location: `DialogEditor.Patch/Diff/DependencyClosure.cs`

```
public static IReadOnlySet<int> Expand(
    int start,
    IReadOnlyDictionary<int, IReadOnlyList<int>> outgoing,
    IReadOnlySet<int> addedIds)
```

Returns the set of node IDs to additionally select: the transitive closure of
`outgoing` edges reachable from `start`, keeping only targets present in
`addedIds`. The traversal follows edges from any reached added node (so a chain
A→B→C pulls both B and C). Cycle-safe via a visited set. `start` itself is not
included in the result. A node with no qualifying targets yields an empty set.

Fully unit-testable; no dependencies beyond the inputs.

### Component 2 — `ConversationChangeViewModel` auto-pull

- New members: a method/setter to receive this conversation's dependency data —
  `outgoing` (`Dictionary<int, IReadOnlyList<int>>`) and `addedIds`
  (`IReadOnlySet<int>`) — and a `bool AutoPullEnabled` property.
- In `OnNodeSelectionChanged`, when a node has just become selected and
  `AutoPullEnabled` is true, run `DependencyClosure.Expand` from that node and set
  each resulting node's `IsSelected = true`. Reuse the existing
  `_suppressRollDown` guard so the cascade fires a single `SelectionChanged`.
- To know *which* node became selected, change the per-node subscription in
  `Add(...)` from the parameterless `node.SelectionChanged += OnNodeSelectionChanged`
  to a lambda that captures the node: `node.SelectionChanged += () =>
  OnNodeSelectionChanged(node);`. `OnNodeSelectionChanged(NodeChangeViewModel node)`
  then inspects `node.IsSelected` and runs auto-pull only on a transition to
  selected (not on deselect).

Implementation note: because `DependencyClosure.Expand` computes the *full*
transitive set from the trigger node in one call, the dependency ticks are
applied under suppression and do not need to re-trigger the closure.

### Component 3 — `DiffViewModel`

- `[ObservableProperty] private bool _autoPullDependencies = true;`
- In `Recompute`, after constructing each `ConversationChangeViewModel`, build its
  dependency data from **`SourceProject`** (the bring-in source) and pass it in:
  - `outgoing[nodeId]`: for an added source node, `n.Links` → `ToNodeId`; for a
    modified source node, `m.AddedLinks` + `m.ModifiedLinks` → `ToNodeId`.
  - `addedIds`: the conversation change's `Added` set.
  - Set `group.AutoPullEnabled = AutoPullDependencies`.
- `OnAutoPullDependenciesChanged` propagates the new value to every group's
  `AutoPullEnabled`.
- When `SourceProject` is null (neither endpoint is the working copy, so apply is
  disabled), the dependency map is empty — auto-pull is a safe no-op.

### Component 4 — View + strings

A default-checked `CheckBox` "Also bring in linked nodes" bound to
`AutoPullDependencies`, placed near the apply bar in `DiffWindow.axaml`, with a
`ToolTip.Tip`. New string keys `Diff_AutoPullLabel` and `ToolTip_Diff_AutoPull`.

## Data flow

```
user ticks node A
  → ConversationChangeViewModel sees A → selected, AutoPullEnabled
  → DependencyClosure.Expand(A, outgoing, addedIds) → { B, C, ... }
  → set B, C IsSelected = true (suppressed) → one SelectionChanged
  → DiffViewModel recomputes CanApply / applied-preview
```

## Error handling

Pure in-memory graph work; no new IO or exceptions. Missing source patch → empty
`outgoing` → no pull. Cycles terminate via the visited set. Targets not in
`addedIds` are ignored.

## Testing (red/green TDD)

- **`DependencyClosure`:** single edge pulls the target; transitive chain A→B→C
  pulls B and C; targets absent from `addedIds` (Modified/Removed/base) are
  excluded; a cycle A→B→A terminates; a node with no qualifying targets returns
  empty.
- **`ConversationChangeViewModel` / `DiffViewModel`:** ticking an added node that
  links to another added node auto-ticks the target; the pull is transitive;
  with the toggle off no pull happens; Modified and Removed link targets are not
  pulled; unticking the source leaves the dependencies ticked; toggling the flag
  on does not retroactively expand the existing selection.

## Intentional limitations / deferred follow-ups

- **Outgoing only** — no pulling of nodes that link *into* the selected node.
- **No auto-untick** — pulled nodes remain selected; the user removes them
  manually.
- **No retroactive pull** on toggle-on — affects subsequent ticks only.
- **Deletion-dangling unaffected** — the dangling-link panel still warns for links
  to deleted targets; dependency-pulling cannot satisfy those.

## Out of scope (YAGNI)

- A preview/diff of exactly which nodes were auto-pulled (the ticked checkboxes
  already show it).
- Pulling across conversations (links are intra-conversation).
- Distinguishing "pulled" vs "manually ticked" nodes visually.
