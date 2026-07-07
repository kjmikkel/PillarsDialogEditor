# Token & Markup Validation in Node Text Editing — Design

**Date:** 2026-07-07
**Status:** Approved
**Gap:** `Gaps.md` › "Token autocomplete and validation in node text editing" (the **validation** half; autocomplete shipped 2026-07-06)
**Builds on:** `2026-07-06-token-autocomplete-design.md` (`TokenCompletionService`), `2026-07-05-tag-reference-window-design.md` (`TagCatalogue`, `tags.json`)

## Problem

The node text editor treats `[Player Name]`, `<i>…</i>`, and the rest of the tag
vocabulary as plain text. Autocomplete (2026-07-06) helps a writer *insert* correct
tokens, but nothing catches a token the writer typed by hand and got wrong —
`[Player Nmae]` (misspelling), `<i>` with no closing `</i>` (unbalanced markup) —
and nothing catches a token that a *translator* mangled (`[Player Name]` rewritten
into a language where the bracket text no longer matches the engine's token). These
survive to the shipped mod and silently fail to substitute.

This feature adds validation: it inspects text the writer/translator already typed
and flags likely token typos and unbalanced markup, while staying silent on the
large body of legitimate free-text bracket conventions.

## Why the discrimination is the whole problem

Autocomplete sidestepped the hard question by only ever *offering* known entries;
it never had to judge arbitrary bracket text. Validation cannot sidestep it. Shipped
data contains **~1,300 distinct free-text `[…]` conventions** — stage directions
(`[Say nothing.]`, `[Attack]`, `[Leave]`), language markers (`[Vailian]`), VO
annotations (`[Pained grunt]`), and engine-built skill/disposition labels
(`[Diplomacy]`). **None of these may ever be flagged.** The entire risk surface of
the feature is false positives on this corpus, so the detection rule is deliberately
conservative: it only speaks up when it has concrete evidence of a mistake.

`TagCatalogue` (engine-verified against both games' decompiled `Conversation.cs`) is
the single source of truth. Each entry has a `Kind`: `Token` (engine-substituted),
`Markup` (Unity rich text), or `Convention` (rendered literally — never a concrete
value, just a category description). Validation checks text against the `Token` and
`Markup` entries; `Convention` entries are *categories*, not matchable values, so
detection never matches against them — anything that isn't a recognised or
near-miss token is *assumed* to be a convention and left alone.

## Decisions (settled during brainstorming)

- **Detection rule — Hybrid.** Fuzzy "did you mean" as the primary signal, plus a
  secondary catch for a clearly parameterised token attempt. (Rejected: a pure
  shape heuristic, which would flag single-word conventions like `[Attack]`,
  `[Vailian]`, `[Diplomacy]` — an enormous false-positive surface.)
- **Surfacing — detail panel + Flow Analytics**, mirroring the bark-warning
  precedent. Live per-node warning box for Default/Female; Flow Analytics adds a
  per-conversation pass that also covers translations. (Rejected: inline squiggles —
  Avalonia `TextBox` has no built-in support, needs a custom adorner, much more View
  work and harder to test.)
- **Translation coverage — in Flow Analytics.** Default/Female get live detail-panel
  warnings; the Flow Analytics pass additionally validates every translation
  language for the open conversation. This is where translator-introduced token
  breakage is caught.
- **Leniency by design, not by special-case.** Markup validation checks *tag-name
  balance only* and never parses attributes, so the known-malformed vanilla
  `<link="neutralvalue://…>` (missing closing quote) passes for free.

## Architecture

Isolation follows the house pattern: pure logic in `DialogEditor.ViewModels`,
minimal View glue in `DialogEditor.Avalonia`. Core's `FlowAnalysisService` **cannot**
reference `TagCatalogue` (it lives in `DialogEditor.ViewModels`), so token validation
is **not** a new `FlowIssueKind` in the Core pass — it is a ViewModels-layer service
whose results are shown *alongside* the existing flow issues.

### Layer A — data (`tags.json`)

Add an optional `"lowercase": true` flag to the five Player entries whose all-lowercase
form the PoE2 engine also substitutes (documented today only in prose `notes`):
`[Player Race]`, `[Player Subrace]`, `[Player Class]`, `[Player Culture]`,
`[Player Background]`. `TagEntry` gains an optional `bool Lowercase` (default `false`).

A data test (mirroring `LookupKindWhitelistTests`) guards that every entry whose
`notes` mention the lowercase form carries `"lowercase": true`, so prose and flag
cannot drift.

### Layer B — pure service `TokenValidationService` (`DialogEditor.ViewModels/Services`)

Sibling to `TokenCompletionService`. Single query:

```
IReadOnlyList<TokenIssue> Validate(string text, string gameId)
```

```
record TokenIssue(TokenIssueKind Kind, string Fragment, string? Suggestion, int Position);
enum  TokenIssueKind { UnknownToken, UnbalancedMarkup }
```

Holds a `TagCatalogue` (defaults to `TagCatalogue.Instance`, injectable for tests,
matching `TokenCompletionService`/`TagReferenceViewModel`). Pure; never throws on
malformed input (returns an empty or partial list). `Position` is the character
offset of the fragment in `text` (for future inline use; not required by the current
surfaces).

**Unknown-token detection (Hybrid).** For each `[…]` span with content `X`:

1. **Exact match** to a known `Token` for `gameId` → OK. "Known" includes the
   all-lowercase variant when the matched entry has `"lowercase": true` **and**
   `gameId` is PoE2. (An empty/unknown `gameId` — project open, no game folder —
   uses the union of both games' tokens, matching the autocomplete side's
   over-offer-rather-than-hide stance; lowercase variants require an explicit PoE2
   context.)
2. **Fuzzy near-miss** — Damerau-Levenshtein distance (transposition-aware; typos
   like `Nmae` are a single transposition) from `X` to the nearest known token name,
   within a conservative length-relative threshold → flag `UnknownToken` with the
   nearest name as `Suggestion` ("did you mean '[Player Name]'?").
3. **Parameterised shape** — `X` is identifier word(s) + whitespace + digits (e.g.
   `Specfied 0`) whose identifier part is not a known parameterised token
   (`Specified`/`SkillCheck`/`Slot`) → flag `UnknownToken` with no suggestion
   ("looks like a token but isn't recognised").
4. **Otherwise** → assumed free-text convention, **silent**.

Threshold tuning is conservative and pinned by the false-positive regression test
(below): short tokens use a tighter absolute threshold so `[Loot]`/`[Lie]`-style
conventions don't fuzz-match short tokens like `[Slot n]`.

**Unbalanced-markup detection.** Stack-based balance of *known paired* markup tag
**names** only (`<i>`, `<ispeech>`, `<xg>`, `<color>`, `<link>`): push on open, pop on
matching close. Unmatched opens and unmatched closes → `UnbalancedMarkup`.
Self-closing `<sprite …>` is not paired and is skipped. **Attribute content is never
parsed or validated** — only the tag name and its `<`/`</`/`>` framing — which is
exactly what lets the vanilla malformed `<link="neutralvalue://…>` (missing closing
quote) balance and pass. Unknown tag names (`<b>`, `<foo>`) are not balance-checked;
they are out of the known-markup set and left alone (leniency).

### Layer C1 — detail panel (`NodeDetailViewModel` + `NodeDetailView.axaml`)

`NodeDetailViewModel` gains `TokenWarnings` (`IReadOnlyList<string>`), built by running
`TokenValidationService` over the selected node's **Default + Female** text and
formatting each `TokenIssue` into a localised message. Recomputed at the same trigger
points as `BarkWarnings` (`OnPropertyChanged(nameof(TokenWarnings))` is raised
alongside it on selection change and on Default/Female text edits).

Rendered in a warning `Border` in `NodeDetailView.axaml`, cloned from the existing
bark-warning box: `IsVisible` bound to `TokenWarnings.Count` via the existing
`CountToVis` converter, an `ItemsControl` over the messages, a mandatory tooltip, and
new `Brush.Validation.*` token(s) (or reuse of `Brush.Severity.Warning`) resolved
through the token registry. Detail panel covers Default/Female only — translations are
not edited here.

The active game id comes from the same source autocomplete uses
(`NodeDetailViewModel.ActiveGameId`).

### Layer C2 — Flow Analytics (`FlowAnalyticsViewModel`)

`Refresh` runs a second pass after `FlowAnalysisService.Analyze`. For each node it
validates Default/Female (from the `ConversationEditSnapshot`) and every translation
language for the open conversation (from a new accessor — a `Func` supplying the open
conversation's `patch.Translations`, i.e. `IReadOnlyDictionary<string,
IReadOnlyList<NodeTranslation>>`). Each resulting `TokenIssue` is tagged with its node
id and language.

Token issues are shown in a **separate `TokenIssues` collection rendered as its own
labelled section** in the Flow Analytics window, below the existing flow-issues list —
*not* folded into the `Issues` collection. Rationale: token rows carry dimensions the
existing `FlowIssueViewModel`/template don't model (which **language**, and the
**suggestion**), and `FlowIssueViewModel.Kind` is a `FlowIssueKind` enum token issues
don't belong in. A separate section keeps both concerns clean and leaves the existing
flow-issue row and template untouched. A `TokenIssueRowViewModel` exposes node id,
language label (or "Default"), the localised message, and a `NavigateCommand` reusing
the existing `_navigateToNode` action. The section has its own empty state.

**Scope limit (documented):** Flow Analytics operates on the currently-open
conversation, so translation validation covers *that* conversation's translations.
There is no single project-wide translation sweep today; see "Deferred".

## Cross-cutting rules (CLAUDE.md)

- **Localisation** — every warning message is a `Loc.Get`/`Loc.Format` resource
  (token name and suggestion are `Loc.Format` arguments). No hardcoded user-visible
  strings. Row/section chrome uses resource keys.
- **Tooltips** — the warning box and the Flow Analytics section header carry
  tooltips; no new icon-only controls are introduced.
- **UI Automation** — automation peers left intact; the Flow Analytics section and
  its rows are name-bearing and keyboard-reachable like the existing issue list.
- **Colour tokens** — any new brush is a `Brush.*` registry token (no stray hex;
  `NoStrayHexTests` stays green).
- **Error handling** — the service is pure and non-throwing (returns empty/partial on
  malformed input); any exception in View glue is logged via `AppLog.Error`/`Warn`;
  no bare `catch`.

## Testing (TDD, red first)

**`TokenValidationServiceTests` (unit — the bulk):**
- Exact-match tokens pass (Default/Female, both games).
- Lowercase variant: `[player race]` passes in PoE2, is flagged in PoE1; a lowercase
  form of a token *without* `"lowercase"` (e.g. `[player name]`) is flagged with a
  "did you mean" suggestion.
- Fuzzy: `[Player Nmae]` → `UnknownToken`, suggestion `[Player Name]`.
- Parameterised shape: `[Specfied 0]` → `UnknownToken`, no suggestion.
- **Silent on conventions:** `[Say nothing.]`, `[Draw your weapons and attack.]`,
  `[Attack]`, `[Lie]`, `[Vailian]`, `[Pained grunt]`, `[Diplomacy]` → **zero** issues.
- Markup: `<i>` without `</i>` flagged; unmatched `</i>` flagged; nested
  `<i><ispeech>…</ispeech></i>` balances; `<sprite …>` never unbalanced; malformed
  vanilla `<link="neutralvalue://…>` (missing closing quote) → **zero** markup issues;
  unknown tag `<b>…</b>` → **zero** issues.
- Game filter: a PoE2-only markup tag validated under PoE1 is not balance-checked.
- Empty/whitespace text → no issues; caret/position offsets correct for a mid-text
  fragment.

**False-positive regression:** a table of representative real shipped convention
values asserted to yield zero issues — the strongest guard against the ~1,300-value
false-positive risk.

**Data test** (mirrors `LookupKindWhitelistTests`): every `tags.json` entry whose
`notes` mention the lowercase form carries `"lowercase": true`.

**`NodeDetailViewModelTests`:** `TokenWarnings` populated for a bad token, empty for
clean text, recomputed on text edit and on selection change.

**`FlowAnalyticsViewModelTests`:** token issues surface for Default/Female and for a
translation language; navigation wired; the token section is empty when text is clean.

## Out of scope / deferred

- **Project-wide translation sweep** — Flow Analytics is per-open-conversation;
  a project-wide walk (like batch-VO-all-conversations) is a future extension.
- Validation in condition/script parameter editors and other text surfaces.
- Inline squiggles / adorner-based in-TextBox marking.
- Auto-fix / quick-fix actions (apply the suggested token in one click).
