# Dialog Text Tag Reference Window — Design

**Date:** 2026-07-05
**Status:** Approved
**Gap:** `Gaps.md` › "Dialog text tag reference window"
**Research:** `PoE Dialog Editor Research/Dialog Text Tags Research.md`, `data/tags-poe1.md`, `data/tags-poe2.md`

## Problem

The tag vocabulary of both games — substitution tokens like `[Player Name]`,
rich-text markup like `<i>`/`<ispeech>`, and literal writing conventions like
`[Say nothing.]` — is documented only in `data/tags-*.md` on disk. A mod author
working in the editor has no way to look up what a bracket means or which tokens
they can type. The condition vocabulary already solved this shape of problem with
`conditions.json` → `ConditionCatalogue` → game-aware UI; tags get the same
treatment.

## Authoritative sources (verified 2026-07-05 against decompiled code)

The token list comes from the engines, not from scanning shipped text:

- **PoE1** — `PoE1 Code\Assembly-CSharp\Conversation.cs` (lines ~551–600):
  `[Player Name]`, `[Player Race]`, `[Player Subrace]`, `[Player Class]`,
  `[Player Culture]`, `[Player Deity]`, `[Player Paladin Order]`,
  `[Slot n]`, `[SkillCheck n]`, `[Specified n]`, `[NPCBacker]`.
  **No lowercase variants** — a lowercase token renders literally in PoE1.
- **PoE2** — `PoE2 Code\Assembly-CSharp\Game\Conversation.cs` (lines ~789–901):
  all of the above plus `[Player Background]`, `[Player Ship]`,
  `[Player Animal Companion]`, `[Interaction Ability]`, `[God_Boon]`, and
  **explicit lowercase pairs** (`[player race]`, `[player subrace]`,
  `[player class]`, `[player culture]`, `[player background]`) that substitute
  the lower-cased value. Matching is exact-case pairs, not case-insensitive.
- **PoE2 ship duels** — `PoE2 Code\Assembly-CSharp\Game\ShipDuelManager.cs`
  (lines ~959–996): `[ShipDuel_Player]`, `[ShipDuel_PlayerShip]`,
  `[ShipDuel_Opponent]`, `[ShipDuel_OpponentShip]`, `[ShipDuel_SurrenderCost]`,
  `[ShipDuel_CloseToBoardCost]`, `[ShipDuel_PlayerFullSailDist]`,
  `[ShipDuel_PlayerHalfSailDist]`, `[ShipDuel_FleeChance]`, `[ShipDuel_BraceChance]`.

Some engine tokens never occur in shipped dialog (`[Player Background]`,
`[Player Paladin Order]` in PoE2 text, `[ShipDuel_Player]`, `[ShipDuel_FleeChance]`,
`[Interaction Ability]`, `[NPCBacker]`, `[God_Boon]`) — they are still valid for
mod authors and are included, marked as unused in shipped dialog (scan count 0).

Markup tags and writing conventions come from the 2026-07-05 stringtable scan
(raw inventories in `PoE Dialog Editor Research/raw-data/`).

**Corrections to `data/tags-*.md`** (same change): the PoE2 doc currently claims
token matching "appears to be case-insensitive" — wrong, it's explicit lowercase
pairs; both docs are missing the engine-only tokens above. The docs are updated to
match the engine findings so all three artifacts (md, json, research note) agree.

## Behaviour

- **Help ▸ Text Tag Reference…** opens a non-modal window (reopening focuses the
  existing one, matching other tool windows' behaviour).
- The window shows three sections: **Substitution tokens** (sub-grouped by
  category: Player, Character reference, Ship duel, Other), **Rich-text markup**,
  and **Writing conventions**. Each entry shows the tag, its description, an
  example from shipped dialog (where one exists), and per-entry notes (e.g. the
  PoE2 lowercase-variant rule).
- A **game selector** (PoE1 / PoE2 / Both) filters entries. It initialises from
  `_activeGameId` when a game folder is open, else defaults to PoE2. "Both" shows
  the union with per-entry game badges.
- A **search box** filters across all sections on tag name and description
  (case-insensitive substring). Empty search shows everything.
- All window chrome (title, section headers, selector labels, search watermark,
  tooltips) is localised via `Strings.axaml`. Entry content (names, descriptions,
  examples) is data from `tags.json`, English-only — same policy as condition
  descriptions.

## Components

| Unit | Responsibility |
|---|---|
| `data/tags.json` (new, repo root) | Source copy of the vocabulary, like `data/conditions.json`. |
| `DialogEditor.ViewModels\Resources\tags.json` (new, embedded) | The copy the app ships; `<EmbeddedResource>` like `conditions.json`. |
| `TagEntry` record (new, `DialogEditor.ViewModels\Services`) | `Name, Kind (Token/Markup/Convention), Games (["poe1","poe2"]), Category, Description, Example, Count, Notes` — `Count` is scan occurrences (0 = engine-supported, unused in shipped dialog); `Notes` nullable. |
| `TagCatalogue` (new, same folder) | Mirrors `ConditionCatalogue`: `LoadEmbedded()`, singleton `Instance`, `All`, `ForGame(gameId)`. |
| `TagReferenceViewModel` (new, `DialogEditor.ViewModels\ViewModels`) | Game selection (enum PoE1/PoE2/Both), `SearchText`, exposes filtered grouped collections per kind. Pure logic, fully testable. |
| `TagReferenceWindow.axaml(.cs)` (new, `DialogEditor.Avalonia\Views`) | Search box + game selector on top, three grouped sections in a scrollable body. `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`, tooltips on every interactive control, `AutomationProperties` for UIA. |
| `MainWindow.axaml(.cs)` + `MainWindowViewModel` | Help-menu item (between the tour group and the UI-strings group) + open/focus wiring following an existing non-modal window's pattern (e.g. Legend). |
| `Strings.axaml` | New keys: menu header + tooltip, window title, section headers, category names, game-selector labels, search watermark, "unused in shipped dialog" badge, "engine-only" note labels. |
| `data/tags-poe1.md` / `data/tags-poe2.md` | Corrected per engine findings (same commit as `tags.json` authoring). |

## Data flow

`tags.json` (embedded) → `TagCatalogue.LoadEmbedded()` (once, singleton) →
`TagReferenceViewModel` filters by game + search → XAML binds grouped
collections. No filesystem or game-folder dependency at runtime; the window
works with no game folder open.

## Error handling

`TagCatalogue.LoadEmbedded()` throws on missing/corrupt resource — a build
defect, surfaced by the existing exception hooks; no user-facing recovery
needed (same posture as `ConditionCatalogue`). No other IO exists.

## Testing (strict TDD)

`TagCatalogueTests` (new):
1. Embedded resource loads; entry count > 0; known entries present
   (`[Player Name]` both games, `[ShipDuel_Player]` poe2-only, `<ispeech>` poe2).
2. `ForGame("poe1")` excludes PoE2-only entries and vice versa.
3. Structural integrity: every entry has non-empty `Name`, `Kind`, `Games`,
   `Description`; every `Games` value is `poe1` or `poe2`.
4. PoE1 has no lowercase player-token variants; PoE2 lowercase variants exist
   (as `Notes` on the base token, not separate entries).

`TagReferenceViewModelTests` (new):
1. Game selector: PoE1 hides ship-duel tokens and markup (PoE1 ships none);
   Both shows the union.
2. Initial game: `poe1`/`poe2` from constructor arg; empty → PoE2.
3. Search filters on name and description, case-insensitive; clearing restores.
4. Grouping: tokens grouped by category, markup and conventions in their own
   sections.

Structural suites (`AutomationNameTests`, string-coverage tests) pick up the new
window automatically.

## Out of scope

- Token autocomplete/validation in the node text editor — the separate gap;
  it will consume `TagCatalogue`, which is why the catalogue lives in
  `DialogEditor.ViewModels`, not the window's code-behind.
- Rendering markup (`<i>` etc.) in node text display.
- Localising tag descriptions (follows the condition-description policy; revisit
  if that policy ever changes).
- Regenerating `data/tags-*.md` from `tags.json` — they are maintained by hand,
  with a header comment in each pointing at the other artifacts.
