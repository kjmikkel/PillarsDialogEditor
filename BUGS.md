# Dialog Editor — Bug Tracker (pre-launch, internal)

> **Temporary file — delete before the initial public release.** This is a lightweight,
> local bug list for solo development. When the project goes public, bug tracking moves to
> GitHub Issues and this file is removed (see the **Bug Tracker** rule in `CLAUDE.md`).

Newest first. When a bug is fixed, **move** its entry to the **Fixed** section with the
fixing commit hash rather than deleting it — the record of what broke and how it was fixed
stays useful until launch.

## How to log a bug

Copy the template into **Open**. A partial entry is fine — *Repro* + *Actual* is enough to
start. IDs are a simple running counter (`B-001`, `B-002`, …) so commits can reference them
("fix B-003: …").

```
### B-NNN — <one-line summary>
- **Area:** <e.g. Diff viewer, Branches, Changelog reader>
- **Severity:** blocker | major | minor | cosmetic
- **Repro:**
  1. <step>
  2. <step>
- **Expected:** <what should happen>
- **Actual:** <what happens — include any error text or AppLog output>
- **Notes:** <hypotheses, suspect files, related entries>
```

When fixed, append to the moved entry:
```
- **Fixed:** <commit hash> — <one-line explanation of the fix + the test that now guards it>
```

---

## Open

_None yet._

---

## Fixed

### B-006 — Validate Voice-Over reports imported VO as missing
- **Area:** Test › Validate Voice-Over — `VoValidationViewModel`
- **Severity:** minor (false positive; no data loss)
- **Repro:**
  1. Import a VO for a node (file lands in the project's `_vo/` folder), F5, F6.
  2. Run **Test › Validate Voice-Over**.
- **Expected:** The node's VO counts as present — it plays in the editor and is re-synced to the game on every F5.
- **Actual:** Reported missing. The scan checked only the game's Voices folder, and F6 removes the synced copy; the detail pane's `_vo/` fallback was never applied in the scan.
- **Fixed:** `32efaee` — extracted the fallback into `VoPathResolver.WithLocalVoFallback` and applied it in both the detail pane and the validation scan (`projectPath` now passed to `VoValidationViewModel`). Guarded by `RunAsync_FileOnlyInProjectVoFolder_NotReportedMissing`.

### B-005 — Added node is invisible and a dead end in-game (PoE2)
- **Area:** Test Patch (F5/F6) — `MainWindowViewModel.DoTestPatch`; `Poe1ConversationSerializer` / `Poe2ConversationSerializer`
- **Severity:** blocker
- **Repro:**
  1. Add a node to an existing PoE2 conversation, give it text, link it onward to another node.
  2. Save, press `F5`, trigger the node in-game.
- **Expected:** The node shows its text and continues to the linked node.
- **Actual:** No text (its stringtable entry was never written — F5 wrote only the conversation bundle; `TranslationApplier` was called by the dialog-patcher CLI but not by the editor), and the conversation dead-ends (both serializers emitted brand-new nodes with an empty `Links` element, dropping outgoing connections — the incoming link survived because it lives on an existing, modified node).
- **Notes:** Verified against the real project (`MyMod.dialogproject`, node 107 in `21_si_pallid_knight`): the patch JSON contained both the link (107→25) and the text; only the F5 write path lost them.
- **Fixed:** `21397b3` — `DoTestPatch` now calls `TranslationApplier.WriteTranslations` for every installed language in the patch, backs up each stringtable per language (empty-backup sentinel ⇒ F6 deletes stringtables the patch created), and both serializers build `Links` from the snapshot for new nodes. Guarded by `MainWindowViewModelTestPatchTests` and `Serialize_AddsNewNode_KeepsItsOutgoingLinks` in both serializer test suites.

### B-004 — Conversation edits lost across save/load cycles (four related defects)
- **Area:** Project persistence — `MainWindowViewModel` save/load, `ConversationViewModel` dirty tracking
- **Severity:** blocker (silent data loss)
- **Repro:**
  1. Open a project, open a conversation, edit only node text/fields in the detail pane → `Ctrl+S` stays disabled ("unable to save").
  2. Or: save an edit, reopen the project, click the conversation → canvas shows vanilla game content ("edits not restored").
  3. Then make any new edit and save → the previously saved edits are silently erased from the `.dialogproject` file.
- **Expected:** Detail-pane edits enable Save Project; reopening a patched conversation shows the saved edits; saving never discards earlier sessions' work.
- **Actual:** Four root causes: (1) detail-pane field edits never set `Canvas.IsModified`, so `CanSaveProject()` stayed false; (2) `LoadConversationFile` never applied the stored patch — canvas showed vanilla; (3) with a vanilla canvas, the next save re-diffed vanilla→current and `WithPatch` replaced the stored patch, erasing prior edits (same class: `LoadNewConversation` used the patched state as diff baseline, dropping created nodes on re-save); (4) `SaveProject` rebuilt `Translations` for the canvas language only, erasing imported translations for other languages.
- **Notes:** Core invariant: stored patches are always *vanilla → edited* (F5 applies them against vanilla), so the canvas may display the patched state but `BaseSnapshot` must stay vanilla/empty.
- **Fixed:** `7c0f3de` — `UndoRedoStack.CommandExecuted` now dirty-flags the canvas; `Canvas.Load` gained an explicit-baseline overload; `LoadConversationFile` applies the stored patch (force-apply + `Status_PatchBaselineMismatch` warning if the game files changed); `LoadNewConversation` diffs against the empty snapshot; `SaveProject` carries over other-language translations. Guarded by `MainWindowViewModelPersistenceTests` and new `ConversationViewModelEditTests` cases.

### B-003 — Default localization format in Settings doesn't persist/display
- **Area:** Settings window — "Default localization format" picker (`SettingsWindow.axaml`)
- **Severity:** minor
- **Repro:**
  1. Open Settings, change "Default localization format" to Json or Xliff.
  2. Close Settings, reopen it.
- **Expected:** The picker shows the previously selected format.
- **Actual:** The picker always appears blank/unselected, and the choice is never saved
  (`AppSettings.DefaultLocalizationFormat` stays "Csv" regardless of selection).
- **Notes:** The ComboBox bound `SelectedItem="{Binding LocalizationFormat, Mode=TwoWay}"`
  (a `string` VM property) against static `<ComboBoxItem Content="Csv"/>` etc. children —
  the `Items` collection held `ComboBoxItem` objects, not strings, so the binding's type
  mismatch broke it in both directions. The sibling `FontScale` picker uses the correct
  `ItemsSource`/`SelectedItem` pattern with matching types.
- **Fixed:** `576d109` — added `LocalizationFormatOptions` (`IReadOnlyList<string>`)
  to `SettingsViewModel` mirroring `FontScaleOptions`, and changed the XAML to
  `ItemsSource="{Binding LocalizationFormatOptions}"` + `SelectedItem="{Binding
  LocalizationFormat, Mode=TwoWay}"` (both `string`-typed), also adding a tooltip and
  `AutomationProperties.HelpText`. Covered by new headless tests in
  `DialogEditor.Tests/Views/SettingsWindowTests.cs`.

### B-002 — No visible in-progress line when making a node connection
- **Area:** Conversation canvas — connection-making (`ConversationView.axaml`, `PendingConnectionViewModel`)
- **Severity:** minor
- **Repro:**
  1. Open a conversation on the canvas (editable).
  2. Press-drag from a node's "out" connector toward another node's "in" connector.
  3. Watch the area between the source connector and the cursor before completing.
- **Expected:** A clearly visible preview line follows the cursor from the source connector
  until the connection is completed (or cancelled), so it's obvious a connection is in
  progress.
- **Actual:** Nothing visible — no preview line at all, including when the cursor passes over
  other nodes (so it's not merely blending into the `#7a6a8e` canvas background). The
  connection only appears once both connectors have been clicked.
- **Notes:** A `nodify:PendingConnection` *was* configured in `ConversationView.axaml`, and
  connections *did* get created, so `Start`/`Complete` both fired — only the in-progress
  **visual** was missing. The two hypotheses originally logged (gesture-model; rendering/
  z-order/anchor) were **both wrong**.
- **Root cause (confirmed via Nodify source):** The `<nodify:PendingConnection>` *control*
  was assigned directly to `NodifyEditor.PendingConnection`, which is a `StyledProperty<object>`
  (a *DataContext* slot), with **no** `PendingConnectionTemplate`. Nodify's `PendingConnection`
  locates its owner editor in `OnApplyTemplate` via `GetParentOfType<NodifyEditor>()` and only
  then subscribes to the `PendingConnectionStarted/Drag/Completed` events that set
  `SourceAnchor`/`TargetAnchor`. Not hosted through the template, the control never attached to
  the editor, so its anchors never updated during a drag and the line had no coordinates to
  draw. The `StartedCommand`/`CompletedCommand` bindings still resolved against the inherited
  `ConversationViewModel` DataContext, so connections were created regardless — exactly the
  observed symptoms.
- **Fixed:** `987873b` — switched to the documented Nodify pattern: bind
  `PendingConnection="{Binding PendingConnection}"` to the VM and move the
  `<nodify:PendingConnection>` control into `NodifyEditor.PendingConnectionTemplate` (a
  `DataTemplate` for `PendingConnectionViewModel`, with `StartCommand`/`CompleteCommand` bound
  directly). Stroke styling unchanged. View-layer XAML wiring with no Core/VM logic change
  (the VM is already covered by `PendingConnectionViewModelTests`); verified visually by the
  user — the dashed preview line now follows the cursor during a drag. If the `#aaaaaa` stroke
  proves too faint against the canvas, a contrast bump is a separate follow-up.

### B-001 — Grouped Condition Block has no Edit button and text overflows the window
- **Area:** Condition Editor — branch/group row (`ConditionEditorWindow.axaml`)
- **Severity:** major
- **Repro:**
  1. Open the Condition Editor on a node/link.
  2. Add a group ("+ New group") so a branch (grouped) row renders.
  3. Observe the branch row, especially with a long group summary.
- **Expected:** The branch row shows a short, ellipsis-truncated group summary, with the
  Edit button clustered hard-right alongside the ↑ ↓ ✕ buttons — all always visible. Full
  group text available on hover via tooltip.
- **Actual:** The group text runs off the right edge of the window and the Edit button is
  pushed off-screen with it, so the group cannot be edited.
- **Notes:** Root cause confirmed — the branch row (`NodeDetailView`-style ellipsis is fine
  on leaves) wraps ⚙ icon + name + Edit button in a **horizontal `StackPanel`**
  (`ConditionEditorWindow.axaml` lines ~102–119, `IsVisible="{Binding IsBranch}"`). A
  horizontal StackPanel measures children with infinite width, so the `TextTrimming=
  "CharacterEllipsis"` on the name never fires; the text grows unbounded and shoves the Edit
  button + the ↑ ↓ ✕ buttons (grid columns 3–5) past the window edge. The missing Edit
  button is a symptom of the overflow, not a separate defect.
- **Fix (confirmed layout):** Replace the inner horizontal StackPanel with a width-bounded
  layout (Grid/DockPanel) so the name truncates inside the `*` middle column. Dock the Edit
  button to the right, clustered just **before** the ↑ ↓ ✕ buttons (chosen option: "Cluster
  Edit with ↑ ↓ ✕"). Keep ⚙ icon + truncated name sharing the flexible middle column; show
  the full group text as a tooltip on hover.
- **Fixed:** `da1c3ca` — replaced the inner horizontal StackPanel with a 3-column Grid
  (`Auto,*,Auto`) so the width-bounded `*` column lets the name's `CharacterEllipsis` fire and
  the Edit button docks hard-right next to ↑ ↓ ✕. Full group text shown via a `DisplayName`
  tooltip; explanatory branch tooltip moved to the ⚙ icon. Markup-only fix (no Core logic);
  verified visually by the user — truncation, clickable Edit, and full-text hover all confirmed.
