# Dialog Editor — Known Gaps

> **Temporary file — delete before the initial public release.** This is an internal,
> pre-launch record of design gaps and deferred features for solo development. At launch,
> anything still worth pursuing is transferred to GitHub Issues for public scrutiny, and
> this file is removed (see the **Internal Tracking (pre-launch)** rule in `CLAUDE.md`).

## Structural (Code Quality)

### Exception Report Window ✓ implemented
Non-blocking `ExceptionReportWindow` shows exception type, message, scrollable monospace stack trace, a **Copy to clipboard** button, a clickable GitHub Issues link, and the log file path. Deduplicates per exception type per session (one window per type; re-shows after the window is closed). Wired into all three exception hooks in `App.axaml.cs`.

### ViewModel Test Coverage ✓ implemented
All ViewModels with non-trivial logic now have tests. Added: `AnnotationViewModelTests` (minimum-size clamping, `SyncScreen` math, snapshot round-trip, undo integration), `ConversationChangeViewModelTests` (tri-state `IsAllSelected`, `SelectedNodeIds`, `AutoPull` dependency closure), and `NodeChangeViewModelTests` (`SelectionChanged` event). Pure pass-through rows (`CommitRowViewModel`, `NodeBlameRowViewModel`) have no logic worth testing. Remaining untested views (`MainWindow` wiring, `NodeDetailView` modal launchers, Nodify canvas controls) contain no testable logic beyond what is already covered at the ViewModel layer.

---

## Feature Gaps

### ~~No "Save As…"~~ ✓ Implemented (2026-07-05)
**File ▸ Save Project As…** (Ctrl+Shift+S) saves the open project under a new
name/location with classic rebind semantics: subsequent saves target the new file, the
internal project `Name` follows the new filename, the window title/`LastProjectPath`
update, and the `_vo/` sidecar folder is copied alongside when the directory changes
(copy failure is reported but never rolls back the save). The command is available
whenever a project is open — no dirty-state requirement, so forking a clean project
works. Spec: docs/superpowers/specs/2026-07-05-save-project-as-design.md.

### ~~Non-save errors are status-bar-only~~ ✓ Implemented (2026-07-05)
Every `MainWindowViewModel` catch that logs `AppLog.Error` (open, import, merge,
game-data load, backup/restore, test-apply, batch VO, sample build, apply/undo-apply
saves, VO sync/index) now also surfaces the exception in `ExceptionReportWindow` via
the renamed `ReportError` delegate; the wiring posts to the UI thread so background
sites are safe, and the window's per-type dedupe prevents floods.
`ErrorReportingCoverageTests` enforces the rule structurally. `AppLog.Warn` sites stay
status-bar-only by design; git tool windows keep their in-window reporting, and the
`PatchConflictException` site is `// error-window-exempt:` because it has its own
recovery dialog.
Spec: docs/superpowers/specs/2026-07-05-error-window-non-save-design.md.

### ~~No "Close Project" command~~ ✓ Implemented (2026-07-05)
**File ▸ Close Project** (Ctrl+W, enabled only with a project open) runs the existing
unsaved-changes guard, then tears down project state via `CloseProjectCore` (shared
with the branch-switch vanished-file path, which keeps its distinct semantics: no
`LastProjectPath` clear, no canvas clear), clears the canvas (`ConversationViewModel.Clear()`),
and clears `AppSettings.LastProjectPath` so the next launch starts projectless.
Spec: docs/superpowers/specs/2026-07-05-close-project-design.md.

### ~~Export Mod Bundle without VO~~ ✓ Resolved by descoping (2026-07-05)
Use-case analysis rejected the proposed with-VO/without-VO export choice: the only
compelling case was that a text-only project (no `_vo/`) could not export a
`.dialogpack` at all — a gating defect, not a missing option. Fixed by making
**File ▸ Export Mod Bundle…** available for any saved project
(`CanExportModBundle`, formerly `HasLocalVoFolder`); the pack contains `vo/`
exactly when `_vo/` exists (consumers always treated `vo/` as optional). The
"exclude my existing VO" cases (small updates to voiced mods, separately
distributed VO) stay unserved until real demand appears.
Spec: docs/superpowers/specs/2026-07-05-export-without-vo-design.md.

### VO import over an ExternalVO alias silently overwrites shared audio
`ExternalVO` (PoE2 only; ~1,000 shipped nodes across 193 conversations) redirects a node's
VO to *another* line's `.wem` (`<speaker folder>/<conversation>_<nodeid>`, resolved under
`Voices/<language>/`), frequently shared across nodes and even across conversation files.
The resolution chain (`VoPathResolver.Check`/`ExpectedRelativePath` → status row, ▶ play,
single-node 🎤 import, batch import, VO validation, orphan scanner) consistently honours
the alias — audited 2026-07-03, all shipped value shapes resolve. **Resolved 2026-07-03:**
single-node import shows a three-way confirm (overwrite shared / clear alias and import to
the node's own path / cancel) naming the target and its shared-count; batch import excludes
aliased rows entirely; alias edits go through the node picker (raw path readonly), and the
Voice group shows the alias target plus "also used by N other node(s)" from a background
disk index. PoE1 note: `<VOFilename />` is empty on all 40,991 shipped nodes —
the External VO box can never populate from PoE1 data.

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

The **history browser** is implemented: a timeline of the open project file's git history (message, author, system-formatted date with an ISO tooltip); selecting a commit opens it in the compare window (diff + selective bring-in) via a preset right endpoint.

**Branch management** (implemented): a Branches window (Edit ▸ Branches…) lists
local branches with the current one marked, and switches, creates (`checkout -b`),
renames, and deletes them. Switching guards unsaved editor edits, reloads the project
from disk afterwards, and — when saved-but-uncommitted changes block the checkout —
offers an informed-consent "commit changes, then switch" (tracked files only; the
file list is shown before you accept). An untracked-file block is surfaced (not
papered over); deleting an unmerged branch requires a force-delete confirmation; the
current branch can't be deleted. Git-not-installed now shows a distinct message
across Compare/History/Attribution/Branches.

The Version Control Integration section is now essentially complete.

**Attribution / blame** (implemented): per-node "who last edited this" derived from `git blame --line-porcelain HEAD` of the `.dialogproject`. A single blame is mapped back onto nodes by `DialogProjectLineMap` (parses the exact blamed bytes with `Utf8JsonReader`, tracking line offsets, to find each node's line ranges across its `AddedNodes`/`ModifiedNodes` object, its `Translations[lang]` entries, and its `NodeComments` entry); each node is attributed to the most-recent commit touching any of those lines. Surfaced two ways: a standalone **Attribution** window (Versions ▸ Attribution…) listing every node with its last editor (author, date, commit, message), and a read-only "Last edited" line in the node detail panel for the selected node. Known limitations: blame is **HEAD-based** (uncommitted edits aren't attributed until committed; brand-new nodes simply don't appear); the map is computed at project open and **not auto-refreshed** after an in-session commit; `DeletedNodeIds` and layout-only changes are out of scope.

> **First-run intro for the compare/apply window — ✅ implemented.** `DiffWindow` shows a dismissible `IntroBanner` on first open, persisted via `AppSettings.DiffWindowSeen` (covered by `AppSettingsTests`). The original deferral reason (no persisted "seen" state) was later removed by the `ThemeOnboardingSeen`/`GuidedTourSeen` settings work.

### Canvas Annotations (Sticky Notes & Regions)
**✅ IMPLEMENTED (2026-06-21).** Writers can leave free-floating annotations on the
conversation canvas — short reminders ("tighten this up, too many NPC nodes in a row") and
at-a-glance area labels ("the critical romance path", "locked into combat from here", "the
trauma conversation"). These serve both solo reminders and team communication. Includes a
7-colour flyout swatch picker (commit `12d5fa8`). Files: `AnnotationViewModel`,
`AnnotationView.axaml`, `AnnotationColorConverter`, `AnnotationSnapshot`.

**Design (settled):** sticky notes and coloured regions are **one primitive**, not two
features. It is a titled, optionally coloured **box** placed at canvas coordinates. "Note"
vs "region" is purely a function of *size and whether it happens to enclose nodes* — there
is **no count-based mode switch**. Drop a small box → reads as a sticky note; drag a large
one around a cluster → reads as a region. Scope is **a single conversation's canvas** (no
cross-conversation regions).

Key decisions and their rationale:

- **Spatial, not membership-based.** The box is a rectangle in canvas space; it bounds
  whatever currently sits under it and does **not** track the node IDs it was drawn around.
  Moving a node out of a region simply removes it from the region with no caveat — this is
  correct by construction, not a disclaimer. (The rejected alternative — capturing node
  membership at creation but behaving spatially afterwards — implies a relationship it never
  maintains; the other alternative, true membership with hull recomputation + orphan
  handling on delete, is more machinery than the use case warrants.)
- **Canvas-owned, not node-attached** — this keeps it from duplicating the existing
  **per-node comment**. The two fill different roles: a node comment is *owned by the node*,
  part of its data/inspector, seen only when you inspect that node, and answers "why is
  *this line* written this way?"; an annotation is *owned by the canvas*, always-on and
  glanceable, and answers "what's going on in *this patch of the map*?". A single small
  annotation does **not** encroach as long as annotations are never anchored to / travelling
  with a node (that anchoring is exactly what would rebuild the node-comment feature behind
  a different renderer, so it is deliberately excluded).
- **Title + body, with a minimum readable size.** A pure-size model fights the requirement
  to hold a *medium, readable message*: the structure is a title bar (always visible, works
  as a region label even when the box is small) plus a wrapping body, with a sensible
  minimum width so the box can't be shrunk into illegibility.
- **Editor metadata, never game data.** Annotations must not leak into the file the game
  consumes — they persist as editor-only state (sidecar to / an ignored section of the
  `.dialogproject`). Persistence strategy is the main implementation decision still open.

Accessibility note: per-annotation colour should be drawn from a curated, theme-aware
palette rather than a raw RGB picker, so saved colours stay legible across light/dark/
high-contrast — see the **Centralised UI Colour Tokens** gap (the annotation region colours
are its `Brush.Annotation.Region.*` Layer 0 tokens). Because
~8% of men can't distinguish common hue pairs, region meaning must not rely on colour
alone (title text already provides the redundant encoding).

### Centralised UI Colour Tokens
**Deferred feature — purely UI; configured by the user in Settings.** Today colours are
scattered across three tiers with no single source of truth: hardcoded hex in `App.axaml`
control themes (`#333`/`#aaa`/`#444` on `ToolbarPlainButton`), hardcoded RGB constants in
~8 brush converters (`NodeColorConverter`, `DiffStatusToBrushConverter`,
`SpeakerCategoryToBrushConverter`, …), and inline values across ~34 `.axaml` files. The app
is hardcoded to `RequestedThemeVariant="Dark"` (stock `FluentTheme`) — there is no
light/dark switch to plug into. The duplication already drifts: `NodeColorConverter` carries
the comment *"mirrors `SpeakerCategoryToBrushConverter`"* because the speaker palette is
hand-copied into two files.

The goal is a single, named-token colour registry. It is framed as **layers**, of which only
the foundation is ever a dependency for other gaps; the upper layers are independent and may
land later or never without making anything beneath them wrong.

- **Layer 0 — Token registry (the foundation; the ONLY layer other gaps may depend on). ✅ IMPLEMENTED (2026-06-08).**
  Two merged resource dictionaries of *semantically named* tokens — `Palette.Dark.axaml`
  (primitive `Palette.*` colours; one of the palette family `Palette*.axaml`, the only files
  permitted hex literals — renamed from `Palette.axaml` when Layer 1 added sibling palettes) → `Tokens.axaml`
  (semantic `Brush.*` brushes, e.g. `Brush.Node.Npc.Header`, `Brush.Diff.Added.Fill`,
  `Brush.Toolbar.Button.Background`). Both now live in **`DialogEditor.Avalonia.Shared/Resources`**
  and are merged by *both* GUI apps' `App.axaml` (the editor and the standalone PatchManager),
  so the single registry serves the whole solution. Every hardcoded value migrated: `App.axaml`
  themes and all 29 views bind `DynamicResource Brush.*`; the brush converters and code-behind
  resolve through `Theming/TokenBrushes.Resolve` instead of `new SolidColorBrush(...)`. The drift bug died by
  construction — the duplicated `NodeColorConverter`/`SpeakerCategoryToBrushConverter` palettes
  became one shared key. **Published contract enforced by test:** `NoStrayHexTests` fails the
  build if any hex literal appears outside the palette family (`Palette*.axaml`), or any production
  type constructs a colour, making "nothing constructs a colour any other way" true rather than aspirational. The
  enforcer is **solution-wide** (anchored on `DialogEditor.slnx`, scanning every project's `.axaml`
  and production `.cs`), so the contract holds app-wide, not just for the editor project. The token
  naming taxonomy — the public interface every dependent gap quotes — and the exhaustive
  migration table live in `docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md`;
  the TDD implementation plan is `docs/superpowers/plans/2026-06-07-colour-token-taxonomy.md`.
  `Brush.Annotation.Region.*` remains a reserved (unpopulated) namespace for the Canvas
  Annotations gap. (Implementation added a few semantic tokens the spec's first draft omitted —
  `Brush.Button.{Primary,Destructive}.Background`, `Brush.Surface.Subtle`, `Brush.Connection.*`,
  `Brush.Text.OnLight*`, `Brush.Diff.Inline.*` — each value-faithful, no new colours.)
  - **Layer 0 follow-up — solution-wide coverage. ✅ DONE (2026-06-08).** The migration and its
    `NoStrayHexTests` enforcer now cover the **entire solution**, not just `DialogEditor.Avalonia`.
    Closed in three moves: (1) the token registry (`Palette.axaml` + `Tokens.axaml`) was relocated
    from `DialogEditor.Avalonia/Resources` into **`DialogEditor.Avalonia.Shared/Resources`** and is
    merged by *both* apps' `App.axaml`, so the shared `PatchManagerView` resolves the same `Brush.*`
    keys whether hosted by the editor or the standalone PatchManager app (the standalone app
    previously merged neither dictionary). (2) `PatchManagerView.axaml` (23 literals) and
    `PatchManager/MainWindow.axaml` (1 literal) were tokenised. (3) `NoStrayHexTests` now anchors on
    `DialogEditor.slnx` and scans every project's `.axaml` plus all production `.cs` (excluding the
    Tests project and the sanctioned `TokenBrushes` resolver). One nuance vs. the original "no new
    tokens expected" prediction: PatchManager carried a warning orange (`#e67e22`) absent from the
    palette and two hint greys (`#444`/`#555`) with no text-role token. Rather than grow the
    registry, these were **consolidated** onto the nearest existing tokens — `⚠` glyphs →
    `Brush.Severity.Warning`, faint hints → `Brush.Text.Disabled` — a deliberate, minor hue shift in
    PatchManager (leaner registry, no new colours). Everything else mapped value-faithfully.
- **Layer 1 — Palette sets. ✅ IMPLEMENTED — all four palettes (2026-06-09).**
  Alternative *values* for the same `Palette.*` token keys, as sibling files in
  `DialogEditor.Avalonia.Shared/Resources` beside the renamed `Palette.Dark.axaml`:
  `Palette.Light.axaml` (neutral ramp slot-inverted; WCAG AA), `Palette.Colourblind.axaml`
  (Dark + an Okabe–Ito remap of the red/green-collision accents; WCAG AA), and
  `Palette.HighContrast.axaml` (near-black surfaces, bright borders, white node cards; WCAG AAA).
  `Tokens.axaml` stays the frozen public contract **bar three surgical token splits** the alternate
  themes required: dark-on-light text → `Palette.Ink.*`; `Brush.Diff.Changed.Fill` → `Palette.Amber.610`
  (separate from the commit-hash gold); and the border/control split → `Palette.Line.*` +
  `Palette.Control.Background` (so HC can make dividers bright and toolbar buttons visible while
  surfaces go near-black). Every split is Dark/Light/Colourblind byte-identical. The sets are
  **merged nowhere** (the running app is unchanged); runtime selection is Layer 2. Accessibility is
  enforced by tests: `PaletteSetParityTests` (identical key set per palette), `PaletteGoldenTests`
  (committed per-value snapshot), `PaletteContrastTests` (WCAG AA for Light/Colourblind, AAA for
  High-Contrast; Dark is the grandfathered baseline and exempt), and `HighContrastBordersAreVisible`
  (HC dividers ≥4.5:1). Designs:
  `docs/superpowers/specs/2026-06-08-layer1-palette-sets-design.md` (Light/Colourblind) and
  `2026-06-09-layer1-highcontrast-design.md` (HC); plans alongside in `docs/superpowers/plans/`.
- **Layer 2 — Runtime selection. ✅ IMPLEMENTED (2026-06-11).** A persisted theme choice
  (`AppSettings.Theme`, default `"Dark"`) applied at runtime **across both apps**. A shared
  `ThemePickerView` + `ThemePickerViewModel` (driven by an injected `IThemeApplier` seam) is
  hosted by the editor's Settings window and by a top bar in the standalone PatchManager, so the
  same control/strings retint either app live. The Avalonia `ThemeApplier` (in
  `DialogEditor.Avalonia.Shared`) swaps the merged palette + a freshly-reloaded `Tokens.axaml`
  (Tokens references palette colours via `{StaticResource}`, resolved once at load, so it must
  be reloaded after the new palette) and flips `RequestedThemeVariant` (Light→Light, the rest→
  Dark). `{DynamicResource Brush.*}` chrome retints for free; converter-driven **canvas node
  colours** retint live via a `ThemeService.Current.Revision` tick fed as a throwaway extra
  source into the node-colour MultiBindings. Both `App.axaml.cs` apply the saved theme before
  the first window shows. The catalogue is a single add-point per palette (no hardcoded count,
  per Layer 1 §8). Design: `docs/superpowers/specs/2026-06-11-layer2-runtime-theme-design.md`.
  (Incidental fix: the standalone PatchManager's `app.ico` was declared `<None>`, not
  `<AvaloniaResource>`, so its window `Icon` crashed startup — corrected to embed the asset.)
- **Layer 2.5 — Redundant non-colour encoding. ✅ IMPLEMENTED (2026-06-11).** The part
  palettes can't fix: meaning must survive when hue is indistinguishable (~8% of men), via
  shape/glyph/line-pattern/text-decoration cues alongside colour, in `DialogEditor.Avalonia`.
  Five areas: (1) the canvas node header's speaker-identity marker is now a `Path` shape
  (Circle/Square/Triangle/Diamond/Player/Narrator/Script, Star for Bark) via
  `NodeTypeShapeConverter`, replacing a colour-only `Ellipse`; (2) diff-view nodes get a
  `+`/`~`/`−` corner badge (`DiffStatusToGlyphConverter`) alongside the existing coloured
  border; (3) "Always" connections get a `10,2,2,2` dash-dot `StrokeDashArray`, distinct from
  "Never"'s `4,3` dash, on both `Connection.always` and `Connection.highlighted.always`; (4)
  the Flow Analytics severity indicator widened from a 4px colour bar to a 16x16 badge with a
  ⛔/⚠ glyph (`FlowIssueKindToSeverityGlyphConverter`); (5) inline diff text
  (`InlineDiffTextBlock`, used by `DiffWindow`/`GitConflictResolutionWindow`) gains
  strikethrough (removed/before-side) and underline (added/after-side) alongside its existing
  colours. PatchManager already paired severity colours with ⚠ glyphs/tooltips/labels — no
  changes needed there. Minimap node markers and the legend window remain colour-only
  (canvas/PatchManager are the source of truth; out of scope). No new colour tokens — all
  changes reuse existing `Brush.*` tokens, so `NoStrayHexTests`/`PaletteContrastTests` are
  unaffected. Design:
  `docs/superpowers/specs/2026-06-11-layer2.5-non-colour-encoding-design.md`.

**Dependency rule:** other gaps point only at Layer 0 — which, being solution-wide, resolves the
same `Brush.*` keys in both apps. E.g. the **Canvas Annotations** gap draws region colours from
`Brush.Annotation.Region.*` tokens; if Layers 1/2/2.5 never ship, annotations still render
correctly across the whole solution with the single Dark set that exists today — never blocked
on, nor made wrong by, an upper layer that doesn't exist.

### Accessibility — Assistive Technology & Keyboard (audit 2026-06-12) — ✅ SECTION COMPLETE
**All 16 items below are resolved (2026-06-12 → 2026-06-15):** implemented, or the audit
premise was disproven by a headless probe (items 3 and 7). The audit's opening claim —
"zero `AutomationProperties` in the entire codebase, no focus visuals, mouse-only canvas" —
described the state **at audit time** and is long obsolete: automation names/HelpText are
now mandatory and structurally enforced (`AutomationNameTests`, `AutomationHelpTextTests`),
the canvas is fully keyboard-operable including connect mode, and live-region/status
announcements, focus-hint bars, hit-target sizes, and AA-checked palettes are all in place.
The items are kept below as the record of what was done and where the pinning tests live.
Only explicitly-deferred fragments remain open (noted inline where they occur — e.g.
`DiffWindow`/`FlowAnalyticsWindow` legend swatches in item 12).

1. **Screen-reader names for icon-only buttons. ✅ IMPLEMENTED (2026-06-12).**
   Icon-only buttons (⚙, ?, 📌, ⊞, ⊟, ⊕, ⌂, ✕, +, −, ↑, ↓, →, (?)) exposed only their
   glyph as the accessible name — a screen reader read "circled plus, button". Avalonia
   does **not** use `ToolTip.Tip` as a name fallback, so the mandated tooltips didn't
   help. All 26 such buttons across 8 views (editor + shared PatchManagerView) now carry
   `AutomationProperties.Name` bound to short localized `AutomationName_*` resources
   (19 strings in `Strings.axaml`/`SharedStrings.axaml`; names deliberately *short* —
   "Zoom in" — while the tooltip keeps the long explanation). Enforced structurally by
   `AutomationNameTests` (`DialogEditor.Tests/Accessibility`), mirroring `NoStrayHexTests`:
   any Button/ToggleButton whose Content is a literal glyph (no letters/digits) must have
   an `AutomationProperties.Name` that is a `{StaticResource}`/`{DynamicResource}`
   reference — a hard-coded English name also fails, keeping the localisation rule
   structural. Remaining follow-up idea: extend the CLAUDE.md tooltip rule to mention the
   paired `AutomationProperties.Name` on new controls (the test already enforces it).

2. **Form-field label association. ✅ IMPLEMENTED (2026-06-12).** Field labels were
   adjacent `TextBlock`s with no programmatic association — every input read as an
   unlabeled "edit" to a screen reader. All 43 TextBox/ComboBox/AutoCompleteBox/
   NumericUpDown controls across 18 views now carry `AutomationProperties.Name`:
   static forms reuse the *adjacent label's existing resource key* (no string
   duplication); template-generated per-row fields (script/condition parameter
   editors) bind the name to the row's label data (`{Binding Name}`); label-less
   search/filter boxes reuse their placeholder resources. Only 3 new strings were
   needed. Enforced by `AutomationNameTests.InputControlsCarryAccessibleLabels`:
   every input control must have `AutomationProperties.Name` as a resource reference
   or binding (hard-coded literals fail), or `AutomationProperties.LabeledBy`.

3. **Focus indicators on custom-templated controls. ✅ VERIFIED — premise disproven,
   no work needed (2026-06-12).** The audit assumed that replacing a button's template
   (`ToolbarPlainButton`/`ToolbarPlainToggleButton` define only `:pointerover`/`:pressed`
   styles) discards the focus visual. A headless probe disproved it: Avalonia's focus
   adorner lives in the **adorner layer**, independent of the control template, and
   renders a contrast-proof double ring (2px white outer + 1px semi-transparent black
   inner) whenever `:focus-visible` is active — keyboard focus IS visible on the custom
   toolbar buttons, in every palette, and likewise on all restyled-but-not-retemplated
   buttons. Pinned by `FocusVisibilityTests` (`DialogEditor.Tests/Accessibility`) so a
   future `FocusAdorner` override or Avalonia default change fails the build instead of
   silently blinding keyboard users.

4. **Canvas keyboard editing. ✅ IMPLEMENTED (navigate + edit structure, 2026-06-12).**
   The canvas is keyboard-operable: arrows traverse the conversation topologically
   (→ child / ← parent, nearest-by-Y; ↑↓ siblings in visual order), PgUp/PgDn cycle
   every node (orphan coverage), Home selects the root, Ctrl(+Shift)+arrows nudge the
   selected node (drag-move semantics, gated on IsEditable), Enter focuses the detail
   panel, Menu key / Shift+F10 opens the node context menu, Escape deselects; canvas
   focus restores the last selection (root on first focus) and the viewport follows
   every move. Pure logic in `CanvasNavigationService` + `ConversationViewModel`
   (unit-tested); thin KeyDown glue in `ConversationView` (headless-tested); key map
   documented in plain language in the Legend window (localized). Design:
   `docs/superpowers/specs/2026-06-12-canvas-keyboard-editing-design.md`.
   **Deferred follow-up: ✅ IMPLEMENTED (keyboard connect mode, 2026-06-15).** Ctrl+L or the "Connect to…" context-menu item enters connect mode; arrow keys choose the target; Enter confirms (self/duplicate → silent no-op); Escape cancels; source node shows a dashed border + 🔗 badge (top-left); status bar announces each transition. Design: `docs/superpowers/specs/2026-06-15-connect-mode-design.md`.

5. **Tooltips are the sole explanation channel. ✅ IMPLEMENTED (2026-06-13).** Every
   focusable control's `ToolTip.Tip` is mirrored into `AutomationProperties.HelpText`
   (enforced by `AutomationHelpTextTests`, solution-wide), and `MainWindow` mirrors the
   focused control's `HelpText` into the status bar via
   `MainWindowViewModel.DisplayStatusText` — sighted keyboard users now see the same
   explanation screen readers announce on focus. Design:
   `docs/superpowers/specs/2026-06-13-helptext-and-focus-hint-design.md`.
   **Deferred follow-ups:** info icons on non-focusable elements (item 12), and a hint
   surface for windows other than MainWindow (item 13).

6. **No UI-scale setting; fixed-size windows will clip under OS text scaling.**
   ✅ Part A IMPLEMENTED (2026-06-14): all 349 literal `FontSize` values across 30
   `.axaml` files now bind a 9-entry `FontSize.*` token layer in `Tokens.axaml`
   (`NoStrayFontSizeTests` enforces this — see
   `docs/superpowers/specs/2026-06-14-fontsize-token-foundation-design.md`).
   ✅ Part B IMPLEMENTED (2026-06-14): added a restart-required `FontScale` setting
   (100/125/150/175/200%) in `SettingsWindow`, with a live preview and restart notice,
   applied to all `FontSize.*` tokens at startup via `FontScaleApplier`
   (`AppSettings.FontScale`, `FontScaleApplierTests`). The 12 previously
   `CanResize="False"` dialogs (including `SettingsWindow`, formerly
   `Height="220"`) are now resizable with `MinWidth`/`SizeToContent="Height"`
   (`ResizableDialogTests` enforces this) — see
   `docs/superpowers/specs/2026-06-14-fontsize-scale-setting-design.md` and
   `docs/superpowers/plans/2026-06-14-fontsize-scale-setting.md`.

7. **Colour-only "new conversation" indicator. ✅ VERIFIED — premise disproven, no work
   needed (2026-06-13).** The audit assumed `BoolToNewConversationBrush`
   (`GameBrowserView.axaml`) — a green tint — was the *only* cue marking a new
   conversation, contradicting the Layer 2.5 non-colour-encoding principle. It isn't: new
   conversations are grouped under their own dedicated `"(new)"` folder
   (`Label_NewConversationsFolder`, inserted at the top of the tree by
   `GameBrowserViewModel.Load`) and each item's `DisplayName` carries a textual
   `" (new)"` suffix (`Label_NewConversation_Suffix`, existing since commit `b6191b5`,
   2026-05-19). The green brush is a third, redundant cue layered on top of two
   pre-existing non-colour ones. Pinned by `GameBrowserViewModelTests.Load_*` (dedicated
   folder, expanded, item `IsNew`) and the existing `DisplayName_WhenNew_ContainsName`
   (textual suffix).

8. **Status bar feedback is never announced. ✅ IMPLEMENTED (2026-06-13).** A
   hidden `StatusLiveRegion` TextBlock in `MainWindow`'s status bar, bound only to
   `StatusText` (not `DisplayStatusText`, so item 5's focus hints don't trigger
   duplicate announcements when tabbing around) and marked
   `AutomationProperties.LiveSetting="Polite"`, announces every operation result
   (save/error/project-opened/etc.) to screen readers. A headless probe confirmed
   `TextBlockAutomationPeer.GetName()` mirrors `Text` and automatically raises a
   `PropertyChanged` notification on change — purely declarative, no manual
   `RaisePropertyChangedEvent` call needed. Design:
   `docs/superpowers/specs/2026-06-13-status-bar-live-region-design.md`.

9. **Fake watermarks. ✅ IMPLEMENTED (2026-06-13).** `ConversationView`'s SearchBox and
   `GameBrowserView`'s FilterBox now use the real `TextBox.Watermark` property (the same
   pattern already used in `NodeDetailView` and `SettingsWindow`) instead of an overlay
   `TextBlock` shown via an `IsVisible`/`StringIsEmpty` binding — `Watermark` is exposed to
   the accessibility tree and handles focus/IME properly, where the overlay was purely
   decorative. `StringIsEmptyConverter` was removed (its registration and tests) as it had
   no remaining uses. Enforced by `FakeWatermarkTests`
   (`DialogEditor.Tests/Accessibility`), a solution-wide scan mirroring
   `AutomationNameTests`/`AutomationHelpTextTests` that fails if any `TextBlock` simulates
   a placeholder via an `IsVisible="{Binding ..., Converter={StaticResource
   StringIsEmpty}}"`-style binding.

10. **Hard-coded `Foreground="White"`. ✅ IMPLEMENTED (2026-06-13).** All 24
    occurrences across 14 views (node titles and the diff badge glyph in
    `ConversationView.axaml`, the Resolve Conflicts button in `MainWindow.axaml`, dialog
    header bars and primary/confirm/caution/destructive buttons, the flow-analytics
    severity glyph, and the test-mode overlay) now bind `{DynamicResource
    Brush.Text.OnAccent}` instead of the literal `"White"` — same value
    (`Palette.White` = `#FFFFFFFF` in every palette today) but now retintable per-theme.
    Enforced solution-wide by `NoNamedColourForegroundTests`
    (`DialogEditor.Tests/Theming`), mirroring `NoStrayHexTests`'s scope/exclusions.

11. **Theme doesn't follow the OS.** `RequestedThemeVariant="Dark"` is fixed.

    **(a) Auto theme from OS preference. ✅ IMPLEMENTED (2026-06-13).** `"Auto"` ("System
    Default") is now the first theme-picker entry and the default `AppSettings.Theme` for
    fresh installs (existing users keep their saved choice). `ThemeApplier.Apply("Auto")`
    resolves via `ThemeApplier.DetectOsThemeId`, which maps the OS's reported
    `PlatformColorValues` to `"HighContrast"` / `"Light"` / `"Dark"` — high-contrast wins
    over light/dark, matching the existing `HighContrast` palette; no platform settings
    falls back to `"Dark"`. Resolution happens at apply-time (launch, or immediately on
    picker selection), not via a live OS-change subscription. See
    `docs/superpowers/specs/2026-06-13-os-theme-detection-design.md`; the Dark AA-contrast
    half of this item and the first-run onboarding-dialog idea raised during brainstorming
    are split into items 14 and 15 below.

    **(b) Small hit targets. ✅ IMPLEMENTED (2026-06-13).** The two 20px-wide ✕ clear
    buttons (`ConversationView.axaml`'s search box, `GameBrowserView.axaml`'s filter box)
    are now 24x24, meeting the WCAG 2.5.8 minimum. Enforced solution-wide by
    `HitTargetSizeTests` (`DialogEditor.Tests/Accessibility`), which fails on any
    `<Button>` with an explicit `Width`/`Height` below 24 — a scan confirmed these were
    the only two offenders; "slim toolbar buttons" size via padding+content rather than
    explicit dimensions, so were not flagged.

12. **✅ IMPLEMENTED (2026-06-14).** The 7 legend swatches in `LegendWindow`'s
    "Connections" (Show Once, Always, Never) and "Node Types" (NPC line, Player
    choice, Narrator, Script/automated action) sections are now wrapped in
    borderless, transparent `Button.legendRow` elements — keyboard-focusable tab
    stops carrying `AutomationProperties.Name`, `ToolTip.Tip`, and
    `AutomationProperties.HelpText`, all three set to the same new full-sentence
    `Legend_*_Help` resource. `AutomationHelpTextTests` now auto-enforces the
    Tip/HelpText mirroring on these rows. `DiffWindow`/`DiffHelpWindow` swatches
    remain a documented won't-do — `DiffWindow`'s legend sits next to an
    already-accessible Help button with the full explanation.
    `FlowAnalyticsWindow`'s per-row icons: ✅ resolved (2026-07-04) — each issue
    row's severity tier (error/warning) is now textual via `SeverityLabel`
    (tooltip + `AutomationProperties.Name` on the existing non-focusable icon,
    so no new tab stops; see
    docs/superpowers/specs/2026-07-04-flow-severity-text-design.md). See
    docs/superpowers/specs/2026-06-14-legend-swatch-accessibility-design.md.

13. **✅ IMPLEMENTED (2026-06-13).** Focused-control hint is no longer MainWindow-only.
    A new shared `FocusHintBar` control (`DialogEditor.Avalonia.Shared`) mirrors the
    focused control's `AutomationProperties.HelpText` into a passive status-bar-styled
    bar, the same way item 5 Part B's `MainWindow.OnAnyGotFocus` feeds the status bar.
    Rolled out to the 10 "workhorse" windows: `SettingsWindow`, `ScriptEditorWindow`,
    `ConditionEditorWindow`, `FindReplaceWindow`, `DiffWindow`, `BatchReplaceWindow`,
    `ExportConversationsWindow`, `FlowAnalyticsWindow`, `BranchesWindow`,
    `GitConflictResolutionWindow`. The remaining 7 small 1–3-control dialogs are
    tracked separately as item 16. See
    docs/superpowers/specs/2026-06-13-focus-hint-bar-design.md.

14. **✅ IMPLEMENTED (2026-06-13).** Dark palette is now AA-checked. Split off from item
    11 — `PaletteContrastTests` previously grandfathered `Palette.Dark` out of its AA
    checks (4.5:1 normal / 3.0:1 large-UI). Of the 24 curated pairs, only `Severity.Error`
    on `Surface.Panel` failed (2.82:1). Brightened `Palette.Dark`'s `Red.500` from
    `#FFC0392B` to `#FFD33F2F` (3.30:1, same hue), added `Palette.Dark` to
    `PaletteMeetsContrastTargets`'s `[InlineData]`, and regenerated
    `palette-golden.approved.txt`.

15. **✅ IMPLEMENTED (commit `7b7af3e`).** First-run theme-onboarding dialog.
    `ThemeOnboardingWindow` (in `DialogEditor.Avalonia.Shared`) shows on fresh install
    (`AppSettings.ThemeOnboardingSeen = false`), hosts the existing `ThemePickerView`
    unmodified, and displays a live-retint preview panel (NPC/Player/Narrator node cards
    with Layer 2.5 shape glyphs, severity badges, button samples) — all via
    `{DynamicResource}` so selecting a palette retints the preview instantly. Continue
    button sets `ThemeOnboardingSeen = true` and closes; both `App.axaml.cs` files wire
    the window as `desktop.MainWindow` before `MainWindow` is created. Design:
    `docs/superpowers/specs/2026-06-14-theme-onboarding-design.md`.

16. **✅ IMPLEMENTED (2026-06-13).** Split off from item 13. Of the 7 small
    1–3-control dialogs left out of the item-13 rollout, each control's
    `AutomationProperties.HelpText` was compared against text already visible in
    its dialog. Three dialogs had at least one control whose `HelpText` is a real
    explanation beyond its visible label, and got a `FocusHintBar`:
    `AboutWindow` (Open Repository / Open Docs buttons; also switched from a fixed
    `Height` to `SizeToContent="Height"`, since its content is entirely
    ViewModel-bound and a fixed height was already a latent clipping risk),
    `ConflictResolutionDialog` (Cancel / Force Apply buttons), and `HistoryWindow`
    (Compare button). The other 4 — `BranchNameDialog`, `CommitConsentDialog`,
    `ChangelogWindow`, `ForceDeleteDialog` — deliberately do **not** get a
    `FocusHintBar`: every `HelpText` value in these dialogs duplicates text already
    on screen (an adjacent label, or the control's own caption), so a bar would
    only echo the screen. See
    docs/superpowers/specs/2026-06-13-focus-hint-bar-small-dialogs-design.md.

### UI Localisation Readiness (audit 2026-06-12)

**Items 1–3 IMPLEMENTED (2026-06-18):** `LanguageApplier` (overlay-merge mechanism,
English no-op, `_LanguageOverlayMarker` sentinel for stateless live switching),
`LocaleService` revision tick (in `DialogEditor.ViewModels.Services`),
`AppSettings.UiLanguage` ("en" default; TODO Auto once a translation ships),
`CoreLocale.SetCulture` facade, `LanguagePickerViewModel`/`LanguagePickerView` (live, no
restart required), editor `SettingsWindow` Language row, `PatchManagerSettingsWindow`
(Appearance + Language, replaces the top-bar theme picker). All `{StaticResource <string
key>}` in views converted to `{DynamicResource}`; `NoStaticStringResourceTests` enforces
the invariant. `AboutViewModel`, `ChangelogViewModel`, `ConversationViewModel` subscribe
to `LocaleService.Revision` for live getter refresh.

**Items 4–6 also implemented (see below) — the section is complete.** Live language
switching, the translation CSV round-trip, the layout-elasticity fixes, and CLDR
plural-category support are all in place — what is missing is not mechanism but
content: no translation overlay has been authored yet.

**Already in place (verified):**
- Single funnel: all UI strings live in three XAML dictionaries (`Strings.axaml`,
  `SharedStrings.axaml`, PatchManager's `Strings.axaml`); all views now use
  `{DynamicResource}` for string references; ViewModels use ~216 `Loc.Get`/`Loc.Format`
  calls. No class-init string caching found — lookups happen at display time.
- `Loc` → `AvaloniaStringProvider` queries `Application.Current.TryGetResource` on every
  call, so swapping the merged dictionary changes every subsequent C# lookup automatically.
- Theme Layer 2 is the exact mechanism precedent: persisted `AppSettings` choice, shared
  picker view, merged-dictionary swap applied in both apps' startup before the first
  window. A `Strings.de.axaml` overlay is the same trick as `Palette.Light.axaml`.
- Good hygiene: positional `{0}`/`{1}` placeholders via `Loc.Format` (no concatenation
  patterns found), translator notes already at the top of `Strings.axaml`, and
  `DialogEditor.Core`'s four strings already use `.resx`/`ResourceManager` with a
  settable `CoreStrings.Culture` (satellite-assembly ready).
- `NoStaticStringResourceTests` enforces `{DynamicResource}` for all string keys.

**Work needed (items 4–6):**
4. **Translation workflow. ✓ Implemented.** `UiStringExportService` reads the two embedded
   AXAML dictionaries (`Strings.axaml`, `SharedStrings.axaml`) and writes a four-column CSV
   (Key, Source, Translation, File). `UiStringImportService` reads a completed CSV and writes
   one `*.{lang}.axaml` overlay per source file, including the `_LanguageOverlayMarker`
   sentinel required by `LanguageApplier`. Both are wired to **Help ▸ Export UI strings…**
   and **Help ▸ Import translated UI strings…** in the main menu. Language code is
   auto-detected from the CSV filename (`ui-strings.de.csv` → `de`) or prompted via the
   `LanguageCodeDialog` if not detectable.
5. **Layout elasticity audit. ✓ Implemented.** Fixed: `SettingsWindow` label style `Width="140"` → `MinWidth="140"` (labels now grow to fit longer translated text); `FindReplaceWindow` label columns `70px` → `Auto` (grow with translated "Find:"/"Replace:" labels); added `MinWidth`/`MinHeight` to six large windows that had neither (BranchesWindow, HistoryWindow, BlameWindow, DiffWindow, GitConflictResolutionWindow, LegendWindow). Node cards (200px), table column trimming, and canvas-area `MaxHeight` constraints are intentional fixed-space layouts — left as-is.
6. **Pluralisation ✓ implemented (2026-07-04):** `Loc.FormatCount` resolves CLDR
   plural-category key suffixes (`_One`/`_Few`/`_Many`/`_Other`…) with rules shipped
   for en/de/fr/pl/ru/ar (en fallback for unknown languages) and a fallback chain
   `_Category` → `_Other` → bare key. All 24 naive "(s)" strings migrated
   (`NoNaivePluralTests` bans the idiom); two-count lines compose pre-pluralised
   fragments; CSV import accepts translator-added category rows (pinned by test).
   Spec: docs/superpowers/specs/2026-07-04-pluralisation-design.md.

### Barks System — Bark Preview
**✅ IMPLEMENTED (barks rendering + validation, 2026-06-21).** Bark nodes render with an
amber colour scheme on the canvas, carry bark-specific validation warnings (text too long,
player-choice child), and those warnings surface in Flow Analytics. The text-length
threshold is 324 characters — the longest bark in either shipped game (PoE1
`14_cv_iovara.conversation` node 73) — and warnings include the actual character count
and an explanation of the yardstick. Files: `BarkConstants`, `NodeColorConverter` (amber
palette), bark warning box in `NodeDetailView`.

**Deferred — in-context overhead-text preview:** writers cannot see how a bark will
actually appear above an NPC's head without running the game. Implementing this requires
investigating the game's bark rendering (font, line-wrapping, maximum visible width) before
UI work can be designed. Pixel dimensions are stored in Unity prefab/scene files; a test
run against the game is needed to calibrate any visual preview.

### About / Version Info
**Implemented.** **Help ▸ About…** shows the application name, version, licence, credits,
and links to the repository and online documentation. The version is read via the shared
`AppVersion` helper — the same source as `dialog-patcher --version` — so the GUI and CLI
never drift. (See `docs/superpowers/specs/2026-06-07-changelog-and-about-design.md`.)

### Changelog / Release Notes
**Implemented.** **Help ▸ Changelog…** opens an in-app reader that parses a bundled
`CHANGELOG.md` (grouped `### Added`/`### Changed`/`### Fixed` subsections per version,
newest first) and shows an empty-state message when there are no entries. Per the CLAUDE.md
**Changelog** rule, `CHANGELOG.md` stays **frozen until the initial public release**;
thereafter each release appends its notes. A version-aware "what's new since your last run"
layer remains a future enhancement — see the design spec.

### Onboarding
A **Create Sample Project** command (Help menu) plus a shipped **beginner walkthrough**
(`docs/walkthrough.md`) now give newcomers a safe, install-matched sandbox for learning the
editor and the version-control tools.

**In-app guided tour ✓ implemented (2026-06-23):** four-step tour bar + adorner ring
highlighting BrowserPanel → CanvasView → DetailPanel → HelpToggle. Auto-triggers on
fresh install; re-accessible via Help ▸ Start Guided Tour.

### Voice-Over Integration

**PoE2 path validation ✓ implemented (2026-06-21):** per-node status indicator in the detail panel and a batch "Validate Voice-Over…" window under Test menu. Resolves `ChatterPrefix` from `speakers.gamedatabundle` via `ChatterPrefixService`; handles `ExternalVO` override paths and Narrator GUID; detects female variants. PoE1 remains out of scope (Unity asset archives).

Remaining gaps:
- **Audio playback ✓ implemented (2026-06-22):** bundled `vgmstream-cli` r2117 + NAudio; play/stop toggle buttons in node detail panel; female variant support; `THIRD_PARTY_LICENSES.md` updated.
- **PoE1 VO** — Unity asset archives; deferred indefinitely.
- **Project-wide batch VO import ✓ implemented (2026-07-04):** "Batch import VO
  (all conversations)…" in the Test menu (the formerly orphaned
  `Menu_BatchImportVoAll` strings) scans every patched conversation via
  `ProjectVoRowScanner` (live canvas snapshot for the open conversation;
  unreadable conversations warned and skipped) and opens the existing
  `BatchVoImportDialog` in its multi-conversation mode
  (`isSingleConversation: false`, Conversation column visible). Scope is the
  project's patched conversations only — a VO-only change to an untouched
  vanilla conversation uses the per-conversation flow. Aliased rows stay
  listed-but-excluded. Empty scan reports via the status bar; gate requires an
  open, saved project and a PoE2 game folder.
  Spec: docs/superpowers/specs/2026-07-04-batch-vo-all-conversations-design.md.
- **Batch VO import entry point ✓ resolved (2026-07-04):** "Batch import VO…" now sits in
  the Test menu after Validate Voice-Over, auto-disabling via the command gate when no
  project is open or the conversation has no voiced nodes (visible-but-disabled for
  discoverability; tooltip names the conditions). The canvas context-menu item remains as
  a shortcut.
- **VO file lifecycle ✓ implemented (2026-07-02):** female-VO reporting is intent-driven (a leftover `_fem.wem` is no longer advertised when the node has no female text); **Validate Voice-Over** gains a project-wide orphaned-files section (files in `_vo/` no VO-enabled node references — deleted nodes, removed conversations, stale female slots) with an armed two-click cleanup that deletes the files and prunes empty prefix folders; and new node IDs never reuse an ID whose `_vo/` file still exists, so a new node can't silently inherit a deleted node's audio (B-005 hazard family). Spec: `docs/superpowers/specs/2026-07-02-vo-lifecycle-design.md`.
- **Mod VO ✓ implemented (2026-06-25):** per-node 🎤 import button and canvas context-menu item open `VoImportDialog` (primary + female `.wem`/`.wav` slots; Wwise detection; "Download Wwise" link). Imported files land in `_vo/` next to the `.dialogproject`; VO status row flips to ✓ immediately. F5 syncs `_vo/` to the game's VO directory with per-file backup; F6 removes/restores. "File › Export Mod Bundle…" packages the project and `_vo/` into a `.dialogpack` (ZIP). Patch Manager and CLI detect `.dialogpack`, extract to temp, apply dialog diff, and copy `vo/` to the game. Known limitation: `.wav` → `.wem` encoding via Wwise CLI is not yet implemented (requires a bundled `.wproj` template — users should pre-encode to `.wem` with the Wwise authoring tool and import the `.wem` directly).

### ~~GUID Parameter Readability~~ ✓ Implemented (first pass)

`ObjectGuid` and `Guid` parameters in the condition and script editors now show an
`AutoCompleteBox` backed by `SpeakerNameService.All` instead of a plain `TextBox`.
Suggestions are formatted "Name — GUID" so `FilterMode=Contains` matches on both the
speaker name and the raw GUID fragment. Selecting a suggestion writes only the bare
GUID back to the stored value (`OnValueChanged` normalises "Name — GUID" → GUID).
Free-text entry of any raw GUID is preserved. Built-in entries (Player, Narrator)
are always available; game-specific companions appear once a game folder is opened.
Works identically for PoE1 and PoE2 — both register into the same `SpeakerNameService`.

**Deferred (second pass):** `GameData`, quest, and item GUIDs have no lookup table;
the "Parameter Readability — Beyond Characters" gap tracks that follow-up survey.

### ~~Parameter Readability — Beyond Characters~~ ✓ Implemented

Implemented across commits `ccc27af..0e3ef19` (Tasks 1–9 of the implementation plan at
`docs/superpowers/plans/2026-06-19-parameter-readability-beyond-characters.md`).

**What shipped:**
- `GameDataNameService` — static registry (kind → `IReadOnlyList<NamedEntry>`) replacing
  the old hard-wired `SpeakerNameService` path. Registered at game-folder-open from
  `MainWindowViewModel.LoadDirectory`.
- `Poe2GameDataBundleParser` — parses `{GameDataObjects:[{ID,DebugName}]}` bundles; used
  by `Poe2GameDataProvider.LoadGameDataNames()`.
- `GlobalVariablesCsvParser` — parses `GlobalVariables.csv` variable-name column.
- `ParameterValueViewModel` refactored: `HasLookup`/`Suggestions` replace
  `IsGuidType`/`GuidSuggestions`; `OnValueChanged` resolves `DisplayName → StoredValue`
  via the registry.
- `ConditionParameter.LookupKind` added (nullable string); `IGameDataProvider`
  extended with `LoadGameDataNames()` (default empty-dict impl preserves test-stub compat).
- 133 `"lookupKind"` annotations across `scripts.json` and `conditions.json`, covering
  Speaker, Quest, Item, Ability, Class, Race, Subrace, Background, Culture, Deity,
  PaladinOrder, Faction, Disposition, DispositionStrength, Skill, Phrase, Keyword,
  StatusEffect, CreatureType, Map, Conversation, WeaponType, ArmorType, GlobalVariable.
- `LookupKindWhitelistTests` build-time guard: any typo in a `"lookupKind"` value
  triggers a failing test.

**Implemented since (PoE2 continued):**
- `Poe2GameDataProvider.LoadGameDataNames()` fully implemented — walks all confirmed
  `.gamedatabundle` filenames (items, abilities, statuseffects, factions, characters, global,
  worldmap, gui) plus quests, GlobalVariables.csv, and Speakers.
- `Class` lookup now filtered to `IsPlayerClass:"true"` entries via a `componentFilter`
  predicate on the `Components` array, keeping only playable classes and excluding NPC
  creature archetypes.
- `Conversation` lookup implemented: enumerates all `.conversationbundle` files and extracts
  `Conversations[0].ID` as the stored GUID, with the filename-without-extension as the
  display name. Registered under `"Conversation"` kind.

**Poe1GameDataProvider.LoadGameDataNames() implemented:**
- PoE1 conditions and scripts use only two lookup kinds: `Speaker` (served by
  `LoadSpeakerNames`) and `GlobalVariable`. The `GlobalVariable` kind is now populated
  from `design/global/game.globalvariables` (XML) via the new `Poe1GlobalVariablesParser`.
  No other lookup kinds appear in PoE1 conditions/scripts — Class, Race, Item, etc. are
  PoE2-only in the annotations.

**Still deferred:**
- `ArmorType` — the PoE2 condition `IsArmorTypeEquipped(Guid, Guid)` references armor-type
  GUIDs, but no `ArmorTypeGameData` bundle was found in any PoE2 `.gamedatabundle`, and the
  condition is unused in all shipped `.conversationbundle` files. Falls back to plain-text
  GUID input.
- `CreatureType` — likewise; `CreatureTypeGameData` absent from all bundles.
- Three parameters intentionally left without a `lookupKind` because no suitable game-data
  source was identified: `"Unlockable"` (PartyHasAbility — spans Ability/Phrase/Talent),
  and generic scene-object GUID params (`"Object GUID"`, `"Collider Object"` in ObjectGuid
  scripts) where the Speaker kind is applied but a scene-object lookup table doesn't exist.

### ~~Font Scale Live Switching~~ ✓ Implemented

Converted 371 `{StaticResource FontSize.*}` → `{DynamicResource FontSize.*}` across 33
`.axaml` files. Added `FontScaleService.Revision` ticker (mirrors `ThemeService`/
`LocaleService`). `FontScaleApplier` now implements `IFontScaleApplier` and bumps the
service on every call. `SettingsViewModel` injects `IFontScaleApplier`, calls it on
scale change, and the restart notice is removed. `NoStaticFontSizeResourceTests` enforcer
added to prevent regressions.
