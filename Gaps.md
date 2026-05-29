# Dialog Editor — Known Gaps

## Structural (Code Quality)

### ViewModel Test Coverage
Significant coverage has been added: both `IGameDataProvider` implementations, `AutoLayoutService`, and several previously untested ViewModels (ConversationFolderViewModel, ConversationItemViewModel, PatchEntryViewModel, SettingsViewModel) now have tests. The remaining gaps are:

- Views / Converters — mostly covered: all 14 converters have unit tests; `LanguageCodeDialog`, `LegendWindow`, `UnsavedChangesDialog`, `ConflictResolutionDialog`, and `ConversationNameDialog` have headless Avalonia integration tests. Remaining untested views (`MainWindow` wiring, `NodeDetailView` modal launchers, and the Nodify canvas controls) contain no testable logic beyond what is already covered at the ViewModel layer.

---

## Feature Gaps

### Version Control Integration
Git **merge-conflict resolution** for `.dialogproject` files is now implemented: opening a conflicted file reconstructs the mine/theirs sides, presents a dedicated resolution window (field-level merge for same-node edits, binary keep-mine/keep-theirs for structural conflicts, word-level inline highlighting of text changes), and loads the merged result as an unsaved project.

Remaining gaps, in two groups:

**Conflict-resolution coverage** (`GitMergeAnalyzer` / `MergeBuilder` in `DialogEditor.Patch/GitConflict/`):

- **Translation conflicts are not analyzed.** Text edits on *modified* nodes are covered (they appear as `FieldChanges`, so they surface as `FieldEdit` conflicts), but text carried in a patch's `Translations` is not diffed. In particular, `NodeEditSnapshot.DefaultText`/`FemaleText` are `[JsonIgnore]`, so the `NodeAddAdd` comparison is structural only — two added nodes that differ *only* in their text would not be flagged as a conflict, and a node whose translation differs between sides is not detected at all.
- **`Layouts` and `NewConversations` divergence is not merged.** `MergeBuilder` starts from "mine" and unions only conversation *patches* present solely on the "theirs" side; canvas layout positions and the new-conversation list fall back to "mine" wholesale. A layout or new-conversation difference that lived inside a git conflict hunk is therefore silently resolved in mine's favour.
- **`ConversationLevel` conflicts are never produced.** `MergeBuilder` handles the `ConversationLevel` kind defensively, but `GitMergeAnalyzer` does not yet emit it; any divergence not reducible to field-edit / delete-vs-edit / add-add is currently not represented as its own conflict.

**Broader VCS features** (not started): **diff viewing** between arbitrary commits/branches (showing what changed in a conversation, rendered in the canvas rather than raw JSON) and **branch/history navigation** (browsing git log, switching branches, attribution).

### Barks System — Bark Preview
Bark nodes now render with an amber color scheme on the canvas, carry bark-specific validation warnings (text too long, player-choice child), and those warnings surface in Flow Analytics. The remaining gap is an in-context preview of overhead floating text: writers cannot see how a bark will actually appear above an NPC's head without running the game. Implementing this requires investigating the game's bark rendering (font, line-wrapping, maximum visible width) before UI work can be designed.

### Voice-Over Integration
An "External VO" field exists but there is no path validation, lip-sync metadata support, or audio preview. Mods that add or replace voiced lines have no tooling support. Note: actual voice-over audio is stored in a proprietary archive format — requires investigation before tooling can be designed.
