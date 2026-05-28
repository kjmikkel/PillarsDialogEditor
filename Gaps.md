# Dialog Editor — Known Gaps

## Structural (Code Quality)

### ViewModel Test Coverage
Significant coverage has been added: both `IGameDataProvider` implementations, `AutoLayoutService`, and several previously untested ViewModels (ConversationFolderViewModel, ConversationItemViewModel, PatchEntryViewModel, SettingsViewModel) now have tests. The remaining gaps are:

- Views / Converters — mostly covered: all 14 converters have unit tests; `LanguageCodeDialog`, `LegendWindow`, `UnsavedChangesDialog`, `ConflictResolutionDialog`, and `ConversationNameDialog` have headless Avalonia integration tests. Remaining untested views (`MainWindow` wiring, `NodeDetailView` modal launchers, and the Nodify canvas controls) contain no testable logic beyond what is already covered at the ViewModel layer.

---

## Feature Gaps

### Version Control Integration
No built-in diff viewing or merge conflict resolution UI for Git. Collaborating on the same conversation across branches is a manual process.

### Yarn Spinner Import — Inline Conditional Choices
Line-level Yarn constructs (`<<if>>`, `<<set>>`, `<<command>>`) are now skipped *with a warning* on import. But a conditional written inline on a choice — `-> Choice text <<if $x>>` — is parsed as a plain choice and the trailing `<<if>>` is dropped silently, because the skipped-construct tally only matches lines that *start* with `<<`. This is the same silent-drop failure mode the warning feature addresses, just at a position the tally doesn't reach. Closing it means extending the importer to detect and strip trailing inline constructs from `->` lines and fold them into the warning tally.

### Barks System — Bark Preview
Bark nodes now render with an amber color scheme on the canvas, carry bark-specific validation warnings (text too long, player-choice child), and those warnings surface in Flow Analytics. The remaining gap is an in-context preview of overhead floating text: writers cannot see how a bark will actually appear above an NPC's head without running the game. Implementing this requires investigating the game's bark rendering (font, line-wrapping, maximum visible width) before UI work can be designed.

### Voice-Over Integration
An "External VO" field exists but there is no path validation, lip-sync metadata support, or audio preview. Mods that add or replace voiced lines have no tooling support. Note: actual voice-over audio is stored in a proprietary archive format — requires investigation before tooling can be designed.
