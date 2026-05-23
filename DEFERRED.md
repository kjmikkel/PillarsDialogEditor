# Deferred Work

Items that were explicitly deferred during development. Each entry records
*what* was deferred, *why*, and enough context to pick it up without
re-reading history.

---

## Editing — Node Properties

### SpeakerCategory for new nodes
**What:** Newly created nodes can only be "NPC Line" or "Player Choice".
Narrator and Script node types cannot be created from the editor.  
**Why:** The `NodeTypeString` ComboBox maps to `IsPlayerChoice` (bool). Narrator
and Script require specific GUID values and a `xsi:type` that the serialiser
would need to set. Low priority for dialogue writing use cases.  
**Where to start:** `NodeDetailViewModel.NodeTypeString`, `CharacterStats.SpeakerCategory`
enum (`Narrator`, `Script` values), `Poe1ConversationParser.ClassifySpeaker`.

---

## Editing — Links

---

## Condition Editor

### ConditionBranch editing
**What:** Grouped / nested conditions (`ConditionBranch`) appear in the condition
editor as read-only rows with a ⚙ indicator. You can move or delete them but
cannot edit their internal logic (e.g. change `(A AND B) OR C` to `(A OR B) AND C`).  
**Why:** Full branch editing requires a recursive sub-editor UI and was
explicitly deferred to a future phase. The data model (`ConditionBranch`),
parsers, and serialisers all support it fully.  
**Where to start:** `ConditionRowViewModel.IsBranch`, `ConditionEditorWindow.axaml`
branch row section. A nested `ConditionEditorWindow` opened from a branch row's
"Edit group…" button is the most natural approach.

---

## Patch / Workflow

### Standalone patch apply utility
**What:** The `DialogEditor.Patch` project was designed from the start to be
reusable without the full editor. A standalone CLI / GUI tool that lets
players/modders apply a `.dialogproject` file to their game without installing
the dialog editor has not been built.  
**Why:** Deferred until the core editor was stable.  
**Where to start:** `DialogEditor.Patch` + `DialogEditor.Core` are the only
dependencies. `PatchApplier.ApplyAll`, `ConversationSnapshotBuilder.Build`,
and both serialisers are all that's needed. A simple CLI that takes
`<game-dir> <project-file>` would cover the basic case.

### Patch stacking conflict resolution UI
**What:** `PatchApplier.ApplyAll` throws `PatchConflictException` when two
patches in a project modify the same field with mismatched `From` values.
There is currently no UI to surface or resolve conflicts — the error is caught
and shown in the status bar.  
**Why:** Conflict resolution UI is a significant UX effort and the initial
use case (one author, one project) doesn't need it.  
**Where to start:** `PatchConflictException` (field, node ID, expected vs actual),
`MainWindowViewModel.TestPatch` catch block.

---

## UI / UX

### New conversation from scratch
**What:** The editor can only open and modify existing `.conversation` /
`.conversationbundle` files. There is no "New Conversation" action.  
**Why:** Creating a new conversation requires knowing valid file paths, game
metadata (speaker GUIDs for a root node), and the serialisation format for a
minimal valid file. Edge case for the primary modding use case.
