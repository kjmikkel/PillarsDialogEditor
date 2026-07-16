# Duplicate / Near-Duplicate Line Detection — Design

**Date:** 2026-07-13
**Gap:** *Smaller Writer/UX Backlog ▸ Duplicate/near-duplicate line detection* in `Gaps.md`
**Status:** Design approved; implementation pending.

## Problem

Copy-paste is how dialog gets written: duplicate a node, tweak the text. The artifact is a
line the writer *meant* to change but didn't (an exact duplicate), or changed only trivially
(a near-duplicate). Nothing surfaces these today. The feature reports them so the writer can
find and fix — or explicitly mark them as intentional.

## Scope decisions (settled during brainstorming)

1. **Compare the writer's own lines only.** The candidate set is the Default text of every
   node the project **adds or edits**, across all patched conversations — not vanilla lines.
   This targets copy-paste artifacts in what the writer wrote, and keeps the set small (tens
   to low hundreds), so true fuzzy near-duplicate matching is cheap.
2. **Two tiers: exact + near.** Exact duplicates (identical after normalization) are a
   high-confidence tier; near-duplicates (similarity ≥ threshold) are a softer tier with the
   similarity % shown. Mirrors the stale-data Confirmed/Likely tiering.
3. **Home: the Validate Text… sweep** (`TextTagValidationWindow`), as new sections beside
   Tag/Spelling and Stale-data — the established home for project-wide text-quality reports.
   No new menu item or entry point.
4. **Read-only except ignore.** No bulk "remove duplicates" action — a duplicate is a writer
   judgement, not safe to auto-remove. The writer navigates and fixes by hand, or ignores.
5. **Ignore list (allowlist).** The writer can mark a duplicate/near pair as intentional; it
   moves to an **Ignored duplicates** pane and stops appearing in the active report. Ignores
   are content-keyed and persisted in the `.dialogproject` as editor metadata.
6. **v1 field scope: Default text only.** Female text, link/choice text, and non-primary
   translations are deferred (noise vs. the core artifact).

## Data source & candidate lines

The writer's authored text for both added and edited nodes lives in
`DialogProject.Patches[conv].Translations[primaryLanguage]` — the DiffEngine keeps all
dialog text there, and it is the same source `ProjectTextTagScanner` reads. So detection is
**game-folder-free, pure, synchronous, in-memory** — no off-thread scan (the candidate set is
the writer's edits only).

- **Candidate** = each `NodeTranslation.DefaultText` in `patch.Translations[primary]` across
  all patched conversations, as `(conversationName, nodeId, text)`.
- **Short-line suppression:** a candidate with **fewer than 4 words** after normalization is
  excluded — "Yes.", "No.", "[Continue]" duplicate legitimately and would drown the report.
  (Word count, not character count, is the single rule.)

## Matching algorithm

**Normalization (shared by both tiers):** trim, collapse internal whitespace runs to single
spaces, lowercase. Tags/tokens (`[Player Name]`, `<i>`) are **kept** — two lines differing
only by a tag are a *near* match, not exact.

**Tier 1 — Exact duplicates.** Group candidates by normalized string; any group with ≥ 2
members is an exact cluster. O(n) via a dictionary. Presented as a group: the shared text
once, then each `(conversation, node)` member.

**Tier 2 — Near-duplicates.** Among candidates **not** already in an exact cluster, flag
pairs whose similarity ≥ **0.85**. Similarity = normalized Levenshtein ratio,
`1 − editDistance(a,b) / max(len(a), len(b))`. Presented as a pair: both endpoints, the
similarity %, and both texts.

- **Threshold 0.85 is hard-coded** in v1 (no UI knob).
- **Length blocking:** two strings can only reach 0.85 similarity if their lengths are within
  ~15%, so each line is compared only against others in nearby length buckets — keeps the
  pairwise pass near-linear in practice without changing results.
- A candidate in an exact cluster is never *also* reported as a near pair.

## Ignore list (allowlist)

**Identity — content-keyed, so ignores survive re-scans and node renumbering:**
- **Exact group** → keyed by its normalized text (one key). Suppresses that cluster however
  many nodes share it.
- **Near pair** → keyed by the *unordered pair* of the two normalized texts (two keys,
  sorted). A later third similar line forms a new pair, still flagged.

**Persistence — editor metadata in the `.dialogproject`, like annotations.** A new nullable
field on `DialogProject`:

```csharp
public enum DuplicateKind { Exact, Near }
public record IgnoredDuplicate(DuplicateKind Kind, IReadOnlyList<string> Keys, string DisplayText);
// DialogProject gains:  IReadOnlyList<IgnoredDuplicate>? IgnoredDuplicates
```

- `Keys`: exact → `[normalizedText]`; near → the two normalized texts, sorted.
- `DisplayText`: a human-readable label built at ignore time (exact → the line; near →
  `«A» ~ «B»`), so the pane can show entries even after the underlying lines are edited apart.
- Nullable for back-compat (old project files load with no ignore list). Never written to
  game files.
- Immutable helpers `WithIgnoredDuplicate` / `WithoutIgnoredDuplicate` (record-`with` style,
  matching `WithPatch` / `WithAnnotations`).

**Behaviour:** ignoring/un-ignoring mutates the in-memory project and marks it **dirty**
(persists on next save, travels with the project). The sweep re-scans immediately, so the
effect is live without a save.

`DuplicateLineScanner.Scan(project, primaryLanguage)` reads `project.IgnoredDuplicates` and
returns the **active** report with ignored clusters/pairs already filtered out. The ignored
pane is driven by `project.IgnoredDuplicates` directly (persisted entries), so it lists
entries regardless of the current scan.

## Components

### `DuplicateLineScanner` (pure service, `DialogEditor.ViewModels.Services`)

Beside `ProjectTextTagScanner`.

```csharp
public static DuplicateLineReport Scan(DialogProject project, string primaryLanguage);

public record LineRef(string ConversationName, int NodeId, string Text);
public record ExactDuplicateGroup(string SampleText, IReadOnlyList<LineRef> Members);
public record NearDuplicatePair(LineRef A, LineRef B, int SimilarityPercent);
public record DuplicateLineReport(
    IReadOnlyList<ExactDuplicateGroup> Exact,
    IReadOnlyList<NearDuplicatePair>   Near);
```

Reads `project.Patches[*].Translations[primary]` for candidates and `project.IgnoredDuplicates`
for filtering. A small internal normalized-Levenshtein helper (or reuse an existing edit-distance
utility if one exists — the token validator already does fuzzy "did you mean" over digit-normalised
forms; check `TokenValidationService` for a reusable distance routine before adding one).

### `TextTagValidationViewModel` extensions

Three new optional delegates (defaulted `null`, mirroring `staleScan`/`prune` so existing
construction/tests are unaffected):

- `Func<DuplicateLineReport>? dupScan` — the active (filtered) report.
- `Func<IReadOnlyList<IgnoredDuplicate>>? ignoredList` — the pane contents.
- `Action<IgnoredDuplicate>? ignore`, `Action<IgnoredDuplicate>? unignore`.

New observable collections `DuplicateRows` and `IgnoredDuplicateRows`, plus
`DuplicateSummaryText` / `HasDuplicates` and `IgnoredSummaryText` / `HasIgnoredDuplicates`.
`Refresh()` calls `dupScan` + `ignoredList` alongside the existing tag/stale scans.

Row VMs:
- `DuplicateRowViewModel` — an exact-group header (+ member rows) or a near-pair row; a
  navigate callback (→ `NavigateToFoundNode`), and an **Ignore** command that calls `ignore`
  with the row's `IgnoredDuplicate` and refreshes.
- `IgnoredDuplicateRowViewModel` — display text + tier label + a **Restore** command that
  calls `unignore` and refreshes.

Textual tier labels (**Exact** / **Near ~87%**) — no colour-only encoding (Layer 2.5 rule).

### `TextTagValidationWindow.axaml`

Two new collapsible sections below Stale-data, following the existing section styling:
- **Duplicate lines** — shown when `HasDuplicates`; exact groups then near pairs; each row
  navigable (double-click / Enter → jump to node, cross-conversation, dirty-guard-respecting)
  with an Ignore action.
- **Ignored duplicates** — shown when `HasIgnoredDuplicates`; each entry with a Restore action.

### `MainWindowViewModel` wiring

Where it constructs the `TextTagValidationViewModel` (the existing `RequestTextTagValidationAsync`
path), also pass:
- `dupScan:     () => DuplicateLineScanner.Scan(_project!, _provider!.Language)`
- `ignoredList: () => _project!.IgnoredDuplicates ?? []`
- `ignore:      e => { _project = _project!.WithIgnoredDuplicate(e);    IsModified = true; }`
- `unignore:    e => { _project = _project!.WithoutIgnoredDuplicate(e); IsModified = true; }`
- navigate: the existing `NavigateToFoundNode`.

(The primary language is `_provider!.Language`; the sweep already requires an open project.)

## Testing (TDD, red first)

`DuplicateLineScannerTests`:
- Exact group across two patched conversations (case/whitespace differences still group).
- Near pair at ≥ 0.85 flagged; a below-threshold pair not flagged.
- Short lines (< min length) excluded from both tiers.
- A candidate in an exact group is not also reported as a near pair.
- Ignored exact key filtered from the active report; ignored near key-pair filtered.
- Length-blocking keeps a true near pair whose lengths differ slightly.

`DialogProjectTests` (or existing project tests):
- `WithIgnoredDuplicate` / `WithoutIgnoredDuplicate` round-trip.
- JSON serialize/deserialize; an old file without `IgnoredDuplicates` loads as null/empty.

`TextTagValidationViewModelTests` (extend):
- `dupScan` populates `DuplicateRows`; `HasDuplicates` reflects it.
- The Ignore command calls the `ignore` delegate and refreshes.
- `ignoredList` populates `IgnoredDuplicateRows`; the Restore command calls `unignore`.
- Existing constructor calls remain valid (new delegates default `null`).

`MainWindowViewModel` wiring test: the `ignore` delegate mutates `_project.IgnoredDuplicates`
and flips `IsModified`.

`TextTagValidationWindowTests` (smoke): the window constructs with duplicate + ignored rows.

## Deferred (YAGNI)

- Female text, link/choice text, and non-primary-language duplicate detection.
- A configurable similarity threshold / a UI knob.
- Bulk auto-remove of duplicates.
- Cross-vanilla comparison ("my line duplicates a vanilla line elsewhere") — the
  "my lines vs everything" scope, which needs a hashing/blocking strategy over ~40k lines.
- Within-conversation duplicate detection in Flow Analytics (a different, per-conversation lens).
