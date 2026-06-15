# Canvas Keyboard Editing — Design

**Date:** 2026-06-12
**Status:** Approved (brainstorm with user; scope/traversal/entry decided by user)
**Gap:** Gaps.md → Accessibility — Assistive Technology & Keyboard, item 4

## Problem

The conversation canvas — the editor's core surface — is mouse-only. Keyboard users
(motor-impaired, keyboard-first, or screen-reader users who finished items 1–2) can
browse conversations and edit node *content*, but cannot move between nodes, reposition
them, or reach the node context menu without a pointer. This blocks WCAG 2.1.1 (all
functionality keyboard-operable) for the app's main interaction.

## Decisions taken with the user

1. **Scope: navigate + edit structure.** Arrow traversal, node nudging, context menu,
   detail-panel handoff. Free-form **connection creation stays mouse-only** in this
   slice — a keyboard "connect mode" is a separate, genuinely hard interaction design,
   deferred (recorded as a follow-up in Gaps.md item 4 when this ships).
2. **Traversal: topological, not spatial.** Arrows follow the conversation structure
   (links), matching both the left-to-right auto-layout (`AutoLayoutService`: X = depth
   layer, Y = row) and the mental model writers have of a dialog tree. Traversal stays
   correct after nodes are hand-rearranged.
3. **Entry: restore last selection.** Focusing the canvas with nothing selected restores
   the last selection (root on first focus). PgUp/PgDn cycle through **all** nodes so
   orphans are keyboard-reachable (full coverage).
4. **In-app documentation is mandatory** (user requirement): the key map is documented
   in the Legend window in plain, non-technical language, fully localized.

## Key map

Keys act when the `NodifyEditor` has keyboard focus. The editor is a single Tab stop;
keys never fire while the search box (its own focus target) is focused.

| Key | Action |
|---|---|
| → | Select a linked **child**. Multiple children: the one vertically nearest the current node; ties broken by link order. Repeat presses do not cycle. |
| ← | Select a **parent**; same nearest-by-Y rule for multiple parents. |
| ↑ / ↓ | Previous/next **sibling** in visual (Y) order. Siblings = children of the current node's primary parent (the ←-target). Parentless nodes (roots, orphans): siblings = all other parentless nodes. |
| PgUp / PgDn | Cycle **all** nodes in stable `Nodes`-collection order, wrapping. Guarantees orphan reachability. |
| Home | Select + center the **root** node (keyboard twin of the ⌂ button). |
| Ctrl+arrows | **Nudge** the selected node 10 px in that direction; Ctrl+Shift+arrows: 50 px. Plain `Location` set — same semantics as a drag move (drags are not individually undoable today; nudges deliberately match rather than invent new undo behaviour). |
| Enter | Move focus to the **detail panel**'s first field for the selected node. |
| Menu key / Shift+F10 | Open the selected node's **context menu** (Delete node / Add connected node). |
| Escape | Deselect. |
| Delete | Delete selected node — already wired (existing MainWindow handler), unchanged. |

Every keyboard selection goes through the same path as mouse selection (sets
`IsSelected`, clears others, sets `SelectedNode`), so connection highlighting and the
detail panel react identically. The viewport follows each selection via the existing
`Editor.BringIntoView` (already used by search navigation).

## Approach (chosen: B — ViewModel-first)

Considered:
- **A. View-layer only** (KeyDown switch in code-behind, like MainWindow's global keys):
  rejected — the subtle logic (nearest-parent, sibling order, cycle order) would be
  untestable except through UI tests, against the project's no-logic-in-views stance.
- **B. ViewModel-first** (chosen): pure traversal logic in `DialogEditor.ViewModels`,
  TDD-able with plain xUnit; the view contributes only a dumb key→method mapping.
- **C. Focusable Nodify containers + Avalonia XY focus navigation**: rejected — fights
  the third-party container/selection model, spatial-only (conflicts with decision 2),
  poorly testable.

## Components

### `CanvasNavigationService` (new; `DialogEditor.ViewModels`; pure static logic)

Functions over the existing `Nodes`/`Connections` collections:

- `GetChild(node, nodes, connections)` / `GetParent(...)` — nearest-by-Y among
  candidates, ties by link order.
- `GetSibling(node, offset, ...)` — ±1 within the Y-ordered sibling list (children of
  the primary parent; parentless nodes form one sibling group).
- `CycleNext(node?, nodes)` / `CyclePrev` — collection-order cycle with wrap; `null`
  current → first/last node.

Adjacency is derived per call from `Connections` (source connector → parent side,
target connector → child side; connectors map to nodes via each node's
`Inputs`/`Outputs`). No Avalonia types anywhere in the service.

### `ConversationViewModel` (additions)

- `SelectNode(node)` — the single selection path (mouse `OnSelected` callbacks converge
  on it too): set `IsSelected`, clear others, set `SelectedNode`.
- `TryNavigate(NavDirection)` → bool — arrows; no-op (false) when no candidate.
- `TryCycle(forward)` → bool — PgUp/PgDn.
- `NudgeSelected(dx, dy)` — Location += delta; no-op when nothing selected.
- `EnsureKeyboardSelection()` — nothing selected → restore last selection, else root;
  tracks "last selection" internally (updated by `SelectNode`).

### `ConversationView.axaml.cs` (thin glue)

One `KeyDown` handler on the editor: map key (+modifiers) → ViewModel call; on any
selection change, `BringIntoView(SelectedNode location)`; set `e.Handled` for consumed
keys. `GotFocus` → `EnsureKeyboardSelection()`. Enter raises a `FocusDetailRequested`
event consumed by `MainWindow` (which owns the detail panel) to focus its first field.
Menu key resolves the selected node's container and opens its existing `ContextMenu`.

**Known risk (resolve during implementation):** Nodify's editor may claim some keys
(e.g. arrows for its own behaviour). The headless view tests pin our handling; if
Nodify conflicts, attach the handler with `RoutingStrategies.Tunnel`.

### `LegendWindow` + strings (the user-required documentation)

A new "Canvas keyboard" group in the existing shortcut section, same key/description
row pattern, all strings localized in `Strings.axaml`, phrased for non-technical
writers (e.g. "→ — Move to the line that follows this one"; "Page Up / Page Down —
Step through every node, including unconnected ones"). Tooltip rule is unaffected (no
new interactive controls).

## Edge cases

- **Empty canvas:** every key no-ops silently (all Try* return false).
- **Loops/back-edges** (legal in conversations): traversal is per-link — no cycle
  detection needed; PgUp/PgDn uses collection order, so it always terminates.
- **Selected node deleted:** existing `SelectedNode = null` path; the next navigation
  key re-enters via `EnsureKeyboardSelection()`.
- **Search box focused:** the editor never receives the keys (separate focus target);
  search-selected nodes become the traversal starting point (selection is shared).
- **Node with no parent but with children** (orphan subtree root): ← no-ops; ↑/↓ move
  within the parentless group; → enters the subtree.

## Testing (strict red/green TDD)

Unit tests (`DialogEditor.Tests`, plain xUnit, no Avalonia):
- child/parent selection: single, multiple (nearest-by-Y), tie → link order, none → false
- siblings: ordering by Y, ends (no wrap), parentless group, single node
- cycle: order, wrap both directions, from-null entry, orphan reachability
- nudge: location arithmetic, both step sizes, no-selection no-op
- `EnsureKeyboardSelection`: last-selection restore, root fallback, empty canvas

Headless view tests (`[AvaloniaFact]`, existing infra):
- arrow KeyDown on focused editor changes `SelectedNode`
- Ctrl+arrow changes `Location`; Ctrl+Shift uses the larger step
- Enter raises `FocusDetailRequested`
- keys while the search box is focused change nothing
- Escape deselects; `e.Handled` set on consumed keys

Legend documentation is covered by the localisation structure itself (resource keys),
not behavioural tests.

## Out of scope (this slice)

- Keyboard connection creation ("connect mode") — deferred, recorded in Gaps.md.
- Focus ring on the canvas node beyond Nodify's existing selection visual.
- Minimap keyboard interaction.
- Screen-reader announcement of the selected node (depends on item 8's live-region work).
