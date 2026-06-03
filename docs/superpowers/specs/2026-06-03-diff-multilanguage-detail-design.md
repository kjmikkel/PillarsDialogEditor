# Multi-Language Before/After Detail — Design

**Date:** 2026-06-03
**Status:** Approved (brainstorming complete; ready for implementation plan)

## Goal

Extend the diff window's before/after detail panel (shipped 2026-05-31, single
language) to show a node's before/after text across **multiple languages** at
once, so a reviewer can see every changed translation without re-opening the
diff in another language.

This implements the deferred follow-up recorded in `Gaps.md` and the original
detail-panel spec:

> "Multi-language before/after (showing every changed language at once) remains a
> deferred follow-up — the panel currently uses the diff window's selected
> language."

## Background

The detail panel (`DiffWindow` + `DiffViewModel` + `NodeDiffDetailViewModel`)
shows, for the selected diff node, its Default and Female text before vs after,
with inline character-level highlighting via `TextDiff`. It is **single
language**: `DiffViewModel.ReconstructConversation` bakes in
`patch.Translations.GetValueOrDefault(_language)`, so all displayed text is the
one reconstructed `StringTable` for `_language` (fixed when the window opens).

Relevant existing types:
- `ConversationPatch.Translations` : `Dictionary<string, List<NodeTranslation>>`
  (language code → per-node translations).
- `NodeTranslation(int NodeId, string DefaultText, string FemaleText)`.
- `IGameDataProvider.AvailableLanguages` — **filesystem-derived** (folder names
  under `localized/`); an open set, not an enum.
- `TextDiff.Diff(before, after)` → `DiffSpan`s, rendered as coloured `Run`s.
- `SpeakerNameService.Resolve(guid)` — precedent for resolve-or-fallback naming.

## Decisions (from brainstorming)

- **Which languages appear:** the window's **primary** language (`_language`)
  always, as an anchor — **plus** every language where the node's text actually
  differs between the two versions. (Added node: any language with after-text;
  Removed node: any language with before-text.)
- **Layout:** all selected languages shown at once as **stacked, labeled
  sections** (scrollable). Primary first, then alphabetical by code.
- **Labels:** **friendly names** (e.g. "French"), resolved from resource keys
  with fallback to the raw code.
- **Rendering:** a small reusable `InlineDiffTextBlock` control renders each
  before/after pair (the dynamic section list cannot use fixed named TextBlocks).

## Design

### Candidate-language rule

For the selected node, the **candidate pool** to examine is:

```
{ primary } ∪ keys(leftPatch.Translations) ∪ keys(rightPatch.Translations)
```

where `leftPatch` / `rightPatch` are the patches for the selected conversation in
the two diff endpoints (absent patch → no keys). Rationale: the game base text is
identical on both diff sides, so only a language that *some patch touches* can
differ — no need to reconstruct every installed language.

A candidate language gets a **section** when:
- it is the primary language (always shown), or
- `Changed` node and its text differs between left and right in that language, or
- `Added` node and it has non-empty after-text in that language, or
- `Removed` node and it has non-empty before-text in that language.

If the node is `Changed` and **no** candidate language's text differs (text
identical in every candidate, including primary), the panel is **structural-only**
and shows the existing hint instead of any sections.

### Component 1 — `ReconstructConversation(name, project, provider, language)`

Add a `language` parameter (replacing the implicit `_language` field use) so the
VM can reconstruct a side for any language. The existing call sites pass
`_language`; behavior for the primary language is unchanged.

### Component 2 — `DiffViewModel` per-language caches

`_leftTextById` / `_rightTextById` change type from
`Dictionary<int,(string,string)>` to
`Dictionary<int, Dictionary<string,(string Default,string Female)>>`
(node id → language code → text).

In `BuildDiffCanvas`, compute the candidate language set for the selected
conversation, then for each candidate language reconstruct each side once and
populate the caches. The primary-language reconstruction also continues to drive
the canvas and ghost-node injection (unchanged). All existing cache-clearing
paths (early returns, error path) are preserved.

`UpdateSelectedNodeDetail` passes the node's per-language maps (plus the primary
code) into `NodeDiffDetailViewModel`.

### Component 3 — `NodeDiffDetailViewModel` → sections

Replace the four flat string properties with:

```
IReadOnlyList<LanguageDiffSection> Sections
bool IsStructuralOnly
bool ShowSections => !IsStructuralOnly
string HeaderText
```

`LanguageDiffSection`:
```
string LanguageCode
string LanguageName      // friendly name, resolved with code fallback
bool   IsPrimary
string DefaultBefore, DefaultAfter
string FemaleBefore,  FemaleAfter
bool   HasFemaleRow      // either side has real female text in this language
```

Construction input: node id, `DiffStatus`, primary code, and two
`Dictionary<string,(string Default,string Female)>` (left/right per-language
text). The VM applies the candidate/section rule, Added/Removed placeholder
substitution **per section**, female-row visibility **per section**, ordering
(primary first, then alphabetical), and structural-only detection. Placeholders
reuse the existing `Diff_Detail_NodeAdded` / `Diff_Detail_NodeRemoved` keys.

### Component 4 — `LanguageNameResolver`

A small resolver (in `DialogEditor.ViewModels`) mapping a language code to a
friendly name: a known code→resource-key table for the common PoE1/PoE2
languages (en, fr, de, es, it, pl, ru, pt-BR, zh-CN, ko, ja), returning
`Loc.Get(key)` for known codes and the **raw code** for anything unmapped.
Mirrors `SpeakerNameService.Resolve`'s resolve-or-fallback shape. The names live
in resources (`Language_Name_*` keys) so they remain translatable; only the
code→key mapping is in code.

### Component 5 — `InlineDiffTextBlock` control

A reusable Avalonia control (`DialogEditor.Avalonia/Controls/`) exposing `Before`
and `After` string properties; on change it populates its own `Inlines` from
`TextDiff.Diff(Before, After)` using the Common/Before/After brushes. The
`DiffWindow` detail panel becomes an `ItemsControl` over `Sections`, each item a
template with the language name, optional "(primary)" marker, and Default/Female
rows built from `InlineDiffTextBlock`. The existing single-language code-behind
(`UpdateDetail` + four named TextBlocks) is removed in favor of this control —
a targeted cleanup of the code being touched.

### Strings

New resource keys in `DialogEditor.Avalonia/Resources/Strings.axaml`:
- `Language_Name_en` … `Language_Name_ko` (friendly names for the common codes).
- `Diff_Detail_PrimaryMarker` — e.g. " (primary)".
Existing `Diff_Detail_*` keys are reused; obsolete single-language label keys are
kept if still referenced, removed if not.

## Data flow

```
node selected on DiffCanvas
  → DiffViewModel reads node id + DiffStatus
  → look up per-language left/right text maps (built per candidate language in BuildDiffCanvas)
  → NodeDiffDetailViewModel builds ordered LanguageDiffSections (primary + changed)
  → DiffWindow ItemsControl renders one InlineDiffTextBlock pair per section
```

## Error handling

- Per-language reconstruction runs inside the existing `BuildDiffCanvas`
  try/catch; a failure for one language logs via `AppLog.Warn` and that language
  is simply omitted (the primary section still renders if it succeeded).
- Unknown language codes fall back to the raw code (never throw).
- Caches cleared on all teardown paths, as today.

## Testing (red/green TDD)

- `LanguageNameResolver`: known code → resource name; unknown code → raw code.
- `NodeDiffDetailViewModel`:
  - primary always present (even when unchanged);
  - a language that changed appears; one that didn't (non-primary) does not;
  - sections ordered primary-first then alphabetical;
  - structural-only when no candidate language differs;
  - per-section female-row visibility;
  - Added/Removed placeholders applied per section.
- `DiffViewModel`: per-language caches populated; selecting a node with changes
  in two languages yields two (or three with primary) sections; preview mode
  still suppresses the panel.
- View: `ItemsControl` renders the expected number of sections;
  `InlineDiffTextBlock` populates inlines (Before/After highlighting).

## Intentional limitations / deferred follow-ups

- **Applied-Preview** still shows no detail panel (unchanged).
- **No per-language editing** (canvas is read-only).
- Friendly names cover the common PoE codes; exotic/unmapped codes show the raw
  code (acceptable fallback).
- **Non-primary base-text fallback.** `ReconstructConversation` sources base
  (untranslated) text from the provider's primary language; the `language`
  parameter only selects the translation overlay. So in a non-primary language
  section, a side with no translation for that node shows the primary-language
  base text (the effective in-game fallback), not a blank. A language translated
  on only one side can therefore show primary-language text under a non-primary
  header. Sourcing base text per language would couple the diff to
  game-data-per-language, which this feature deliberately avoids — deferred.

## Out of scope (YAGNI)

- A language filter/search within the panel.
- Collapsing/expanding individual language sections.
- Showing unchanged non-primary languages.
