# Token & Markup Autocomplete in Node Text Editing — Design

**Date:** 2026-07-06
**Status:** Approved
**Gap:** `Gaps.md` › "Token autocomplete and validation in node text editing" (the **autocomplete** half only; validation is a separate follow-up)
**Builds on:** `2026-07-05-tag-reference-window-design.md` (`TagCatalogue`, `tags.json`)

## Problem

The node text editor treats `[Player Name]`, `<i>…</i>`, and the rest of the tag
vocabulary as plain text. A writer must remember the exact spelling of every
substitution token and rich-text tag, and mistypes (`[Player Nmae]`, an `<i>`
with no `</i>`) survive to the shipped mod. The Tag Reference window (Help ▸ Text
Tag Reference…) lets them *look up* the vocabulary, but they still type it by
hand into the Default/Female fields.

This feature adds IDE-style autocomplete: typing `[` or `<` in a dialog-text
field offers the known tokens/markup for the open game and inserts a ready-to-fill
template on accept.

## Why the hard half is already solved

`TagCatalogue` (from the reference-window work) is **engine-verified against both
games' decompiled `Conversation.cs`** and tags every entry with a `Kind`:

- `Token` — engine-substituted (`[Player Name]`, `[Specified n]`) → offered on `[`
- `Markup` — Unity rich text (`<i>…</i>`, `<color="…">…</color>`) → offered on `<`
- `Convention` — rendered literally, open-ended free text (`[Say nothing.]`,
  `[Vailian]`, `[Pained grunt]`) → **never offered**

So autocomplete consumes the already-vetted list. The gap's standing open
question — "confirm the exact token list and case-insensitivity from the
token-replacement code instead of inferring from text" — was a blocker for the
**validation** half (discriminating a real token from a free-text stage
direction); it does not gate autocomplete, which only ever offers known entries.

## Scope

**In:** the two player-facing dialog-text fields in `NodeDetailView` — the
Default/Male `TextBox` (`x:Name="DefaultTextBox"`) and the Female `TextBox`.

**Out:** `ActorDirection` (a free-text stage direction, no player-facing tokens),
the editor-only node comment, condition/script parameter editors, and per-language
translation text (edited via the CSV/merge round-trip, not inline per node). None
carry the substitution vocabulary. **Validation** (unknown-token / unbalanced-tag
warnings) is explicitly out — a separate gap half.

## Decisions (settled during brainstorming)

- **Full IDE-style popup** — `[`/`<` opens a filtered dropdown that narrows as you
  type; ↑/↓ move, Enter/Tab/double-click accept, Esc/click-away dismiss.
- **Smart insert with the caret at the first placeholder** — accepting a
  parameterised or paired entry inserts a real template and lands the caret on the
  first thing to fill (see marker syntax).
- **Data-driven insertion templates** — the insert form lives in `tags.json` as an
  optional `insert` field, not derived by parsing display copy at runtime. Keeps
  the engine-verified data file the single source of truth and makes the insert
  logic trivially testable (this codebase's `tags.json` / `conditions.json` /
  `LookupKindWhitelistTests` pattern).
- **Popup item layout** — name-primary with the `Description` as a smaller second
  line.

## Architecture

Isolation follows the house pattern: pure logic in `DialogEditor.ViewModels`,
minimal View glue in `DialogEditor.Avalonia`.

### 1. Data: the `insert` field

`TagEntry` gains an optional `string? Insert`. `tags.json` entries may carry an
`insert` string containing **exactly one** marker:

- `${}` — an empty caret position.
- `${text}` — insert `text` **pre-selected**, so the writer types over it.

Entries with no `insert` (plain tokens) insert their `Name` verbatim with the
caret at the end. Worked set (⟦⟧ = selected, ‸ = caret):

| Display `Name`                        | `insert`                             | Applied result            |
|---------------------------------------|--------------------------------------|---------------------------|
| `[Player Name]`                       | *(none)*                             | `[Player Name]‸`          |
| `[Specified n]`                       | `[Specified ${0}]`                  | `[Specified ⟦0⟧]`         |
| `[SkillCheck n]`                      | `[SkillCheck ${0}]`                 | `[SkillCheck ⟦0⟧]`        |
| `[Slot n]`                            | `[Slot ${0}]`                       | `[Slot ⟦0⟧]`              |
| `<i>…</i>`                            | `<i>${}</i>`                        | `<i>‸</i>`                |
| `<ispeech>…</ispeech>`               | `<ispeech>${}</ispeech>`            | `<ispeech>‸</ispeech>`    |
| `<xg>…</xg>`                         | `<xg>${}</xg>`                      | `<xg>‸</xg>`              |
| `<color="…">…</color>`              | `<color="${}"></color>`            | `<color="‸"></color>`     |
| `<link="glossary://…">…</link>`     | `<link="glossary://${}"></link>`   | `<link="glossary://‸"></link>` |
| `<link="stringtooltip://…">…</link>`| `<link="stringtooltip://${}"></link>` | caret after `stringtooltip://` |
| `<link="neutralvalue://…">…</link>` | `<link="neutralvalue://${}"></link>` | caret after `neutralvalue://` |
| `<sprite="Inline" name="…" tint=1>` | `<sprite="Inline" name="${}" tint=1>` | caret in `name` quotes |

Only the *first* placeholder is targeted (per the settled decision); every
authored `insert` carries exactly one.

### 2. Completion core: `TokenCompletionService` (pure, `DialogEditor.ViewModels`)

Single query: given `(string text, int caretIndex, string gameId)` →
`CompletionResult` with the ordered candidate list, or "no active context".

- **Context detection** — scan back from the caret to the nearest `[` or `<`. If a
  matching `]`/`>` lies between that delimiter and the caret, the context is
  *closed* → no popup. Otherwise the delimiter opens a context: `[` → `Token`
  candidates, `<` → `Markup` candidates. `Convention` is never a candidate.
- **Fragment** — the substring from the delimiter to the caret (e.g. `[Pla`, `<is`).
  Token names contain spaces and `]`, so a space does **not** dismiss; only a
  literal `]`/`>` closes the context.
- **Match key** — each entry's *literal insertion text* (the `insert` form with
  markers stripped, or `Name` when there is no `insert`). Matching is a
  **case-insensitive prefix**: `[Pla` matches `[Player Name]`; `<is` matches
  `<ispeech>…</ispeech>` but not `<i>…</i>`.
- **Ranking** — matches ordered by descending shipped `Count` (common tokens
  first), then alphabetical.
- **Game filter** — entries whose `Games` contains `gameId`; an empty/unknown
  `gameId` (project open with no game folder) yields the **union** of both games —
  over-offer rather than silently hide. PoE1 therefore never sees `[ShipDuel_*]`.
- **Accept** — for a chosen entry, `ApplyCompletion(text, fragmentStart, entry)`
  returns `(int replaceStart, int replaceLength, string insertedText,
  int selectionStart, int selectionLength)`. The View applies it verbatim; the
  service owns all caret/selection math.

The service holds a `TagCatalogue` (defaulting to `TagCatalogue.Instance`,
injectable for tests, matching `TagReferenceViewModel`).

### 3. View glue: attached behaviour (`DialogEditor.Avalonia`)

A `TokenCompletionBehavior` attached to the two `TextBox`es. Responsibilities,
kept as thin as possible:

- On text/caret change, call the service; show/refilter/hide a `Popup` anchored to
  the caret with a keyboard-driven `ListBox`.
- Keys: ↑/↓ move selection, Enter/Tab/double-click accept, Esc and click-away
  dismiss, and the popup closes on its own when the context closes (`]`/`>` typed
  or no candidates match — reappearing if a backspace restores a matching
  fragment).
- On accept, apply the service's `ApplyCompletion` result to the `TextBox`
  (`Text` + `SelectionStart`/`SelectionEnd` or `CaretIndex`).
- Each popup row shows `Name` (primary) and `Description` (smaller second line),
  both sourced from `tags.json` data.

The active game id comes from the same source the node editor already exposes
(`NodeDetailViewModel.ActiveGameId`).

## Cross-cutting rules

- **Localisation** — no new user-visible hardcoded strings. Row text (`Name`,
  `Description`) is data from `tags.json`, exactly as the reference window renders
  it. Any static chrome the popup needs (none currently anticipated) would use a
  resource key.
- **Tooltips (CLAUDE.md)** — the two `TextBox`es already carry tooltips; the popup
  is a transient completion aid, not a new labelled control. No new toolbar/icon
  controls are introduced.
- **UI Automation (CLAUDE.md)** — automation peers are left intact; the popup list
  is keyboard-operable and its items are name-bearing, so the app stays drivable.
- **Error handling (CLAUDE.md)** — the service is pure and does not throw on
  malformed input (it returns "no context"). Any exception in the View glue is
  logged via `AppLog.Error`/`Warn`; no bare `catch`.

## Testing (TDD, red first)

**`TokenCompletionService` (unit):**
- Context: open `[`, open `<`, closed (delimiter already terminated), none, nested,
  at string start, at string end, caret mid-fragment.
- Fragment extraction incl. spaces inside token names (no premature dismiss).
- Game filter: PoE1 excludes `[ShipDuel_*]`; unknown game → union.
- `Convention` entries never appear.
- Ranking: higher `Count` before lower; alphabetical tiebreak.
- `ApplyCompletion` results: plain token (caret at end), parameterised
  (`0` selected), paired markup (empty caret between tags), attribute markup
  (caret inside the quotes).
- Case-insensitive matching (`[pla` → `[Player Name]`).
- No match → empty candidate list.

**Data (`tags.json`) — mirrors `LookupKindWhitelistTests`:**
- Every parameterised or paired entry has an `insert`.
- Every `insert` contains **exactly one** marker and parses.
- Display/insert agreement: the part of `Name` before its first placeholder
  (`n`, `…`) equals the part of the marker-stripped `insert` literal before its
  marker — e.g. `Name` `<color="…">…</color>` and `insert`
  `<color="${}"></color>` both open with `<color="`. Guards typos between the
  display and insert forms.

**View (headless, mirroring `ConversationView` glue tests):**
- Typing `[` shows the popup; Enter inserts and closes it; Esc hides it — to the
  extent the headless harness allows.

## Out of scope / deferred

- **Validation** (unknown-token warnings, unbalanced-markup warnings) — the other
  half of the gap; needs the free-text-vs-token discrimination the autocomplete
  side sidesteps by only ever offering known entries.
- Autocomplete in condition/script parameter editors and translation surfaces.
- Re-editing assistance for an *existing* token already in the text (e.g. renaming
  in place) — accept-and-insert only.
