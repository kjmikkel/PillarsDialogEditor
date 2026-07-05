---
tags: [research, text, tags, markup, stringtables, poe1, poe2]
status: first-pass
date: 2026-07-05
---

# Dialog Text Tags Research

What lives inside `[...]` and `<...>` in the conversation text of both games, extracted by scanning every English conversation `.stringtable` (`DefaultText` + `FemaleText`). Feeds the tag reference shipped in the repo (`data/tags-poe2.md`, `data/tags-poe1.md`) and any future editor features (autocomplete, validation, rendering).

**Scan scope:** PoE2 GOG 5.0 — 1,073 stringtables under `exported/localized/en/text/conversations`; PoE1 GOG + both White March expansions — `data{,_expansion1,_expansion2}/localized/en/text/conversations`. Script and full inventories in [[#Raw data]].

---

## TL;DR

| | PoE2 | PoE1 |
|---|---|---|
| Distinct `[...]` values | 1,372 | 1,048 |
| …of which real substitution tokens | ~25 | ~15 |
| Distinct `<...>` values | 65 | 3 (dev test file only) |
| Rich-text markup in dialog | ✅ extensive | ❌ none shipped |

Three unrelated mechanisms share the square-bracket syntax:

1. **Substitution tokens** — replaced by the engine at runtime (`[Player Name]`)
2. **Rich-text markup** — Unity rich text, angle brackets (`<i>`, `<link>`)
3. **Writing conventions** — rendered literally; meaning is for the player (`[Say nothing.]`, `[Vailian]`)

> [!important]
> Only the ~25 identifier-like tokens are a *vocabulary*. The other ~1,300 bracket values are free-text stage directions. Any future editor validation must not flag those as "unknown tags" — frequency + shape (identifier-like vs. sentence-like) is what separates them.

> [!note]
> In-game option labels like "[Diplomacy]" or PoE1's "[Honest]" are **not in the text**. The UI injects them from the node's skill-check/disposition data. A writer never types them; the editor should never expect them.

---

## 1. Substitution tokens

### Shared by both games

| Token | PoE2 count | PoE1 count | Replaced with |
|---|---|---|---|
| `[Player Name]` | 199 | 257 | The Watcher's name |
| `[Player Race]` | 8 | 83 | The Watcher's race |
| `[Player Deity]` | 1 | 2 | Priest player's deity |
| `[Specified 0…5]` | 752/394/179/131/162/51 | 409/13 (0–1 only) | Character bound to the node's Specified Speaker slot *n* |
| `[SkillCheck 0…3]` | 79/4 (0–1 only) | 101/33/14/7 | Party member selected by skill-check node *n* |
| `[Slot 0…5]` | 3/–/1 (sparse) | 132/118/118/115/115/115 | Character in party slot *n* (PoE1 party banter workhorse) |

### PoE2-only

| Token | Count | Replaced with |
|---|---|---|
| `[Player Ship]` | 188 | Player's ship name |
| `[Player Animal Companion]` | 33 | Ranger companion's name |
| `[Player Culture]` | 7 | Watcher's culture |
| `[Player Subrace]` | 1 | Watcher's subrace |
| `[player class]` | 1 | Watcher's class — sic, lowercase (`06_cv_ateira.stringtable`); token matching appears case-insensitive |
| `[ShipDuel_OpponentShip]` | 26 | Enemy ship name |
| `[ShipDuel_PlayerShip]` | 22 | Player ship name |
| `[ShipDuel_Opponent]` | 4 | Enemy captain name |
| `[ShipDuel_SurrenderCost]` | 2 | Surrender demand |
| `[ShipDuel_CloseToBoardCost]` | 2 | Cost to close for boarding |
| `[ShipDuel_PlayerFullSailDist]` | 1 | Distance at full sail |
| `[ShipDuel_PlayerHalfSailDist]` | 1 | Distance at half sail |
| `[ShipDuel_BraceChance]` | 1 | Brace success chance |

All ship-duel tokens live in `re_si_ship_combat.stringtable`, which also contains a literal `[ERROR] AI used unrecognized ship combat action.` fallback (3×) — not a token.

---

## 2. Rich-text markup (`<…>`) — PoE2

Unity rich-text tags; pairs must be closed. PoE1 ships none (only `<temp>`/`<Fail>`/`<Success>` in `test_interaction.stringtable`, developer content).

| Tag | ~Count | Effect |
|---|---|---|
| `<i>…</i>` | 820 pairs | Italics — narration emphasis, foreign words, thoughts |
| `<ispeech>…</ispeech>` | 490 pairs | "Inner speech" — telepathy, soul-voices, gods |
| `<xg>…</xg>` | 17 pairs | Dialect styling for single words (`<xg>nae</xg>`, Engrim's brogue) |
| `<color="red">…</color>` | 7 pairs | Text colour (`si_legacy_history.stringtable` required-history markers) |
| `<link="glossary://Entry">…</link>` | 30 | Hover glossary link (ship actions, deities) |
| `<link="stringtooltip://sheet/id">…</link>` | 10 | Hover tooltip from tooltip stringtables (untranslated Vailian speeches, cyclopedia) |
| `<link="neutralvalue://Language: translation">…</link>` | 80 | Hover translation of an in-world-language line; text before `:` names the language |
| `<sprite="Inline" name="icon" tint=1>` | 25 | Inline icon, self-closing (ship-combat action buttons) |

> [!tip] Editor implications
> If the canvas or detail pane ever renders node text, `<i>`/`<ispeech>` cover 95%+ of markup encountered. `neutralvalue://` links double as the localisation mechanism for fictional languages — stripping tags naively would lose the translation.

Data quirks worth remembering: a few `link` attributes are missing their closing quote in shipped data (e.g. `<link="neutralvalue://Vailian: Give me the Devourer of Souls!>` in `re_cv_ghost_ship_captain_encounter.stringtable`) — a lenient parser is required.

---

## 3. Writing conventions (rendered literally)

- **Stage directions** — bracketed non-spoken actions in player options: `[Say nothing.]` (306 in PoE2), `[Attack]`/`[Attack.]` (283), `[Leave]`/`[Leave.]` (387), `[Lie]` (160), `[Shrug]` (54)… May stand alone or prefix speech: `[Lie] "He kept the koīki for himself..."`. Same convention in PoE1 (`[Leave.]` 189, `[Intimidate]` 13).
- **Language markers** (PoE2) — leading bracket names the in-world language of the quoted line: `[Vailian] "Perla Vailian, fentre?"`. Seen for Vailian, Huana, Rauataian, Engwithan, Ixamitl, Eld Aedyran, Ordhjóma, Lembur — mostly `companion_rekke_intro.stringtable`, usually paired with a `neutralvalue://` hover translation.
- **VO/chatter annotations** (PoE1) — subtitle placeholders for audio barks in companion chatter: `[Pained grunt]` (47), `[Effort grunt]` (42), `[Grunt/cry]` (42), `[Death grunt/cry]` (42), `[Unintelligible, layered whispers]` (25), `[Coughing]` (22). Related to the chatter system covered in [[Voice-Over Integration Research]].

---

## Raw data

Complete inventories (token ⟶ count ⟶ example line ⟶ example file, TSV, sorted by count) plus the scan script, under `raw-data/`:

- [poe2-square-bracket-inventory.tsv](raw-data/poe2-square-bracket-inventory.tsv) — all 1,372 values
- [poe2-angle-bracket-inventory.tsv](raw-data/poe2-angle-bracket-inventory.tsv) — all 65 values
- [poe1-square-bracket-inventory.tsv](raw-data/poe1-square-bracket-inventory.tsv) — all 1,048 values
- [poe1-angle-bracket-inventory.tsv](raw-data/poe1-angle-bracket-inventory.tsv) — the 3 test-file values
- [scan-tags.ps1](raw-data/scan-tags.ps1) — the PowerShell scanner (re-runnable against any game copy)

## Open questions

- Which class implements the `[Token]` replacement in PoE2 (`Assembly-CSharp`)? Decompiling it would confirm the exact token list + case-insensitivity instead of inferring from shipped text.
- Does `[Slot n]` in PoE2 still work, or is it vestigial (only 4 occurrences vs. 713 in PoE1)?
- Possible editor features: token autocomplete in node text, unknown-token warning (identifier-shaped only), `<i>`/`<ispeech>` rendering in the detail pane.
