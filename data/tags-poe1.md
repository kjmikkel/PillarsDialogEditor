# Pillars of Eternity — Dialog Text Tag Reference

Extracted by scanning all English conversation `.stringtable` files under
`PillarsOfEternity_Data/data{,_expansion1,_expansion2}/localized/en/text/conversations`
(GOG, with both White March expansions) for `[...]` and `<...>` markup.
Counts are occurrences across `DefaultText` + `FemaleText`.

**Token list verified against decompiled engine code (2026-07-05):**
`Assembly-CSharp\Conversation.cs` (~lines 551–600). Tokens with count 0 exist
in the engine but never occur in shipped dialog — still valid for mod text.

The same three mechanisms as PoE2 (see `tags-poe2.md`), with a smaller
vocabulary — no ship tokens, and essentially no rich-text markup.

Machine-readable copy: `data/tags.json` (embedded in the app for the in-app
tag reference window; keep the two in sync when either changes).

---

## 1. Substitution tokens

| Token | Count | Replaced with |
|---|---|---|
| `[Player Name]` | 257 | The Watcher's name |
| `[Player Race]` | 83 | The Watcher's race |
| `[Player Deity]` | 2 | The priest player's chosen deity |
| `[Player Subrace]` | 0 | The Watcher's subrace (engine-only) |
| `[Player Class]` | 0 | The Watcher's class, gendered form (engine-only) |
| `[Player Culture]` | 0 | The Watcher's culture (engine-only) |
| `[Player Paladin Order]` | 0 | The paladin player's order, gendered form (engine-only) |
| `[NPCBacker]` | 0 | Backer NPC description text (engine-only) |
| `[Specified 0]`, `[Specified 1]` | 409 / 13 | The character bound to the node's Specified Speaker slot *n* |
| `[SkillCheck 0]` … `[SkillCheck 3]` | 101 / 33 / 14 / 7 | The party member selected by skill-check node *n* |
| `[Slot 0]` … `[Slot 5]` | 132 / 118 / 118 / 115 / 115 / 115 | The character in party slot *n* (used heavily in party banter) |

**No lowercase variants:** unlike PoE2, PoE1 has no `[player race]`-style
lowercase pairs — a lowercase token renders literally. Matching is exact-case.

PoE2-only tokens (`[Player Ship]`, `[Player Background]`,
`[Player Animal Companion]`, `[Interaction Ability]`, `[God_Boon]`,
`[ShipDuel_*]`) do not exist here. `[Temp]` is appended by the engine to nodes
with missing text — a developer marker, not a token to use.

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
"[Honest]") are **not** in the text — the engine builds them from node data
(confirmed: `Conversation.cs` ~lines 441–452 assembles `[Stat: Value]`
prefixes from the node's checks).
