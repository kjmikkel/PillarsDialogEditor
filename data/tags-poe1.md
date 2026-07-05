# Pillars of Eternity — Dialog Text Tag Reference

Extracted by scanning all English conversation `.stringtable` files under
`PillarsOfEternity_Data/data{,_expansion1,_expansion2}/localized/en/text/conversations`
(GOG, with both White March expansions) for `[...]` and `<...>` markup.
Counts are occurrences across `DefaultText` + `FemaleText`.

The same three mechanisms as PoE2 (see `tags-poe2.md`), with a smaller
vocabulary — no ship tokens, and essentially no rich-text markup.

---

## 1. Substitution tokens

| Token | Count | Replaced with |
|---|---|---|
| `[Player Name]` | 257 | The Watcher's name |
| `[Player Race]` | 83 | The Watcher's race |
| `[Player Deity]` | 2 | The priest player's chosen deity |
| `[Specified 0]`, `[Specified 1]` | 409 / 13 | The character bound to the node's Specified Speaker slot *n* |
| `[SkillCheck 0]` … `[SkillCheck 3]` | 101 / 33 / 14 / 7 | The party member selected by skill-check node *n* |
| `[Slot 0]` … `[Slot 5]` | 132 / 118 / 118 / 115 / 115 / 115 | The character in party slot *n* (used heavily in party banter) |

PoE2-only tokens (`[Player Ship]`, `[Player Culture]`, `[Player Subrace]`,
`[Player Animal Companion]`, `[ShipDuel_*]`) do not exist here.

---

## 2. Rich-text markup (`<…>`)

Effectively none. The only angle-bracket tags in shipped text are
`<temp>`, `<Fail>`, and `<Success>` inside `test_interaction.stringtable`
(developer test content, rendered literally). PoE1 dialog text does not use
`<i>`, `<ispeech>`, `<link>`, or `<sprite>`.

---

## 3. Writing conventions (rendered literally)

- **Stage directions** — same convention as PoE2: `[Leave.]` (189+210),
  `[Attack]` (183 with/without period), `[Lie]` (128), `[Say nothing.]` (28),
  `[Intimidate]` (13), `[Turn around.]` (30), path choices in scripted
  interactions (`[Take the path to the left.]`, 23), etc.
- **VO/chatter annotations** — PoE1-specific: companion chatter stringtables
  describe non-verbal audio in brackets: `[Pained grunt]` (47),
  `[Effort grunt]` (42), `[Grunt/cry]` (42), `[Death grunt/cry]` (42),
  `[Coughing]` (22), `[Unintelligible, layered whispers]` (25), and similar
  `…Effort`/`…grunt`/`…cry` variants. These are subtitle placeholders for
  audio barks, not dialog options.

Skill/attribute/disposition option labels shown in-game (e.g. "[Perception]",
"[Honest]") are **not** in the text — the UI injects them from node data.
