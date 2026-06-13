# Dialog Editor — Known Gaps

> **Temporary file — delete before the initial public release.** This is an internal,
> pre-launch record of design gaps and deferred features for solo development. At launch,
> anything still worth pursuing is transferred to GitHub Issues for public scrutiny, and
> this file is removed (see the **Internal Tracking (pre-launch)** rule in `CLAUDE.md`).

## Structural (Code Quality)

### ViewModel Test Coverage
Significant coverage has been added: both `IGameDataProvider` implementations, `AutoLayoutService`, and several previously untested ViewModels (ConversationFolderViewModel, ConversationItemViewModel, PatchEntryViewModel, SettingsViewModel) now have tests. The remaining gaps are:

- Views / Converters — mostly covered: all 13 converters have unit tests; `LanguageCodeDialog`, `LegendWindow`, `UnsavedChangesDialog`, `ConflictResolutionDialog`, and `ConversationNameDialog` have headless Avalonia integration tests. Remaining untested views (`MainWindow` wiring, `NodeDetailView` modal launchers, and the Nodify canvas controls) contain no testable logic beyond what is already covered at the ViewModel layer.

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

> **Deferred idea (revisit) — first-run intro for the compare/apply window.** A one-time, dismissible explanatory panel shown the first time a writer opens the compare window, to orient non-technical users. Deferred from the selective-apply design (`docs/superpowers/specs/2026-05-30-selective-apply-design.md`) because it needs persisted "seen" state; the always-on in-context cues (colour-key strip, one-line hint, plain-language tooltips, Help window) cover the immediate need. Worth reconsidering after selective apply ships.

### Canvas Annotations (Sticky Notes & Regions)
**Deferred feature.** Writers should be able to leave free-floating annotations on the
conversation canvas — short reminders ("tighten this up, too many NPC nodes in a row") and
at-a-glance area labels ("the critical romance path", "locked into combat from here", "the
trauma conversation"). These serve both solo reminders and team communication.

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

### Accessibility — Assistive Technology & Keyboard (audit 2026-06-12)
The project has invested heavily in **visual** accessibility — the Layer 1 palette sets
(High-Contrast, Colourblind), `PaletteContrastTests` enforcing WCAG AA/AAA, the Layer 2.5
non-colour encoding work, and the mandatory-tooltip rule — but **assistive-technology and
keyboard accessibility are essentially absent**: there are zero `AutomationProperties` in
the entire codebase, custom-templated controls have no focus visuals, and the canvas is
mouse-only. The items below are ordered by the suggested tackle order (cheap, high-leverage
fixes first; the canvas keyboard-editing design task last among the big ones).

Recommended order rationale: **1 → 2 → 3** first — automation names and focus rings are
mechanical sweeps that unlock screen-reader/keyboard use of everything *outside* the canvas,
and each fits the project's test-first style (a failing test asserting the invariant, then
the sweep). Canvas keyboard editing (**4**) is the one genuine design task and should come
after the cheap wins. The rest are independent and can land in any order.

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
   **Deferred follow-up:** keyboard *connection creation* ("connect mode" — pick
   source, pick target, confirm) remains mouse-only; it needs its own interaction
   design pass.

5. **Tooltips are the sole explanation channel. ✅ IMPLEMENTED (2026-06-13).** Every
   focusable control's `ToolTip.Tip` is mirrored into `AutomationProperties.HelpText`
   (enforced by `AutomationHelpTextTests`, solution-wide), and `MainWindow` mirrors the
   focused control's `HelpText` into the status bar via
   `MainWindowViewModel.DisplayStatusText` — sighted keyboard users now see the same
   explanation screen readers announce on focus. Design:
   `docs/superpowers/specs/2026-06-13-helptext-and-focus-hint-design.md`.
   **Deferred follow-ups:** info icons on non-focusable elements (item 12), and a hint
   surface for windows other than MainWindow (item 13).

6. **Tiny fixed font sizes, no text scaling.** ~127 instances of 9–11px fonts across 19
   views; `NodeDetailView` group headers are **FontSize 8**. There is no UI-scale setting,
   and fixed-size windows (`SettingsWindow` is `CanResize="False" Height="220"`) will clip
   under OS text scaling. Opportunity: move font sizes into `Tokens.axaml` as semantic
   tokens (`FontSize.Caption`, `FontSize.Body`, …) — the Layer 0 token infrastructure and
   its enforcement-test pattern already exist — then add a scale factor in Settings.

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

10. **Hard-coded `Foreground="White"`.** Node titles and the diff badge glyph in
    `ConversationView.axaml`, plus the Resolve Conflicts button in `MainWindow.axaml`,
    bypass the palette system (named brushes evade `NoStrayHexTests`' hex check), so
    High-Contrast cannot adjust them. Tokenise (e.g. `Brush.Text.OnAccent`), and consider
    teaching `NoStrayHexTests` to also reject named-colour literals.

11. **Theme doesn't follow the OS; small hit targets.** Two smaller items: (a)
    `RequestedThemeVariant="Dark"` is fixed — detecting the OS high-contrast/dark
    preference for the *default* palette (Layer 2's `AppSettings.Theme` still overrides)
    would help users who've already configured their system; relatedly, Dark is
    "grandfathered" out of `PaletteContrastTests` AA — bringing it to AA and removing the
    exemption is a clean win. (b) Several hit targets (20px-wide ✕ clear buttons, slim
    toolbar buttons) sit below the WCAG 2.5.8 24×24 minimum.

12. **Info icons carry no tooltip/HelpText.** Item 5's sweep is scoped to *focusable*
    controls — static info icons (legend swatches, inline "i"/help glyphs rendered as
    `TextBlock`/`Border`) are skipped because they can't receive keyboard focus, so
    `AutomationProperties.HelpText` would never be announced on them. Making these
    operable (e.g. focusable `Button`-styled icons with both `ToolTip.Tip` and
    `AutomationProperties.HelpText`) would let keyboard/screen-reader users reach the
    same explanations sighted mouse users get.

13. **Focused-control hint is MainWindow-only.** Item 5's status-bar hint (Part B)
    depends on `MainWindow`'s status bar, which other windows/dialogs
    (`SettingsWindow`, `ScriptEditorWindow`, `ConditionEditorWindow`, `FindReplaceWindow`,
    `DiffWindow`, etc.) don't have — their `AutomationProperties.HelpText` (mirrored
    solution-wide by item 5's Part A) is reachable by screen readers but has no
    sighted-keyboard-user equivalent there. Worth a lightweight hint surface (e.g. a
    bottom hint bar) for dialogs once item 5's pattern proves out.

### UI Localisation Readiness (audit 2026-06-12)
The localisation rule (no hard-coded user-visible text) has been followed, but the app
cannot yet actually *switch language*. The good news from the audit: the architecture is
genuinely localisation-ready, and **restart-based** language switching is close — the
work is a delivery mechanism and a translation workflow, not a rewrite.

**Already in place (verified):**
- Single funnel: all UI strings live in three XAML dictionaries (`Strings.axaml`,
  `SharedStrings.axaml`, PatchManager's `Strings.axaml`); views make ~513
  `{StaticResource}` string references and ViewModels ~216 `Loc.Get`/`Loc.Format` calls.
  No class-init string caching found — lookups happen at display time.
- `Loc` → `AvaloniaStringProvider` queries `Application.Current.TryGetResource` on every
  call, so swapping the merged dictionary changes every subsequent C# lookup automatically.
- Theme Layer 2 is the exact mechanism precedent: persisted `AppSettings` choice, shared
  picker view, merged-dictionary swap applied in both apps' startup before the first
  window. A `Strings.de.axaml` overlay is the same trick as `Palette.Light.axaml`.
- Good hygiene: positional `{0}`/`{1}` placeholders via `Loc.Format` (no concatenation
  patterns found), translator notes already at the top of `Strings.axaml`, and
  `DialogEditor.Core`'s four strings already use `.resx`/`ResourceManager` with a
  settable `CoreStrings.Culture` (satellite-assembly ready).

**Work needed for restart-based switching (the recommended path).** Because all XAML
string references resolve when each window loads, merging the chosen language dictionary
at startup makes `StaticResource` work as-is — no reference sweep needed:
1. Per-language dictionaries with **overlay merge**: English merged first, chosen
   language merged last (last-merged wins), so untranslated keys fall back to English by
   construction.
2. `AppSettings.Language` + a Settings picker + a "takes effect after restart" note —
   clone of the theme-picker pattern, hosted by both apps.
3. Set `CoreStrings.Culture` at startup; add satellite `.resx` per language.
4. **Translation workflow (the real cost):** `.axaml` is translator-hostile. An
   export/import bridge to XLIFF or CSV is needed — the in-house
   `LocalizationExportService`/`ImportService` pattern (built for game content) is the
   template.
5. **Layout elasticity audit:** German/French run ~30% longer; fixed label widths
   (`Width="140"` in Settings), `CanResize="False"` fixed-height windows, and 200px node
   cards will clip. Overlaps with Accessibility item 6 (font tokens / text scaling) —
   do them together.
6. Minor: naive pluralisation (`"{0} nodes"` breaks in languages with multiple plural
   forms — usually accepted in tools); and the no-hardcoded-strings rule has **no
   enforcement test** (honor-system today, and a translated build makes violations
   invisible in English testing) — the `NoStrayHexTests`/`AutomationNameTests` structural
   pattern extends naturally here.

**Live switching (no restart) — deliberately out of scope.** It would additionally need
the ~513 `StaticResource` string refs converted to `DynamicResource` plus a
change-notification kick for the ~216 ViewModel lookups (the `ThemeService.Revision`
tick is the precedent). Several times the effort for little gain; restart-on-change is
the desktop norm.

**Difficulty verdict:** the switching mechanism itself is roughly a day following the
theme Layer 2 playbook; the honest costs are the translation file workflow (item 4) and
the layout robustness pass (item 5).

### Barks System — Bark Preview
Bark nodes now render with an amber color scheme on the canvas, carry bark-specific validation warnings (text too long, player-choice child), and those warnings surface in Flow Analytics. The remaining gap is an in-context preview of overhead floating text: writers cannot see how a bark will actually appear above an NPC's head without running the game. Implementing this requires investigating the game's bark rendering (font, line-wrapping, maximum visible width) before UI work can be designed.

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
editor and the version-control tools. The remaining onboarding idea is an **in-app guided
tour** (highlighting controls step-by-step), deferred — see the sample/tutorial spec's
"Future enhancements".

### Voice-Over Integration
An "External VO" field exists but there is no path validation, lip-sync metadata support, or audio preview. Mods that add or replace voiced lines have no tooling support. Note: actual voice-over audio is stored in a proprietary archive format — requires investigation before tooling can be designed.

### GUID Parameter Readability
Script and conditional parameters that are GUID-typed (`ObjectGuid`, `Guid`,
`GameData`) currently render as a plain `TextBox`, so the writer sees only opaque
strings — `b1a7e800-0000-0000-0000-000000000000` gives no clue that it refers to a
party member, let alone which one. We should identify the most-used GUID parameter
types in PoE2 and expose each value's human-readable meaning at the point of entry.

The target UX: keep free-text entry (any GUID can still be typed/pasted), but back
GUID inputs with a filtered suggestion list that matches on **both** the raw GUID
and the readable name (so typing "Aloth", "Edér", or part of the GUID surfaces the
right entry). This is the `AutoCompleteBox` pattern already used for enum params and
the script/condition pickers, applied to GUID-typed parameters instead of the plain
`TextBox`.

Most of the infrastructure exists, **for both games**. `SpeakerNameService` is the
single GUID→name registry, fed by whichever `IGameDataProvider` is loaded
(`MainWindowViewModel` registers `provider.LoadSpeakerNames()` on game load). PoE2
gets explicit names from `speakers.gamedatabundle`; PoE1 derives them via
`Poe1SpeakerNameParser` (maps `CharacterMapping` GUID→InstanceTag from the
`.conversation` files, resolves the tag against `characters.stringtable`, with
codename overrides like `GGP`→Durance). Both games also use GUID-typed parameters in
scripts/conditionals (e.g. PoE1 has 36 `ObjectGuid` condition params), so the feature
is equally relevant to both. The remaining work:

- Route `ObjectGuid`/`Guid` parameters to a suggestion-backed input sourced from
  `SpeakerNameService.All`, while preserving free text. Because both games normalise
  to the same registry, this is **game-agnostic** — building it once covers PoE1 and
  PoE2 with no game-specific branch.
- Make the suggestion filter match the GUID as well as the name (`SpeakerEntry.ToString()`
  currently returns only the name, so the GUID isn't searchable — needs a combined
  display string or a custom item filter), and show both ("Edér — b1a7e801-…") so the
  mapping is visible at a glance.
- For `GameData` / quest / item GUIDs there is no lookup table yet; identifying and
  loading the most common ones is a larger, deferred follow-up. A first pass can
  cover party/companion `ObjectGuid` params, which are the most common and already
  resolvable.

Caveat on PoE1 coverage: PoE1 names are *derived* heuristically rather than read from
an explicit speaker table, so resolution is good but imperfect (unmatched tags fall
back to the normalised instance tag). This is existing `SpeakerNameService` behaviour,
not extra work for this feature — it just means PoE1 suggestions may occasionally show
a tag-like name instead of a polished display name.

### Parameter Readability — Beyond Characters (PoE2 survey)
**Follow-up to GUID Parameter Readability; do this after the character case ships.**
Resolving character GUIDs (`ObjectGuid` → `SpeakerNameService`) is only the first slice.
Many other script/condition parameters are equally opaque, and before we can give them
the same name-suggestion treatment we need to know *what kinds of values they are* and
*where the readable names would come from*. The catalogue's `Type` field is coarse — it
flattens many distinct game-data kinds into `Guid`/`GameData` — so this gap's first step
is a **survey**, not implementation.

First step (the actual deliverable here): go through `scripts.json` and `conditions.json`
and, per parameter, record (a) its declared `Type`, (b) the concrete game-data kind it
actually points at (quest, item, ability, faction, global flag, map, …), and (c) where a
human-readable name for that kind could be sourced. The flattened types hide this — e.g.
a `Guid` param on `StartQuest` points at a quest, while a `GameData` param elsewhere
points at an item or ability, but both read as the same type today.

Current PoE2 type inventory (from the catalogues, to seed the survey):

- **Already readable / no work:** `Boolean`, `Operator`, and all `Enum:*` types already
  render as Options-backed suggestion lists. `Int32`/`Single` are plain numbers.
- **Character GUIDs:** `ObjectGuid` (≈51 condition + 2 script params) — handled by the
  GUID Parameter Readability gap above.
- **Opaque game-data GUIDs (the real follow-up targets):** `Guid` (≈30) and `GameData`
  (≈28) point at non-character assets — quests, items, abilities, factions, etc. Each
  distinct kind needs its own GUID→name lookup table sourced from the relevant
  `*.gamedatabundle`, mirroring what `Poe2SpeakerNameParser`/`SpeakerNameService` did for
  characters. They can't be tackled until the survey establishes which params map to which
  kind.
- **Other opaque-but-not-GUID:** `GlobalVariable` flag names (its `TypeHint` already points
  at `GlobalVariables.csv`, an obvious autocomplete source) and free-form `String` params
  (context-dependent — flag/item/conversation names — and may not generalise).

Output of the survey should be one or more concrete follow-up gaps (e.g. "Quest GUID
readability", "Item GUID readability", "GlobalVariable autocomplete"), each scoped to a
single game-data kind with an identified name source — so they can be implemented the same
way the character case was.
