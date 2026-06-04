# Dialog Editor — Known Gaps

## Structural (Code Quality)

### ViewModel Test Coverage
Significant coverage has been added: both `IGameDataProvider` implementations, `AutoLayoutService`, and several previously untested ViewModels (ConversationFolderViewModel, ConversationItemViewModel, PatchEntryViewModel, SettingsViewModel) now have tests. The remaining gaps are:

- Views / Converters — mostly covered: all 14 converters have unit tests; `LanguageCodeDialog`, `LegendWindow`, `UnsavedChangesDialog`, `ConflictResolutionDialog`, and `ConversationNameDialog` have headless Avalonia integration tests. Remaining untested views (`MainWindow` wiring, `NodeDetailView` modal launchers, and the Nodify canvas controls) contain no testable logic beyond what is already covered at the ViewModel layer.

---

## Feature Gaps

### Version Control Integration
Git **merge-conflict resolution** for `.dialogproject` files is now implemented: opening a conflicted file reconstructs the mine/theirs sides, presents a dedicated resolution window (field-level merge for same-node edits, **per-language text/translation conflicts**, binary keep-mine/keep-theirs for structural conflicts, **a whole-conversation fallback when a conversation diverges beyond per-node merge**, word-level inline highlighting of text changes), and loads the merged result as an unsaved project. Canvas **layout positions** and the **new-conversation list** are auto-merged (layouts union per node with theirs winning on overlap; new-conversation lists are unioned).

Conflict-resolution coverage is essentially complete. A translation conflict shows the **Default and Female text variants as separate labelled rows** when the node carries female text — so a female-only difference (identical `DefaultText`) is clearly labelled, and a conflict where both variants differ shows both changes rather than hiding the female one. Minor known limitations:

- The whole-conversation (`ConversationLevel`) fallback is deliberately eager: any theirs-side content the per-node merge can't preserve (e.g. a translation in a language only theirs has) collapses that conversation to a single keep-mine/keep-theirs choice, trading per-node granularity for guaranteed no-silent-loss.

**Diff viewing** (implemented): selecting two endpoints (working copy, git branch, or recent commit) lists changed conversations with +/~/− counts; selecting a conversation renders it on a read-only canvas with per-node colour tinting (green = added, amber = changed, red = removed). Ghost nodes for removed nodes are injected from the left project's reconstruction. Before/after text detail for a selected node is implemented: selecting a node shows its Default/Female text before and after, with inline highlighting of the changed text (reusing `TextDiff`). Hidden in Applied-Preview mode; a node whose only differences are structural shows a "structural only" hint instead of identical text. Multi-language before/after is implemented: the detail panel shows the node's text for the primary language plus every language whose text changed, as stacked sections (primary first, then alphabetical) with friendly language names and inline highlighting. Languages with no change (other than the primary anchor) are omitted. Known limitation (inherited from the game-data-light reconstruction): within a non-primary language section, a side that has no translation for the node falls back to the **primary-language base text** (the effective in-game fallback) rather than blanking — so a language translated on only one side can show primary-language text under a non-primary header. Fixing it would require sourcing base text per language, which is deferred to keep the diff list game-folder-free.

Known, **intentional** diff limitations (a whole-feature review on 2026-05-31 confirmed these are by-design, documented in `ProjectDiff` remarks):
- The diff is **patch-relative**, not effective-conversation-relative. A node that one version's patch modifies and the other leaves at the game-base value is reported **Removed** ("reverted to base"), even though it still exists. A fully effective diff would need to reconstruct both conversations against the game base, coupling the (currently game-folder-free) list to game data — deferred.
- **Comment-only** node changes are not surfaced (NodeComments are excluded from the diff signature), consistent with selective apply treating comments as outside the apply unit.
- Endpoint-load errors are now localized and name the failing version; the working-copy read is guarded against IO/permission failures (was an unhandled-exception crash path). A corrupt or hand-edited project file (readable bytes, invalid contents) is reported distinctly via `DiffExceptionKind.ParseFailed` → "the file looks damaged or isn't a valid project file", rather than being conflated with the locked/unreadable IO error.

**Selective apply** (implemented): from the compare window the user ticks individual changed lines and **brings them into their copy** (per-node cherry-pick into the working-copy `.dialogproject`). Your copy is the left endpoint (the bring-in target); the other version is on the right, so the tree colours match the bring-in effect. Includes a live "applied preview", a save-before-apply guard, a single-step undo, a count-only dangling-link warning (warn-but-allow), and a plain-language Help window. The dangling-link warning is now a collapsible panel above the apply bar listing each dangling link (conversation, source node, removed target); read-only, collapsed by default, hidden when there are none. Automatic dependency-pulling is implemented: ticking a change auto-ticks the added nodes it links to (transitively), preventing brought-in links that point at nodes never created. It is outgoing-only and has a default-on "Also bring in linked nodes" toggle.

**Remaining VCS gaps**: **branch/history navigation** (browsing git log, switching branches, attribution) is not started.

> **Deferred idea (revisit) — first-run intro for the compare/apply window.** A one-time, dismissible explanatory panel shown the first time a writer opens the compare window, to orient non-technical users. Deferred from the selective-apply design (`docs/superpowers/specs/2026-05-30-selective-apply-design.md`) because it needs persisted "seen" state; the always-on in-context cues (colour-key strip, one-line hint, plain-language tooltips, Help window) cover the immediate need. Worth reconsidering after selective apply ships.

### Barks System — Bark Preview
Bark nodes now render with an amber color scheme on the canvas, carry bark-specific validation warnings (text too long, player-choice child), and those warnings surface in Flow Analytics. The remaining gap is an in-context preview of overhead floating text: writers cannot see how a bark will actually appear above an NPC's head without running the game. Implementing this requires investigating the game's bark rendering (font, line-wrapping, maximum visible width) before UI work can be designed.

### Voice-Over Integration
An "External VO" field exists but there is no path validation, lip-sync metadata support, or audio preview. Mods that add or replace voiced lines have no tooling support. Note: actual voice-over audio is stored in a proprietary archive format — requires investigation before tooling can be designed.
