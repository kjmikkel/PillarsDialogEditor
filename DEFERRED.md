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

## Patch / Workflow

### Patch stacking conflict resolution UI
**What:** `PatchApplier.ApplyAll` throws `PatchConflictException` when two
patches in a project modify the same field with mismatched `From` values.
There is currently no UI to surface or resolve conflicts — the error is caught
and shown in the status bar.  
**Why:** Conflict resolution UI is a significant UX effort and the initial
use case (one author, one project) doesn't need it.  
**Where to start:** `PatchConflictException` (field, node ID, expected vs actual),
`MainWindowViewModel.TestPatch` catch block.
