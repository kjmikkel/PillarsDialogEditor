# Dialog Editor — Known Gaps

## Structural (Code Quality)

### ViewModel Test Coverage
Significant coverage has been added: both `IGameDataProvider` implementations, `AutoLayoutService`, and several previously untested ViewModels (ConversationFolderViewModel, ConversationItemViewModel, PatchEntryViewModel, SettingsViewModel) now have tests. The remaining gaps are:

- Views / Converters — mostly covered: all 14 converters have unit tests; `LanguageCodeDialog`, `LegendWindow`, `UnsavedChangesDialog`, `ConflictResolutionDialog`, and `ConversationNameDialog` have headless Avalonia integration tests. Remaining untested views (`MainWindow` wiring, `NodeDetailView` modal launchers, and the Nodify canvas controls) contain no testable logic beyond what is already covered at the ViewModel layer.

---

## Feature Gaps

### Version Control Integration
Git **merge-conflict resolution** for `.dialogproject` files is now implemented: opening a conflicted file reconstructs the mine/theirs sides, presents a dedicated resolution window (field-level merge for same-node edits, **per-language text/translation conflicts**, binary keep-mine/keep-theirs for structural conflicts, **a whole-conversation fallback when a conversation diverges beyond per-node merge**, word-level inline highlighting of text changes), and loads the merged result as an unsaved project. Canvas **layout positions** and the **new-conversation list** are auto-merged (layouts union per node with theirs winning on overlap; new-conversation lists are unioned).

Conflict-resolution coverage is essentially complete. Minor known limitations:

- A translation conflict where only `FemaleText` differs (same `DefaultText`) is detected and resolvable, but the resolution dialog shows the `FemaleText` rather than labelling it as the female-variant — a cosmetic display limitation.
- The whole-conversation (`ConversationLevel`) fallback is deliberately eager: any theirs-side content the per-node merge can't preserve (e.g. a translation in a language only theirs has) collapses that conversation to a single keep-mine/keep-theirs choice, trading per-node granularity for guaranteed no-silent-loss.

**Diff viewing** (implemented): selecting two endpoints (working copy, git branch, or recent commit) lists changed conversations with +/~/− counts; selecting a conversation renders it on a read-only canvas with per-node colour tinting (green = added, amber = changed, red = removed). Ghost nodes for removed nodes are injected from the left project's reconstruction. Before/after text detail for a selected node is not yet implemented (deferred — the canvas tinting is the priority).

**Remaining VCS gaps**: **selective apply** (applying individual conversation patches from one branch to another) and **branch/history navigation** (browsing git log, switching branches, attribution) are not started.

> **Deferred idea (revisit) — first-run intro for the compare/apply window.** A one-time, dismissible explanatory panel shown the first time a writer opens the compare window, to orient non-technical users. Deferred from the selective-apply design (`docs/superpowers/specs/2026-05-30-selective-apply-design.md`) because it needs persisted "seen" state; the always-on in-context cues (colour-key strip, one-line hint, plain-language tooltips, Help window) cover the immediate need. Worth reconsidering after selective apply ships.

### Barks System — Bark Preview
Bark nodes now render with an amber color scheme on the canvas, carry bark-specific validation warnings (text too long, player-choice child), and those warnings surface in Flow Analytics. The remaining gap is an in-context preview of overhead floating text: writers cannot see how a bark will actually appear above an NPC's head without running the game. Implementing this requires investigating the game's bark rendering (font, line-wrapping, maximum visible width) before UI work can be designed.

### Voice-Over Integration
An "External VO" field exists but there is no path validation, lip-sync metadata support, or audio preview. Mods that add or replace voiced lines have no tooling support. Note: actual voice-over audio is stored in a proprietary archive format — requires investigation before tooling can be designed.
