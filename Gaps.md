# Dialog Editor — Known Gaps

> **Temporary file — delete before the initial public release.** This is an internal,
> pre-launch record of design gaps and deferred features for solo development. At launch,
> anything still worth pursuing is transferred to GitHub Issues for public scrutiny, and
> this file is removed (see the **Internal Tracking (pre-launch)** rule in `CLAUDE.md`).

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
  Two merged resource dictionaries of *semantically named* tokens — `Resources/Palette.axaml`
  (primitive `Palette.*` colours; the only file permitted a hex literal) → `Resources/Tokens.axaml`
  (semantic `Brush.*` brushes, e.g. `Brush.Node.Npc.Header`, `Brush.Diff.Added.Fill`,
  `Brush.Toolbar.Button.Background`). Every hardcoded value migrated: `App.axaml` themes and all
  29 views bind `DynamicResource Brush.*`; the brush converters and code-behind resolve through
  `Theming/TokenBrushes.Resolve` instead of `new SolidColorBrush(...)`. The drift bug died by
  construction — the duplicated `NodeColorConverter`/`SpeakerCategoryToBrushConverter` palettes
  became one shared key. **Published contract enforced by test:** `NoStrayHexTests` fails the
  build if any hex literal appears outside `Palette.axaml` or any converter constructs a colour,
  making "nothing constructs a colour any other way" true rather than aspirational. The token
  naming taxonomy — the public interface every dependent gap quotes — and the exhaustive
  migration table live in `docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md`;
  the TDD implementation plan is `docs/superpowers/plans/2026-06-07-colour-token-taxonomy.md`.
  `Brush.Annotation.Region.*` remains a reserved (unpopulated) namespace for the Canvas
  Annotations gap. (Implementation added a few semantic tokens the spec's first draft omitted —
  `Brush.Button.{Primary,Destructive}.Background`, `Brush.Surface.Subtle`, `Brush.Connection.*`,
  `Brush.Text.OnLight*`, `Brush.Diff.Inline.*` — each value-faithful, no new colours.)
  - **Layer 0 follow-up — `DialogEditor.Avalonia.Shared` not yet migrated (deferred).** The
    Layer 0 migration and its `NoStrayHexTests` enforcer cover the **`DialogEditor.Avalonia`**
    project only. The sibling **`DialogEditor.Avalonia.Shared`** project is out of scope and
    still hardcodes colour: `PatchManagerView.axaml` carries **23 inline hex literals** (its
    code-behind constructs no brushes). Because the enforcer never scans this project, those
    literals are undetected — so the contract "nothing constructs a colour any other way" holds
    for the main app but **not** app-wide. To close: tokenise `PatchManagerView.axaml` onto the
    existing `Brush.*` keys (`Tokens.axaml` is already merged app-wide via `App.axaml`, so the
    tokens resolve there), then widen `NoStrayHexTests.AvaloniaRoot()` (or add a second root) to
    also scan `DialogEditor.Avalonia.Shared`. No new tokens are expected — the 23 literals should
    map onto the same surface/border/text greys and accents already in the registry.
- **Layer 1 — Palette sets (deferred, independent).** Alternative *values* for the same token
  keys: Dark (today's colours), Light, High-Contrast, and colourblind-tuned sets. Same keys,
  different hex; because Layer 0 guarantees everything reads keys, swapping the set retints
  the whole app.
- **Layer 2 — Runtime selection (deferred, independent).** The Settings UI to choose a
  palette/theme at runtime, persist the choice, and plug into Avalonia's `ThemeVariant`. This
  is the bulk of the work.
- **Layer 2.5 — Redundant non-colour encoding (deferred, independent; accessibility).** The
  part palettes can't fix: meaning must survive when hue is indistinguishable (~8% of men),
  via icons/borders/labels alongside colour. May warrant its own gap.

**Dependency rule:** other gaps point only at Layer 0. E.g. the **Canvas Annotations** gap
draws region colours from `Brush.Annotation.Region.*` tokens; if Layers 1/2/2.5 never ship,
annotations still render correctly with the single Dark set that exists today — never blocked
on, nor made wrong by, an upper layer that doesn't exist.

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
