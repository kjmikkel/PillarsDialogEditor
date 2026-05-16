# Conversation Editing & Creation — Design Spec

**Date:** 2026-05-16  
**Scope:** Phase 1 — full structural editing of existing conversations; Phase 2 — creating new conversations and duplicating existing ones.  
**Game targets:** Pillars of Eternity I and Pillars of Eternity II: Deadfire (both formats).

---

## Background

The application is currently a read-only viewer. Domain models (`ConversationNode`, `NodeLink`, `StringEntry`, `Conversation`) are immutable C# `record` types. `IGameDataProvider` exposes only `LoadConversation`. There is no write-back path and no mutable edit layer.

---

## Architectural Approach: Viewmodel-as-Truth

`NodeViewModel` becomes the mutable source of truth for an open conversation. Observable property setters on `NodeViewModel` do not mutate state directly — each setter constructs an `IEditCommand` (carrying old and new value), pushes it onto an `UndoRedoStack` owned by `ConversationViewModel`, and the command's `Execute()` performs the assignment.

The immutable parse-model types (`ConversationNode`, etc.) are unchanged — they remain the output of parsing and the input for constructing viewmodels. The serializers read back from the viewmodel layer, not the parse-model layer.

---

## Phase 1: Editing Conversations

### 1. Undo / Redo Stack

**New type:** `UndoRedoStack` in `DialogEditor.Core` (or `DialogEditor.ViewModels`).

```
interface IEditCommand
    string Description   // shown in Undo/Redo button tooltips
    void Execute()
    void Undo()

class UndoRedoStack
    void Execute(IEditCommand cmd)   // calls cmd.Execute(), pushes to history, clears redo stack
    void Undo()
    void Redo()
    bool CanUndo
    bool CanRedo
    string? UndoDescription          // description of the top undo item
    string? RedoDescription          // description of the top redo item
```

`ConversationViewModel` holds one `UndoRedoStack`. The stack is cleared on `Load()`. `IsModified` is set to `true` on any `Execute()` call and reset to `false` on save or fresh load.

**Command types required (Phase 1):**
- `SetNodeTextCommand(NodeViewModel, string oldDefault, string newDefault, string oldFemale, string newFemale)`
- `SetNodePropertyCommand<T>(NodeViewModel, string propertyName, T oldValue, T newValue, Action<T> apply)`
- `AddNodeCommand(ConversationViewModel, NodeViewModel)`
- `DeleteNodeCommand(ConversationViewModel, NodeViewModel, IReadOnlyList<ConnectionViewModel> removedConnections)`
- `AddConnectionCommand(ConversationViewModel, ConnectionViewModel)`
- `DeleteConnectionCommand(ConversationViewModel, ConnectionViewModel)`

**Tests:** `UndoRedoStackTests` — execute, undo, redo, boundary conditions (undo with empty stack, redo after new command clears redo stack).

---

### 2. NodeViewModel — Mutable Properties

All editable fields gain `[ObservableProperty]` setters. The setter body:
1. Constructs the appropriate `IEditCommand` with the old and new value.
2. Calls `_undoStack.Execute(cmd)` (the stack calls `cmd.Execute()`, which does the actual assignment and raises `OnPropertyChanged`).

Editable properties:
- `DefaultText`, `FemaleText`
- `IsPlayerChoice`, `SpeakerCategory`, `SpeakerGuid`, `ListenerGuid`
- `DisplayType`, `Persistence`, `ActorDirection`
- `Comments`, `ExternalVO`, `HasVO`, `HideSpeaker`

Read-only for Phase 1 (structured fields, too complex to edit as raw text):
- `ConditionStrings`, `ConditionExpression`, `Scripts`

`NodeViewModel` receives a reference to the `UndoRedoStack` at construction time (passed from `ConversationViewModel.Load()`).

---

### 3. Serialization (Write-back)

#### IGameDataProvider

```csharp
void SaveConversation(ConversationFile file, ConversationViewModel conversation);
```

Both `Poe2GameDataProvider` and `Poe1GameDataProvider` implement this.

#### PoE2 Serializers

- **`Poe2ConversationSerializer`** — walks `ConversationViewModel.Nodes` and reconstructs the `.conversationbundle` JSON. Mirrors `Poe2ConversationParser` in reverse: rebuilds `$type`, `NodeID`, `SpeakerGuid`, `ListenerGuid`, `Links`, `Conditionals` (preserved verbatim from parse, since conditions are read-only in Phase 1), `OnEnterScripts` / `OnExitScripts` / `OnUpdateScripts` (likewise preserved), `DisplayType` int, `Persistence` int, `HideSpeaker`, `HasVO`, `ExternalVO`.
- **`Poe2StringTableSerializer`** — writes `DefaultText` / `FemaleText` pairs back to the `.stringtable` XML file, preserving all other entries not belonging to nodes in this conversation.

#### PoE1 Serializers

- **`Poe1ConversationSerializer`** + **`Poe1StringTableSerializer`** — same concept, format determined by `Poe1ConversationParser`. Implementation deferred until PoE1 parser is audited for round-trip fidelity.

#### Save Strategy

Before writing any file, the serializer renames the original to `<filename>.bak`, overwriting any existing `.bak`. The new file is then written. This gives a one-step single-file recovery point independent of the full backup system.

#### NodeIdAllocator

```csharp
static class NodeIdAllocator
    static int Next(IEnumerable<NodeViewModel> nodes)  // returns max(NodeIds) + 1
```

Stateless. Tested independently.

**Tests:** Round-trip tests for each serializer — parse a real or fixture file, mutate a field, serialize, re-parse, assert the mutated field is present and all other fields are unchanged.

---

### 4. Backup System

#### First-load backup offer

`AppSettings` gains a `KnownGameDirectories` set (persisted). When `LoadDirectory` sees a path not in this set, it shows a one-time prompt:

> *"This is the first time you have opened this game folder. Would you like to back up all conversation and string table files before editing? This backup can be used to restore the original files at any time."*

If the user accepts, an `IFolderPicker` dialog opens so they choose the backup root. The backup is written to `<chosen>/<timestamp>/` (e.g. `2026-05-16T14-32/`), preserving the internal directory structure of the conversations and string tables trees. The chosen path and timestamp are saved in `AppSettings` keyed by game directory.

The path is then added to `KnownGameDirectories` regardless of whether the user accepts or declines the backup, so the offer does not repeat.

#### Restore from backup

A *Restore from backup* command (File/Tools menu) reads the saved backup path from `AppSettings` for the current game directory. It shows a confirmation prompt — this is a destructive wholesale overwrite. On confirmation, it copies the backup tree back over the live files and reloads the current conversation.

**New service:** `BackupService` in `DialogEditor.Core`:

```
class BackupService
    Task BackupAsync(string gameConversationsRoot, string gameStringTablesRoot, string targetRoot, CancellationToken ct)
    Task RestoreAsync(string backupRoot, string gameConversationsRoot, string gameStringTablesRoot, CancellationToken ct)
```

Progress is reported via `IProgress<string>` (current file being copied), displayed in the status bar.

**Tests:** `BackupServiceTests` — backup copies all files with correct paths; restore overwrites originals; nested directories are preserved.

---

### 5. Structural Editing — Canvas Interactions

#### Adding a node

- Double-click on empty canvas space: creates a new node at the click position.
- New node: `NodeId` from `NodeIdAllocator`, `IsPlayerChoice = false`, `SpeakerCategory = Npc`, `SpeakerGuid` pre-filled to the most common speaker GUID in the current conversation (best-guess default), empty text. Node is immediately selected; detail panel opens.
- Right-click on an existing node → *Add connected node*: same as above, but also creates a link from the right-clicked node to the new node. New node is placed to the right of the parent.

#### Deleting a node

- `Delete` key on selected node, or right-click → *Delete node*.
- Command removes the node and all connections that reference it (as source or target). Undo restores all of them.

#### Adding a connection

- Drag from any node's output connector to another node's input connector.
- A preview line follows the cursor while dragging.
- Releasing on a valid input connector creates a `NodeLink`. Releasing on empty space cancels.

#### Deleting a connection

- Right-click on a connection line → *Delete connection*.
- Or: click a connection to select it, then press `Delete`.

#### Node right-click context menu

Every node responds to right-click with a context menu containing:
- **Delete node** — fires `DeleteNodeCommand`
- **Add connected node** — fires `AddNodeCommand` + `AddConnectionCommand`

---

### 6. Node Detail Panel — Editable Fields

The detail panel replaces display-only controls with editable equivalents where appropriate.

| Field | Control |
|---|---|
| `DefaultText` | Multi-line `TextBox` (commits on focus-loss) |
| `FemaleText` | Multi-line `TextBox` (commits on focus-loss) |
| `IsPlayerChoice` | `ComboBox` — NPC Line / Player Choice |
| `DisplayType` | `ComboBox` — Conversation / Bark |
| `Persistence` | `ComboBox` — None / OnceEver |
| `SpeakerGuid` | `TextBox` |
| `ListenerGuid` | `TextBox` |
| `ActorDirection` | `TextBox` |
| `Comments` | `TextBox` |
| `ExternalVO` | `TextBox` |
| `HasVO` | `CheckBox` |
| `HideSpeaker` | `CheckBox` |
| `ConditionExpression` | Read-only `TextBlock` (Phase 1) |
| `Scripts` | Read-only `TextBlock` (Phase 1) |

**Links section:** Each link row gains a **Delete** button. An **Add link** button opens a small inline picker: type a node ID or search by text, then confirm to add the link. This fires `AddConnectionCommand`.

All field changes are committed as edit commands and are fully undoable.

---

### 7. Toolbar and Status

**New toolbar items:**
- **Undo** (`Ctrl+Z`) — disabled when `CanUndo` is false; tooltip: *"Undo: \<UndoDescription\>"*
- **Redo** (`Ctrl+Y`) — disabled when `CanRedo` is false; tooltip: *"Redo: \<RedoDescription\>"*
- **Save** (`Ctrl+S`) — disabled when `IsModified` is false

**Dirty indicator:** Window title shows `● <ConversationName>` when `IsModified` is true.

**Unsaved-changes guard:** Switching conversations in the browser while `IsModified` is true shows a prompt: *Save / Discard / Cancel*.

---

### 8. Localisation

All new user-visible strings — button labels, tooltips, context menu items, dialog titles, status messages, undo/redo description templates, backup prompts, restore confirmation text — are defined in `Strings.axaml` before use. No hard-coded strings in XAML or C#.

---

## Phase 2: Creating New Conversations

### 1. New Conversation

A *New Conversation* command (toolbar / File menu) opens a small dialog:
- **Name** — filename for the new `.conversationbundle`
- **Destination folder** — subfolder within the game's conversations directory, chosen via folder picker

The editor creates:
- A minimal valid `.conversationbundle` with one root NPC node (NodeId = 1)
- A matching empty `.stringtable` with one blank entry for node 1

The new `ConversationFile` is added to `GameBrowserViewModel`'s collection immediately (no full rescan). The conversation is loaded into the canvas and `IsModified` is set to `true` (the file does not yet exist on disk until the user saves).

### 2. Duplicate Conversation

A *Duplicate* command (right-click on a conversation in the browser, or toolbar) opens a name/path dialog identical to New Conversation. It deep-copies:
- All nodes with their existing IDs and properties
- All string entries under new IDs (allocated via `StringIdAllocator`)

Node link IDs are preserved. The duplicate is treated as a new unsaved file (same `IsModified = true` pattern as New Conversation).

### 3. String ID Allocation for New Conversations

**`StringIdAllocator`** in `DialogEditor.Core`:

```
class StringIdAllocator
    StringIdAllocator(string stringTablesRoot)   // scans all .stringtable files on construction
    int Next()                                    // returns max(seen IDs) + 1, increments internal counter
```

Scanned once per session (or per `LoadDirectory`). New IDs are guaranteed not to collide with any existing string entry across the whole game directory.

**Tests:** `StringIdAllocatorTests` — correct max detection, sequential allocation, empty directory returns a safe starting ID.

---

## Test Coverage Summary

All logic classes require a failing test before implementation (TDD, per `CLAUDE.md`):

| Area | Test class |
|---|---|
| Undo/redo stack | `UndoRedoStackTests` |
| NodeIdAllocator | `NodeIdAllocatorTests` |
| StringIdAllocator | `StringIdAllocatorTests` |
| BackupService | `BackupServiceTests` |
| Poe2ConversationSerializer | `Poe2ConversationSerializerTests` |
| Poe2StringTableSerializer | `Poe2StringTableSerializerTests` |
| Poe1ConversationSerializer | `Poe1ConversationSerializerTests` |
| Poe1StringTableSerializer | `Poe1StringTableSerializerTests` |
| ConversationViewModel (edit ops) | `ConversationViewModelEditTests` |
| NodeDetailViewModel (edit binding) | `NodeDetailViewModelEditTests` |

---

## Out of Scope (Phase 1 & 2)

- Editing `ConditionStrings`, `ConditionExpression`, or `Scripts` — these require a dedicated condition/script editor and are deferred.
- Multi-conversation undo (undo stack is per-conversation, cleared on load).
- Conflict resolution when saving a file that has been modified on disk by another tool since it was loaded.
- Versioned backup history beyond the timestamped folder per session.
