# ExternalVO Alias UX — Design

**Date:** 2026-07-03
**Status:** Approved (brainstorming session 2026-07-03)

## Background (audit findings, 2026-07-03)

`ExternalVO` is a PoE2 VO **alias**: when set, the node plays *another* line's recording.
The stored value is a path relative to `Voices/<language>/`, without extension, shaped
`<speaker folder>/<conversation>_<nodeid:0000>` (e.g. `narrator/05_cv_dragon_dais_0001`).

- Shipped Deadfire data: **1,000 aliased nodes across 193 of 1,130 conversations**,
  787 distinct targets, 656 (83%) resolving to a real `.wem`. Reuse crosses conversation
  files. Five values deviate from the canonical shape (spaces in folder, uppercase,
  hyphenated conversation names) — all resolve on disk.
- PoE1: the equivalent `<VOFilename />` is empty on **all 40,991 shipped nodes** (base +
  White March). The field can never populate from PoE1 data.
- The editor's resolution chain already honours aliases everywhere
  (`VoPathResolver.Check`/`ExpectedRelativePath` → detail-pane status, ▶ playback,
  🎤 import destination, batch import, VO validation, orphan scanner).
- The gap (logged in `Gaps.md`): **importing VO on an aliased node silently overwrites
  the shared target file**, affecting every other node that aliases it.

## Goals

a) Convey the alias to the user (what it means, where it points, how widely shared).
b) Make it editable through a **node picker** — no free-text path entry; raw path readonly.
c) Close the silent-overwrite import hazard.

Out of scope: full VO manager window; creating aliases to node-less audio (see
Exclusions); filesystem watching for mods installed mid-session.

## Decisions (user-confirmed)

1. **Editing model:** pick a node (conversation + node), not a file. The alias is derived
   from the picked node's canonical path.
2. **Conveyance:** readable alias line + existence state + **shared-count** ("also used by
   N other nodes") — the count is what exposes the overwrite blast radius.
3. **Import guard:** confirmation dialog on single-node import with overwrite /
   clear-alias-and-import-own / cancel. Batch import excludes aliased rows.
4. **PoE1:** the External VO row is not rendered at all. Parser/serializer keep
   round-tripping the empty `<VOFilename />` unchanged.
5. **Raw path:** displayed readonly (selectable monospace); all edits via picker + Clear.
   No `{}`-toggle raw editing.
6. **Index strategy (Approach A):** one-time per-session background disk index with
   project-patch overlay (see Data layer).

## Section 1 — Voice group layout (PoE2)

The free-text `External VO` textbox is removed.

**No alias set:** existing content (`Has VO`, status row, ▶ play, 🎤 import) plus one new
button: **"Reuse another line's VO…"** → opens the node picker.

**Alias set:** an alias block replaces the own-file framing:
- Line 1: *"Plays the recording of `<conversation>` node `<id>`"* — parsed from the
  trailing `_0000`; falls back to showing the raw path when unparseable.
- Line 2: raw path, readonly monospace, selectable; plus ✓ found / ✗ missing for the
  **target** file (existing `VoiceSummary`/status semantics).
- Line 3: *"Also used by N other node(s)"* from the effective index; `0` renders as
  "not shared". Blank (not `0`) until the background index is ready.
- Buttons: **Change…** (picker, pre-selected to current target when parseable),
  **Clear** (empties `ExternalVO`, node falls back to its canonical path), existing
  ▶ play buttons (already alias-aware).

**PoE1:** none of the above renders.

All strings via `Strings.axaml`; every control tooltipped + `AutomationProperties`.

## Section 2 — Data layer

### `VoAliasParse` (pure, `DialogEditor.Core`)

`TryParse(aliasPath)` → `(speakerFolder, conversation, nodeId)?`. Split on the **last**
`_` followed by **four or more** digits at end-of-string (the writer pads with
`{nodeId:0000}`, a *minimum* of four — ids ≥ 10000 produce five digits); folder =
segment before the first `/`. Null for anything else (the 5 shipped oddballs and
hand-crafted values) → UI falls back to raw-path display. TDD against the audited
real shapes.

### `VoAliasIndexService` (static, session lifetime, `DialogEditor.ViewModels/Services`)

Same lifecycle pattern as `SpeakerNameService`: contents replaced wholesale, nothing
persisted; every app start / game-root switch re-reads current disk state.

- `Rebuild(gameRoot)`: background scan of
  `exported/design/conversations/**` **plus** `override/*/design/conversations/**`,
  with **override-wins precedence per conversation path** (an override version replaces
  the base file's contribution). Streaming regex extraction of non-empty `"ExternalVO"`
  values — no full JSON parse. Produces
  `Dictionary<string aliasPath, List<(string conv, int nodeId)>>`, case-insensitive keys.
- `GetReferences(aliasPath)`, `bool IsReady`.
- Failures log via `AppLog` and leave the service not-ready (UI shows no count).
- Test seam: `RegisterForTests(...)` mirroring `SpeakerNameService.Register`.
- Wired where `SpeakerNameService.Register` is called on game-folder open
  (`MainWindowViewModel`), fire-and-forget task.

### Project overlay (in `NodeDetailViewModel`, not the service)

Effective shared-count = disk references, minus entries the open project's patch also
touches, plus the patch's current in-memory `ExternalVO` values. Keeps the service
file-only and the overlay unit-testable without a filesystem.

Deliberately **not** built: filesystem watcher for mods installed mid-session (stale
until restart, matching `SpeakerNameService`; a manual rescan or F5 hook is the
designated extension point if it ever bites).

### New `NodeDetailViewModel` members

`HasVoAlias`, `VoAliasDescription`, `VoAliasSharedCount`, `VoAliasSharedText`,
picker/clear commands — refreshed in `NotifyAllProxies()`, localised via `Loc`.

## Section 3 — Node picker (`VoAliasPickerWindow` + `VoAliasPickerViewModel`)

- Two panes. Left: conversation list (game + override, same source as the main browser)
  with the existing filter-box pattern. Right: selected conversation's nodes — id,
  speaker name (`SpeakerNameService`), text preview (stringtable), ✓/✗ canonical `.wem`
  existence. Conversations parsed **on selection only**, reusing existing parsers
  read-only.
- Pickability: node selectable iff
  `VoPathResolver.ExpectedRelativePath(speakerGuid, "", nodeId, conv)` is non-null;
  others disabled with an explanatory tooltip. Nodes with missing `.wem` (✗) are
  pickable — the user may be about to import audio for them.
- Search box filters node list by text/id.
- OK / double-click writes the derived alias into `ExternalVO` through the existing VM
  property (undo/redo + dirty-tracking for free). **Change…** pre-selects the current
  target when the alias parses.
- Window carries the app icon, tooltips, `AutomationProperties`, localised strings.
  PoE2-only by construction (entry buttons don't render on PoE1).

## Section 4 — Import guard

**Single-node 🎤** (`NodeDetailViewModel.ImportVo`): when `ExternalVO` is set, an async
confirm delegate (pattern of `ShowImportDialog`) interposes, stating the target path and
the effective shared-count, with three outcomes:
1. **Overwrite the shared recording** → proceed as today.
2. **Clear alias, import to own path** → `ExternalVO = ""` (undoable), re-resolve, then
   proceed; unknown speaker prefix stops with the existing "no speaker" status.
3. **Cancel.**

**Batch import:** aliased rows render with status "shared audio (aliased)", browse/import
cells disabled, tooltip pointing at the single-node flow. Batch remains the tool for
filling *missing own* VO; deliberate alias overwrites take the deliberate path.

## Exclusions / trade-offs

- With the raw box readonly and picker node-based, an alias pointing at **node-less
  audio** (the 131 cut-content-style paths) can be displayed but not *created* in the
  editor. Conscious trade-off of the picker-only decision.
- The one shipped uppercase alias only resolves because Windows filesystems are
  case-insensitive; a Linux-hosted game folder would report it Missing. Accepted.

## Testing

- `VoAliasParse`: canonical shape, hyphenated conversation names, folder with spaces,
  uppercase, non-parsing fallbacks.
- `VoAliasIndexService`: temp-dir fixtures — base-only; base+override precedence;
  case-insensitive keys; malformed file skipped with `AppLog` warning.
- `NodeDetailViewModel`: alias description/count (overlay shadowing disk entries),
  three-way import-confirm branching via fake delegates, PoE1 invisibility,
  clear-alias undo.
- `VoAliasPickerViewModel`: pickability rule, filtering, derived path, pre-selection.
- Manual: alias a MyMod node via picker → status/▶ follow target; F5 → in-game reuse;
  clear-alias import → own recording plays.

Suite runs serially; static services (`VoAliasIndexService`) need test-reset seams like
the existing `SpeakerNameService.Register(empty)` pattern.
