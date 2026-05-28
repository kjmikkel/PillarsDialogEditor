# ViewModel Editing Test Coverage — Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the documented gap in `ConversationViewModelEditTests.cs` — six behavior groups covering `AddConnectedNode`, redo for all operation types, redo-stack clearing, `CanUndo`/`CanRedo` state, and `RestoreLayout`.

**Architecture:** Pure additions to the existing `ConversationViewModelEditTests.cs` test class, using the established `MakeVm()` / `MakeNode(id)` helpers. No new files, no new helpers, no production-code changes.

**Tech Stack:** xUnit, `DialogEditor.ViewModels`, existing `StubDispatcher` and `StubStringProvider` helpers.

---

## Behavior Groups

### 1 — `AddConnectedNode` structure

- New node appears in `vm.Nodes`.
- A connection from parent → new node appears in `vm.Connections`.
- `vm.SelectedNode` switches to the new node.
- New node inherits parent's `SpeakerCategory`, `SpeakerGuid`, `ListenerGuid`, `DisplayType`, `Persistence`.
- New node's `NodeId` is not equal to the parent's `NodeId`.

### 2 — `AddConnectedNode` undo semantics

`AddConnectedNode` pushes two separate commands onto the undo stack (AddNode, then AddConnection). Consequences:

- After one `Undo()`: the connection is removed but the node is still present.
- After a second `Undo()`: the node is also removed.
- After one `Redo()` from that state: node reappears (no connection yet).
- After a second `Redo()`: connection reappears.

### 3 — Redo for each operation type

Currently zero redo tests exist. One test per operation:

| Test name | Setup | Assert after redo |
|---|---|---|
| `RedoAddNode_RestoresNode` | Add → Undo → Redo | node in `Nodes` |
| `RedoDeleteNode_RemovesNodeAgain` | Add → Delete → Undo → Redo | node gone, connections empty |
| `RedoAddConnection_RestoresConnection` | Add two nodes, AddConnection → Undo → Redo | connection in `Connections` |
| `RedoDeleteConnection_RemovesConnectionAgain` | Add two nodes + connection, DeleteConnection → Undo → Redo | `Connections` empty |

### 4 — Redo stack cleared by new operation

After undo, executing any new mutating operation sets `CanRedo` to false and the previously undone work is gone.

Test: Add node A → Undo → Add node B → assert `CanRedo == false`.

### 5 — `CanUndo` / `CanRedo` state machine

| State | `CanUndo` | `CanRedo` |
|---|---|---|
| Fresh VM | false | false |
| After one operation | true | false |
| After undo | false | true |
| After redo | true | false |

One test covering all four transitions in sequence.

### 6 — `RestoreLayout`

- Nodes present in the dictionary take the specified positions.
- A node whose ID is absent from the dictionary keeps its original location.
- An ID in the dictionary that has no matching node in the VM is silently ignored (no exception).

### 7 — `IsModified` is not cleared by undo (intentional behavior)

`IsModified` is set to `true` by every mutating operation and to `false` only by `Load()`. Undo does not restore it to false — the VM has no "saved baseline" pointer. One test documents this as the intended contract so future refactors don't accidentally add false-clearing logic.

---

## What is NOT in scope

- Views / Converters — separate gap, separate plan.
- `NodeViewModel` property-change tests — already covered in `NodeViewModelTests.cs`.
- Multi-node selection — no multi-selection exists at the ViewModel layer; Nodify handles it in the view.
- `UndoRedoStack` itself — already covered in `UndoRedoStackTests.cs`.
