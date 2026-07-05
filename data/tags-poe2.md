# Pillars of Eternity II: Deadfire — Dialog Text Tag Reference

Extracted by scanning all 1,073 English conversation `.stringtable` files
(`exported/localized/en/text/conversations`, GOG 5.0) for `[...]` and `<...>`
markup. Counts are occurrences across `DefaultText` + `FemaleText`.

**Token list verified against decompiled engine code (2026-07-05):**
`Assembly-CSharp\Game\Conversation.cs` (~lines 789–901) and
`Game\ShipDuelManager.cs` (~lines 959–996). Tokens with count 0 exist in the
engine but never occur in shipped dialog — still valid for mod text.

Three different mechanisms share the bracket syntax:

1. **Substitution tokens** — `[Token]` is replaced by the engine at runtime.
2. **Rich-text markup** — `<tag>…</tag>` styles the text (Unity rich text).
3. **Writing conventions** — brackets with free text inside carry meaning for
   the player, but the engine renders them literally.

---

## 1. Substitution tokens

The engine replaces these with dynamic values. Usable in mod dialog text.

### Player tokens

| Token | Count | Replaced with |
|---|---|---|
| `[Player Name]` | 199 | The Watcher's name |
| `[Player Ship]` | 188 | The player's ship name |
| `[Player Animal Companion]` | 33 | Ranger animal companion's name |
| `[Player Race]` | 8 | The Watcher's race (e.g. "elf") |
| `[Player Culture]` | 7 | The Watcher's culture (e.g. "Aedyr") |
| `[Player Subrace]` | 1 | The Watcher's subrace (e.g. "wood elf") |
| `[Player Deity]` | 1 | The priest player's chosen deity |
| `[Player Class]` | 1* | The Watcher's class (*shipped use is the lowercase variant, `06_cv_ateira.stringtable`) |
| `[Player Background]` | 0 | The Watcher's background (engine-only) |
| `[Player Paladin Order]` | 0 | The paladin player's order, gendered form (engine-only) |

**Lowercase variants:** matching is by exact-case pairs, **not**
case-insensitive. The engine does a second replace for `[player race]`,
`[player subrace]`, `[player class]`, `[player culture]`, and
`[player background]`, substituting the lower-cased value (for mid-sentence
use). No lowercase pair exists for Name, Ship, Animal Companion, Deity, or
Paladin Order — any other casing renders literally.

### Character-reference tokens

| Token | Count | Replaced with |
|---|---|---|
| `[Specified 0]` … `[Specified 5]` | 752 / 394 / 179 / 131 / 162 / 51 | The character bound to the node's Specified Speaker slot *n* (script-selected, e.g. a specific companion) |
| `[SkillCheck 0]`, `[SkillCheck 1]` | 79 / 4 | The party member selected by skill-check node *n* (highest relevant skill) |
| `[Slot 0]`, `[Slot 2]` | 3 / 1 | The character in party slot *n* — engine supports the full `[Slot 0]`…`[Slot 5]` range (shipped text only uses 0 and 2) |

### Ship-duel tokens (`re_si_ship_combat.stringtable`)

| Token | Count | Replaced with |
|---|---|---|
| `[ShipDuel_OpponentShip]` | 26 | Enemy ship's name |
| `[ShipDuel_PlayerShip]` | 22 | Player ship's name |
| `[ShipDuel_Opponent]` | 4 | Enemy captain's name |
| `[ShipDuel_SurrenderCost]` | 2 | Cost demanded on surrender |
| `[ShipDuel_CloseToBoardCost]` | 2 | Distance/cost to close for boarding |
| `[ShipDuel_PlayerFullSailDist]` | 1 | Distance at full sail |
| `[ShipDuel_PlayerHalfSailDist]` | 1 | Distance at half sail |
| `[ShipDuel_BraceChance]` | 1 | Brace success chance |
| `[ShipDuel_Player]` | 0 | Player captain's name (engine-only) |
| `[ShipDuel_FleeChance]` | 0 | Flee success chance (engine-only) |

`[ERROR]` (3 uses, same file) is a literal fallback shown when the ship AI
takes an unrecognized action — not a token to reuse.

### Other engine tokens

| Token | Count | Replaced with |
|---|---|---|
| `[Interaction Ability]` | 0 | Display name of the ability granted by a scripted interaction |
| `[NPCBacker]` | 0 | Backer NPC description text |
| `[God_Boon]` | 0 | Name of the god's boon being granted |

`[Temp]` is appended by the engine to nodes with missing text — a developer
marker, not a token to use.

---

## 2. Rich-text markup (`<…>`)

Unity rich-text tags; all pairs must be closed.

| Tag | Count | Effect |
|---|---|---|
| `<i>…</i>` | ~820 pairs | Italics — narration emphasis, foreign words, thoughts |
| `<ispeech>…</ispeech>` | ~490 pairs | "Inner speech" style — telepathy, soul-voices, gods (rendered italic with distinct styling) |
| `<xg>…</xg>` | ~17 pairs | Dialect styling for single words (e.g. `<xg>nae</xg>` in Engrim's brogue) |
| `<color="red">…</color>` | 7 pairs | Text colour (seen only in `si_legacy_history.stringtable` for required-history markers) |
| `<link="glossary://Entry">…</link>` | ~30 | Hover link to a glossary entry (ship actions, deities) |
| `<link="stringtooltip://sheet/id">…</link>` | ~10 | Hover tooltip from a tooltip stringtable (untranslated Vailian speeches, cyclopedia notes) |
| `<link="neutralvalue://Language: translation">…</link>` | ~80 | Hover translation of an in-world-language line; the text before `:` names the language |
| `<sprite="Inline" name="icon" tint=1>` | ~25 | Inline icon (ship-combat action buttons); self-closing |

---

## 3. Writing conventions (rendered literally)

These are free text, not a fixed vocabulary — the scan found ~1,300 distinct
values, all following two patterns:

- **Stage directions** — a player-response line consisting of (or starting
  with) a bracketed action is a non-spoken option: `[Say nothing.]`,
  `[Attack]`, `[Lie] "He kept the koīki for himself..."`, `[Leave] "Farewell."`
  The most frequent: Say nothing (306), Attack (283), Leave (387 with/without
  period), Lie (160 with/without period), Shrug (54), Nod (23+). A leading `[Lie]`, `[Bluff]`-style
  verb also cues the game's disposition system indirectly through the writer's
  intent, but the bracket text itself is display-only.
- **Language markers** — a leading bracket names the in-world language of the
  quoted line: `[Vailian] "Perla Vailian, fentre?"` — seen for Vailian, Huana,
  Rauataian, Engwithan, Ixamitl, Eld Aedyran, Ordhjóma, Lembur (mostly
  `companion_rekke_intro.stringtable`), usually combined with a
  `neutralvalue://` hover translation.

Skill/attribute option labels shown in-game (e.g. "[Diplomacy]") are **not**
in the text — the UI injects them from the node's skill-check data.

See also: `tags-poe1.md` for the PoE1 differences.
