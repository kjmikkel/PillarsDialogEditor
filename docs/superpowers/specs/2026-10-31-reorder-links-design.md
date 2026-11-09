# Reorder a node's links — design

Issue: [#359](https://github.com/kjmikkel/PillarsDialogEditor/issues/359). Follow-up to #353 (spec
`2026-10-31-first-passing-link-design.md`): both games take the first link that passes, in link order, so
order decides which lines are reachable, and the editor now fades links an earlier certain link hides.

**How this was decided.** The owner settled in conversation that reordering happens **on the canvas**, and
that link position numbers show **only while the source node is selected**. Everything else not taken from
the code is **my call** (the designing agent's), marked _(my call)_.

## 1. Intent

A writer sees a faded link (or the Flow Analytics warning) and wants to fix it by putting the link before the
one that hides it. Today the only way is deleting and re-adding links, and even then project patches cannot
express an order: `PatchApplier` keeps the game file's link order and appends added links, so any order the
writer builds on the canvas is lost on save → reopen and in exported mods.

### Goals
- Move a link earlier or later among its node's links, from the link's right-click menu, undoable.
- See each link's position while its source node is selected.
- The order survives save, reopen, export (mod packaging), patch merging and git-conflict merging.

### Non-goals
- Drag-to-reorder, keyboard shortcuts, a reorder dialog _(my call: two menu items cover the need; more later
  if writers ask)_.
- Reordering from Node Details (the owner chose the canvas).
- Bank child order (`ChildNodeIDs`) — a different list.

### Anti-goals
- A reorder that looks saved but silently reverts on reopen or export.
- Changing any existing project's output: a patch with no order record applies exactly as today.

## 2. Canvas

**Menu.** The connection context menu (`ConnectionContextMenu` in `ConversationView.axaml`, today only "Delete
Connection") gains two `AccessibleMenuItem`s above it _(my call: above, so the destructive item stays last)_:
- **Move Link Earlier** — swaps the link with the previous link of the same source node.
- **Move Link Later** — swaps it with the next one.

Disabled at the ends of the list and in reading mode (hidden there, like Delete Link in Node Details).
Tooltips (also `AutomationProperties.HelpText`) explain why order matters, e.g. "Move this link one place
earlier among the links of node {n}. The game takes the first link that passes, in order, so an earlier link
can hide later ones."

**Order on the canvas.** Link order is `ConversationViewModel.Connections` filtered by source (as
`BuildSnapshot` saves it, see #353). A move swaps the two connections' positions in `Connections`; nothing else
in that collection moves _(my call: links of other nodes keep their indices, so undo is exact)_. It goes
through the undo stack as one command ("Move link" in the Edit menu's undo text), marks the conversation
modified, and refreshes hiding for that source (#353's `RefreshShadowing`).

**Position numbers.** While a node is selected, each of its outgoing links shows a small number (1, 2, 3, …)
near the source end of the line. Only when the node has two or more links _(my call: a single link has no order
to show)_. Uses an existing small-text brush and font token; not interactive, so no tooltip, but the
connection's UIA help text includes "link {i} of {n}" so the order is reachable without sight _(my call)_.
Placement on the line is the builder's.

**As built.** Each number sits on a small chip in the canvas background colour, on its own link's curve about a
third of the way from the node (`LinkLabelGeometry.LabelPoint`, which mirrors Nodify's Bézier control points).
Every link of a node leaves the same port, so a fixed offset from the port would stack the numbers on top of each
other. The chip keeps a number readable where it crosses another line.

**Right-click on a link.** Nodify 6.6.0's `BaseConnection.OnPointerReleased` overrides Avalonia's
`Control.OnPointerReleased` without calling it, and that base method is what raises `ContextRequested` on a
right-button release. So a link's context menu never opened, which predates this work (Delete Connection was
unreachable too). The canvas now draws links with `CanvasConnection`, a `Nodify.Connection` subclass that raises
`ContextRequested` itself; a test pins Nodify's behaviour so an upgrade that fixes it is noticed. Links are still
not reachable by keyboard or UI Automation: #361.

## 3. Patch format

`NodeModification` gains `IReadOnlyList<int>? UpdatedLinkOrder` (init, default null): the target node ids of the
node's links in their new order, written only when the order differs from what applying the rest of the patch
would give. Replace-all, like `UpdatedConditions`.

- **DiffEngine:** after computing added / deleted / modified links, compute the order `PatchApplier` would give
  (base links minus deleted, in base order, then added links in current order). If the current order differs,
  set `UpdatedLinkOrder` to the current order. A node whose only change is its order still gets a
  `NodeModification` (update the "no change" check).
- **PatchApplier:** after building the link list as today, if `UpdatedLinkOrder` is set, sort links by their index
  in it. Links not in the list (for example a link another patch or a game update added) keep their relative
  order **after** the listed ones _(my call: never drop a link)_. Ids in the list with no link are ignored.
- **Schema:** `ConversationPatch.CurrentSchemaVersion` 3 → 4, with a no-op `ConversationPatchV3ToV4` step
  _(my call: an older editor reading a v4 patch would silently drop the order; the version gate makes it refuse
  instead, which is how earlier format changes were handled)_. v3 patches load unchanged.
- **PatchMerger** (combining patches): later wins for `UpdatedLinkOrder`, like the other replace-all fields.
- **ConflictDetector** (several mods on one conversation): two projects that both set `UpdatedLinkOrder` on the
  same node conflict, reported as the field "LinkOrder" like `Conditions`.
- **Git conflict MergeBuilder:** "LinkOrder" is a pickable field in `ApplyField`, like `Conditions` /
  `Scripts`, and every `NodeModification` it rebuilds carries `UpdatedLinkOrder` through.
- **Diff view / history:** a node with `UpdatedLinkOrder` shows a "Links reordered" change line. Check every
  place that summarises a `NodeModification` (`ConversationChange`, `NodeLinkAnalyzer`, blame) so the field is
  not ignored where it matters _(builder: list them)_.

**Serializers** for the game files already write links in snapshot order (#353's round-trip tests pin it); add
a test that a reordered node saves its links in the new order for PoE1 and PoE2.

## 4. Localisation and UI rules
All strings in `DialogEditor.Avalonia/Resources/Strings.axaml`. Menu items get their UIA name from their
header. Tooltips required on both items.

## 5. Left to the builder
- Number placement and styling on the line; how the canvas knows "selected".
- Undo command shape (a dedicated `MoveLinkCommand` is expected).
- Test names and layout.

## 6. Follow-ups
- #361: reach a canvas link (its menu and its "link i of n" text) from the keyboard and through UI Automation.
