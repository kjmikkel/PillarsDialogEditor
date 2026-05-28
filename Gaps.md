# Dialog Editor — Known Gaps

## Structural (Code Quality)

### ViewModel Test Coverage
Significant coverage has been added: both `IGameDataProvider` implementations, `AutoLayoutService`, and several previously untested ViewModels (ConversationFolderViewModel, ConversationItemViewModel, PatchEntryViewModel, SettingsViewModel) now have tests. The remaining gaps are:

- Views / Converters (26 .cs files) — no unit tests, relies on manual verification

---

## Feature Gaps

### Version Control Integration
No built-in diff viewing or merge conflict resolution UI for Git. Collaborating on the same conversation across branches is a manual process.

### Import from Other Formats
Four one-shot import paths are implemented: CSV (spreadsheet-authored dialogue), a purpose-built JSON schema, Articy Draft 3/X XML, and Yarn Spinner `.yarn` files. Each produces a new conversation auto-laid-out on the canvas. Remaining limitations: no round-trip export back to these formats; Yarn Spinner conditions and commands (`<<if>>`, `<<set>>`, `<<command>>`) are silently skipped rather than imported.

### Barks System — Bark Preview
Bark nodes now render with an amber color scheme on the canvas, carry bark-specific validation warnings (text too long, player-choice child), and those warnings surface in Flow Analytics. The remaining gap is an in-context preview of overhead floating text: writers cannot see how a bark will actually appear above an NPC's head without running the game. Implementing this requires investigating the game's bark rendering (font, line-wrapping, maximum visible width) before UI work can be designed.

### Voice-Over Integration
An "External VO" field exists but there is no path validation, lip-sync metadata support, or audio preview. Mods that add or replace voiced lines have no tooling support. Note: actual voice-over audio is stored in a proprietary archive format — requires investigation before tooling can be designed.
