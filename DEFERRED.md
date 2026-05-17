# Deferred Work

Items that were explicitly deferred during development. Each entry records
*what* was deferred, *why*, and enough context to pick it up without
re-reading history.

---

## Editing — Node Properties

### Script editing
**What:** `OnEnterScripts`, `OnExitScripts`, `OnUpdateScripts` on dialogue nodes
are read-only. You can see existing scripts in the detail panel but cannot
add, remove, or edit them.  
**Why:** Scripts are structured calls with their own function catalogue (separate
from conditions), parameter types, and serialisation rules. Deferred as
"Phase 2" of node editing after conditions were complete.  
**Where to start:** `NodeViewModel.Scripts` is `IReadOnlyList<string>` (display
strings). The real data is in `ConversationNode.Scripts`. Both parsers already
read scripts into `Scripts`; serialisers write them back unchanged. The pattern
to follow is the condition editor (Phases 3–5 in the condition editor work).

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

### Condition editing on links
**What:** `NodeLink.Conditions` (conditions that gate when a link is followed) are
preserved through the round-trip but cannot be added, removed, or edited.  
**Why:** Link conditions were Phase 5b — the condition editor was built for node
conditions first. The data model (`NodeLink.Conditions`) and serialisers already
support them fully; only the UI and diff/patch layers need extending.  
**Where to start:** `ConnectionViewModel` would need a `Conditions` property
(similar to `QuestionNodeTextDisplay`). `LinkEditSnapshot` would need a
`Conditions` field. The condition editor window can likely be reused or adapted.

### QTD ComboBox has no "clear to default" option
**What:** Once `QuestionNodeTextDisplay` is set to `ShowOnce` / `Always` / `Never`,
there is no way to clear it back to the default empty string via the ComboBox.
The ComboBox shows nothing selected for `""` but you cannot *select* nothing.  
**Why:** Avalonia's XAML compiler cannot construct an empty `sys:String` element
in an inline `Items` list, so the empty option was removed.  
**Where to start:** Add a `"(default)"` display item that maps to `""` via a
value converter, or switch to `ItemsSource` bound to a list property that
includes an explicit blank entry.

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

### Condition catalogue completeness
**What:** The catalogue (`data/conditions.json`) covers 50 of 131+ PoE1 and
287+ PoE2 conditions — the dialogue-relevant ones. Uncatalogued conditions that
already exist in game files are preserved and displayed with their stripped
method name and no parameter type hints; they cannot be *added* from the picker.  
**Why:** The full set was not catalogued to keep the initial work manageable.
The research data (`data/conditions-poe1.md`, `data/conditions-poe2.md`) lists
all conditions; adding entries to `conditions.json` is the only work needed.  
**Where to start:** `data/conditions-poe1.md` / `conditions-poe2.md` are the
authoritative reference. Copy the method name, parameters, and types into
`conditions.json` following the existing format. The `fullName` must be the
exact C# reflection format: `Boolean MethodName(Type1, Type2, ...)`.

### PoE2 MapType for IsMapVisibility
**What:** `IsMapVisibility` takes a `MapType` enum with hundreds of area-specific
integer-keyed values. The parameter is currently a free-text `String` field
with a description pointing the user to the enum name.  
**Why:** The enum is too large for a ComboBox and the values are mostly internal
area identifiers. A searchable picker seeded from the enum would help.

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

### Speaker / Listener GUID lookup
**What:** `SpeakerGuid` and `ListenerGuid` are free-text fields. Users must
know the GUID for the character they want. There is no browse / autocomplete.  
**Why:** Speaker name resolution (`SpeakerNameService`) already exists for PoE2
(loads from `speakers.gamedatabundle`). PoE1 returns an empty dictionary.
A searchable picker from loaded speaker names would cover most use cases.  
**Where to start:** `SpeakerNameService.Register()` stores a `Dictionary<string,string>`
(GUID → name). Binding a ComboBox on the Speaker GUID field to the inverted
dictionary (name → GUID) with a search filter is the core of the feature.

### New conversation from scratch
**What:** The editor can only open and modify existing `.conversation` /
`.conversationbundle` files. There is no "New Conversation" action.  
**Why:** Creating a new conversation requires knowing valid file paths, game
metadata (speaker GUIDs for a root node), and the serialisation format for a
minimal valid file. Edge case for the primary modding use case.
