# Dialog Editor — Known Gaps

## Structural (Code Quality)

### ViewModel Test Coverage
Significant coverage has been added: both `IGameDataProvider` implementations, `AutoLayoutService`, and several previously untested ViewModels (ConversationFolderViewModel, ConversationItemViewModel, PatchEntryViewModel, SettingsViewModel) now have tests. The remaining gaps are:

- Complex editing operations — multi-node selection, bulk edits, undo on branching scenarios — are untested and fragile to regression
- Views / Converters (26 .cs files) — no unit tests, relies on manual verification

---

## Feature Gaps

### Writer Comments (Localization)
`NodeComments` entries are stored in the patch file and included in CSV/JSON/XLIFF exports as translator context, but there is no UI to write them. Authors must edit the `.dialogproject` JSON directly to populate the `nodeComments` map.

### Version Control Integration
No built-in diff viewing or merge conflict resolution UI for Git. Collaborating on the same conversation across branches is a manual process.

### Import from Other Formats
Only game XML is supported as a source. No import path from dialogue authoring tools (Articy Draft, etc.) or other intermediate formats.

### Barks System
Display Type toggle exists but no bark-specific tooling — no preview of overhead floating text in context, no bark-specific validation.

### Voice-Over Integration
An "External VO" field exists but there is no path validation, lip-sync metadata support, or audio preview. Mods that add or replace voiced lines have no tooling support. Note: actual voice-over audio is stored in a proprietary archive format — requires investigation before tooling can be designed.
