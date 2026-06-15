# Keyboard Connect Mode — Design

**Date:** 2026-06-15
**Status:** Approved (brainstormed with user; entry points, highlight style, edge-case
behaviour, and announcements decided by user)
**Gap:** Gaps.md → Accessibility — Assistive Technology & Keyboard (audit 2026-06-12),
item 4's deferred follow-up — "keyboard connection creation ('connect mode') ...
needs its own interaction design pass."

## Problem

The 2026-06-12 canvas keyboard editing work (Gaps.md item 4) made the canvas
navigable and structurally editable from the keyboard — except for creating
connections between nodes, which remained mouse-only (drag from a node's Output
connector to another node's Input connector via `PendingConnectionViewModel`).
This is the last mouse-only structural operation on the canvas, blocking full
WCAG 2.1.1 (all functionality keyboard-operable) for the editor's core surface.

## Key simplification

Each `NodeViewModel` exposes exactly **one** `Output` connector and **one** `Input`
connector (`Inputs`/`Outputs` are single-element lists — `NodeViewModel.cs:279-280`).
The general "connect mode" problem — pick a connector, then pick another connector —
therefore reduces to **pick a source node, then pick a target node**; connector
selection is implicit. This lets connect mode reuse the existing topological
navigation keys (arrows / PgUp/PgDn / Home) from the 2026-06-12 work as-is: the
"target candidate" *is* `SelectedNode`, moved the normal way.

## Decisions taken with the user

1. **Entry: both a context-menu item and a shortcut.** "Connect to…" is added to
   the existing node context menu (`Apps`/`Shift+F10`, alongside "Delete node" /
   "Add connected node") for discoverability, *and* **Ctrl+L** ("Link" — currently
   unused) starts connect mode on the current selection for speed.
2. **Confirm/cancel edge cases: silent no-op, stay in connect mode.** If the
   target candidate is the source node itself, or a node already connected to the
   source, Enter does nothing and connect mode remains active — matching the
   existing mouse `PendingConnectionViewModel.Complete` behaviour (self/duplicate
   silently clears the pending source). The user navigates to a different node and
   presses Enter again, or presses Escape to cancel.
3. **Source highlight: dashed border + 🔗 badge (top-left corner).** Mirrors the
   existing diff-status pattern (coloured border + corner glyph badge — Layer 2.5
   non-colour-encoding), but **dashed** (vs. the diff overlay's solid border) and
   placed **top-left** (the diff badge occupies top-right), so the two never
   collide. Reuses an existing amber/warning-toned `Brush.*` token — no new colour
   token, so `NoStrayHexTests`/`PaletteSetParityTests`/`PaletteGoldenTests` are
   unaffected.
4. **Announcements: yes, via the existing live region.** Entering, confirming, and
   cancelling connect mode set `MainWindowViewModel.StatusText`, which the item-8
   `StatusLiveRegion` already announces to screen readers. Sighted keyboard users
   get the same information via the status bar (and, per item 5, via
   `FocusHintBar`/`DisplayStatusText` if focus moves).

## Key map (additions)

| Key | Context | Action |
|---|---|---|
| Ctrl+L | Editor focused, a node selected, canvas editable | Start connect mode: the selected node becomes the **source**. |
| (Context menu) "Connect to…" | Apps/Shift+F10 on a node | Same as Ctrl+L, for the node the menu was opened on. |
| → ← ↑ ↓ / PgUp / PgDn / Home | While connecting | Unchanged — move the **target candidate** (`SelectedNode`) exactly as outside connect mode. |
| Enter | While connecting | **Confirm**: create a connection from the source's Output to the target candidate's Input, then exit connect mode. Self/duplicate target → silent no-op, stays in connect mode. |
| Enter | Not connecting, node selected | Unchanged — focus the detail panel (2026-06-12 behaviour). |
| Escape | While connecting | **Cancel** connect mode (no connection created, selection unchanged). |
| Escape | Not connecting | Unchanged — deselect. |

All other connect-mode-active keys (nudge, delete, etc.) are unaffected and continue
to operate on `SelectedNode`/the source/target as today; no new restrictions are
introduced beyond the Enter/Escape reinterpretation above.

## Components

### `NodeViewModel` (addition)

- `IsConnectionSource` (bool, `[ObservableProperty]`) — true only for the node
  currently acting as connect-mode source. Drives the highlight overlay.

### `ConversationViewModel` (additions)

- `IsConnecting` (bool, observable).
- `ConnectionSource` (`NodeViewModel?`, observable).
- `BeginConnect(NodeViewModel node)`:
  - No-op (return `false`) if `!IsEditable` or `IsConnecting` is already `true`.
  - Otherwise: `SelectNode(node)` (source becomes the target-candidate starting
    point too — arrow keys move away from it), `ConnectionSource = node`,
    `node.IsConnectionSource = true`, `IsConnecting = true`.
  - Raises `ConnectModeChanged(ConnectModeChange.Started, node, null)`.
  - Returns `true`.
- `TryBeginConnect()`:
  - Returns `false` if `SelectedNode is null`; otherwise delegates to
    `BeginConnect(SelectedNode)`.
- `TryConfirmConnection()` — called only while `IsConnecting`:
  - If `SelectedNode is not null && SelectedNode != ConnectionSource` and no
    existing `ConnectionViewModel` already links `ConnectionSource.Output` →
    `SelectedNode.Input`:
    - `AddConnection(ConnectionSource.Output, SelectedNode.Input)`.
    - Exit connect mode (clear `IsConnectionSource`, `ConnectionSource = null`,
      `IsConnecting = false`).
    - Raise `ConnectModeChanged(ConnectModeChange.Connected, source, target)`.
  - Else: no state change (stays connecting). No event raised.
  - Always returns `true` (the key is consumed either way).
- `CancelConnect()` — called only while `IsConnecting`:
  - Exit connect mode (same cleanup as above, no connection added).
  - Raise `ConnectModeChanged(ConnectModeChange.Cancelled, source, null)`.
  - Always returns `true`.
- `ConnectModeChanged` event:
  `EventHandler<ConnectModeEventArgs>` where
  `ConnectModeEventArgs(ConnectModeChange Change, NodeViewModel Source, NodeViewModel? Target)`
  and `ConnectModeChange` is `{ Started, Connected, Cancelled }`.
- `DeleteNode(NodeViewModel node)`: if `node == ConnectionSource`, call
  `CancelConnect()` before proceeding with the existing delete logic — connect
  mode cannot reference a deleted node.

### `ConversationView.axaml.cs` (`Editor_KeyDown`)

- New `ctrl` arm: `Key.L when ctrl => vm.TryBeginConnect()`.
- `Key.Enter` gets two arms (order matters — first match wins):
  1. `Key.Enter when none && vm.IsConnecting => vm.TryConfirmConnection()`
  2. `Key.Enter when none && vm.SelectedNode is not null => RaiseFocusDetail()`
     (unchanged, existing arm).
- `Key.Escape` gets two arms:
  1. `Key.Escape when none && vm.IsConnecting => vm.CancelConnect()`
  2. `Key.Escape when none => vm.Deselect()` (unchanged, existing arm).
- All other arms unchanged.

### `ConversationView.axaml` (node template, context menu)

- New `MenuItem` in `nodify:Node.ContextMenu`:
  `Header="{StaticResource Menu_ConnectToNode}"`,
  `Command="{Binding $parent[UserControl].DataContext.BeginConnectCmdCommand}"`,
  `CommandParameter="{Binding}"`, `CanExecute = IsEditable` (via
  `[RelayCommand(CanExecute = nameof(IsEditable))]` wrapper `BeginConnectCmd`,
  mirroring `DeleteNodeCmd`/`AddConnectedNodeCmd`), with
  `ToolTip.Tip`/`AutomationProperties.HelpText` set to a new
  `ToolTip_ConnectToNode` resource.
- New overlay `Border` (sibling to the existing diff-status overlay, in the same
  200px node `Grid`):
  - `BorderThickness="3"`, dashed `StrokeDashArray` distinct from the diff
    overlay's solid border, `BorderBrush` bound to `IsConnectionSource` via a
    converter (or a simple `BoolToBrushConverter` to `Transparent`/the chosen
    amber token), `IsVisible`/transparent-when-false so it renders as nothing
    when not the connect-mode source (Layer 2.5 precedent).
  - 🔗 badge `Border` (18×18, rounded), top-left corner (`Margin="-9,-9,0,0"`,
    `HorizontalAlignment="Left"`), same visibility binding, reusing the diff
    badge's structural pattern but mirrored to the opposite corner.

### `MainWindowViewModel` (wiring)

- In the existing `Canvas`-wiring block (alongside the `Canvas.PropertyChanged`
  and `Canvas.Connections.CollectionChanged` subscriptions), subscribe to
  `Canvas.ConnectModeChanged` and set `StatusText`:
  - `Started` → `Loc.Format("Status_ConnectMode_Started", e.Source.NodeId)`
  - `Connected` → `Loc.Format("Status_ConnectMode_Connected", e.Source.NodeId, e.Target!.NodeId)`
  - `Cancelled` → `Loc.Get("Status_ConnectMode_Cancelled")`

### `LegendWindow` + strings

New "Connect mode" group in the existing Canvas-keys section (`Legend_CanvasKeys`),
following the established key/description row pattern:

- `Legend_Key_CtrlL` / `Legend_Key_CtrlL_Desc` — e.g. *"Ctrl + L — Start connecting
  the selected line to another one. (Also available from the right-click /
  Menu-key menu as 'Connect to…'.)"*
- A short addition to the existing Enter/Escape rows (or a new combined row)
  explaining the contextual behaviour while connecting, e.g. *"While connecting:
  use the arrow keys to pick the destination line, press Enter to confirm, or
  Escape to cancel."*

New strings (all in `Strings.axaml`, localized, no hard-coded text):
- `Menu_ConnectToNode`, `ToolTip_ConnectToNode`
- `Legend_Key_CtrlL`, `Legend_Key_CtrlL_Desc`
- `Legend_ConnectMode_WhileConnecting` (the Enter/Escape explanation row)
- `Status_ConnectMode_Started`, `Status_ConnectMode_Connected`,
  `Status_ConnectMode_Cancelled`

## Edge cases

- **No node selected, Ctrl+L pressed:** `TryBeginConnect()` returns `false`
  (no-op) — matches every other `Try*` keyboard method's "nothing to act on"
  convention.
- **Read-only canvas (diff view), Ctrl+L / "Connect to…":** both gated on
  `IsEditable` (the `BeginConnect`/`TryBeginConnect` early-return and the menu
  item's `CanExecute`), matching every other structural edit.
- **Already connecting, Ctrl+L or "Connect to…" pressed again (possibly on a
  different node):** `BeginConnect` no-ops — the user must confirm or cancel the
  current connect-mode session first. Deliberately simple; revisit only if this
  proves confusing in practice.
- **Source node deleted while connecting** (existing Delete key still wired):
  `DeleteNode` calls `CancelConnect()` first, so `IsConnectionSource`/
  `ConnectionSource`/`IsConnecting` are cleared consistently.
- **Mouse interaction during connect mode:** unrestricted. Clicking another node
  with the mouse calls the existing `OnSelected`/`SelectNode` path, which simply
  updates the target candidate — mouse and keyboard can be mixed mid-connect-mode.
- **Self-loop attempt** (target candidate == source, e.g. Enter pressed
  immediately after starting connect mode without navigating): covered by decision
  2 — silent no-op, stays connecting.
- **Duplicate connection attempt:** covered by decision 2 — silent no-op, stays
  connecting (same `Connections.Any(...)` check `PendingConnectionViewModel.Complete`
  already uses).

## Testing (strict red/green TDD)

`DialogEditor.Tests/ViewModels/ConversationViewModelTests.cs` (plain xUnit):
- `BeginConnect`: sets `IsConnecting`, `ConnectionSource`, `IsConnectionSource`,
  selects the node, raises `Started`; no-ops when `!IsEditable` or already
  connecting.
- `TryBeginConnect`: delegates correctly; `false` when `SelectedNode is null`.
- `TryConfirmConnection`: creates the connection and exits connect mode on a
  valid target (raises `Connected` with correct source/target); self-target
  no-op (stays connecting, no event); duplicate-target no-op (stays connecting,
  no event); always returns `true` while connecting.
- `CancelConnect`: clears state, raises `Cancelled`, returns `true`.
- `DeleteNode`: deleting the current `ConnectionSource` calls `CancelConnect()`
  (state cleared) before the node is removed.

Headless `DialogEditor.Tests/Views/ConversationViewTests.cs` (`[AvaloniaFact]`,
existing infra):
- Ctrl+L on a selected, editable canvas sets `vm.IsConnecting = true`.
- Enter while connecting calls `TryConfirmConnection` (not `RaiseFocusDetail`) —
  assert via resulting VM state / no `FocusDetailRequested` raised.
- Escape while connecting calls `CancelConnect` (not `Deselect`) — assert
  `SelectedNode` is unchanged and `IsConnecting` becomes `false`.
- Normal Enter/Escape behaviour (not connecting) is unchanged — regression check
  against the 2026-06-12 tests.

`DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`:
- Each `ConnectModeChanged` case (`Started`/`Connected`/`Cancelled`) sets
  `StatusText` to the expected formatted/localized string.

Legend documentation is covered by the localisation structure itself (resource
keys present and non-empty), not behavioural tests — consistent with the
2026-06-12 precedent.

## Out of scope

- Multi-connector nodes (the "pick a connector" step) — not applicable; every
  node has exactly one Output and one Input today, and nothing in this design
  assumes otherwise, but it does not generalise to a future multi-connector node
  without revisiting `BeginConnect`'s "node implies connector" assumption.
- Deleting connections from the keyboard — out of scope for this slice; the
  existing context menu's "Delete connection" (`DeleteConnectionCmd`) remains
  mouse-only via `ConnectionViewModel`'s context menu, unchanged.
- Visual indication of *which* connections already exist from the source while
  navigating (e.g. dimming already-connected targets) — the duplicate check at
  confirm time (decision 2) covers correctness; a richer live preview is a
  possible future enhancement, not required here.
