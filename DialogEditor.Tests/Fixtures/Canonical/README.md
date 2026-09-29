# Canonical conversations

One hand-written conversation per game, named `canonical`, that exercises every construct the
editor reads or writes ([#122](https://github.com/kjmikkel/PillarsDialogEditor/issues/122)). When
you need to debug or verify a conversation feature, test it against these first. They are small,
deterministic, and safe on a public CI runner.

Each folder is a **miniature game install**. `poe1/` and `poe2/` have the same layout as a real
Pillars of Eternity or Deadfire folder, so the editor, the tests and the patcher all see a normal
game.

The fixtures are written from the documented game formats: the `OEIFormats` model and the shape
of shipped files. **Nothing is copied from game data.** All text, the NPC GUID
(`c0a1c0a1-0000-4000-8000-000000000122`), global-variable names and script call hashes are
invented. The player and narrator GUIDs are engine constants. The `.wem` files are four
placeholder bytes, not audio.

## Using them

- **Tests:** `new FakePoe1Game()` and `FakePoe2Game.Canonical()` copy a fixture to a temp folder
  (see `DialogEditor.Tests/Helpers`). `CanonicalConversationTests` covers:
  - every construct listed below is present;
  - the conversation loads in both languages without a warning;
  - an unchanged save round-trips, using the same checks as the shipped-data tests
    ([docs/game-data-tests.md](../../../docs/game-data-tests.md)).
- **In the app:** copy `poe1/` or `poe2/` somewhere outside the repo, choose that copy as the game
  folder, and open `canonical` from the Conversations dock. Use a copy because the editor writes
  to its game folder (backups, Test Patch, VO sync), and those files must not end up in the
  source tree. With the `running-the-app` tooling:

  ```powershell
  Copy-Item -Recurse DialogEditor.Tests/Fixtures/Canonical/poe2 $env:TEMP/canonical-poe2
  ./tools/ui-automation/Capture-Canvas.ps1 -OutDir $env:TEMP/canonical-shots -Conversation canonical `
      -GameDirectory $env:TEMP/canonical-poe2 -Themes Dark -FontScales 1
  ```

## Node map

Both games share one graph. The node IDs, types and flow are the same, so a construct has the
same node ID in either game:

```
0 NPC ─┬─▶ 1 player ──▶ 4 script ──▶ 7 narrator bark ──▶ 10 NPC (end)
       ├─▶ 2 player ──▶ 5 bank {8, 9} ─────────────────▶ 10
       └─▶ 3 player ──▶ 6 trigger → canonical, start 0
```

| Node | Type | Constructs it covers |
|---|---|---|
| 0 | TalkNode (NPC) | Entry node. Three outgoing links: 0→1 with default link values; 0→2 with RandomWeight **2** and QuestionNodeTextDisplay **ShowAlways**; 0→3 with **ShowNever**. OnEnter script. Text with the `[Player Name]` token and a **female variant**. |
| 1 | PlayerResponseNode | Persistence **OnceEver**. PoE1 carries a **nested condition** on the node: `A or (B and not C)`. PoE2 carries it on link 1→4. PoE1: link 1→4 **omits RandomWeight and QuestionNodeTextDisplay** (#112). |
| 2 | PlayerResponseNode | Persistence **OncePerConversation**. Condition with Operator **Or**. |
| 3 | PlayerResponseNode | Persistence **MarkAsRead**. **Negated** condition. |
| 4 | ScriptNode | DisplayType **Hidden**. **OnEnter, OnExit and OnUpdate** scripts. PoE2: the OnEnter script has its own **Conditional** (#115). |
| 5 | BankNode | **PlayRandom**, children 8 and 9. No DisplayType (#113, #114). |
| 6 | TriggerConversationNode | Hands over to `canonical`, start node 0 (#113). |
| 7 | TalkNode (narrator) | DisplayType **Bark**, PlayType Random, NoPlayRandomWeight 1. Text with `<i>` **markup** and a female variant. PoE2: **HasVO**; `narrator/canonical_0007.wem` and `…_fem.wem`. |
| 8 | TalkNode in bank 5 | DisplayType **Overlay**. PoE2: **ExternalVO alias** `narrator/canonical_0007` (node 7's file). |
| 9 | TalkNode in bank 5 | **No stringtable entry**, on purpose: the missing-string case. |
| 10 | TalkNode (NPC) | End node. **Comments** and **ActorDirection** (PoE1), **HideSpeaker** (PoE2). |
| −200 | ScriptNode (PoE2 only) | The conversation-level script node. It is present as `ConversationScriptNode` and as a hidden node at the end of `Nodes`, with an OnEnter script. The editor never shows it and must carry it through untouched (#115). |

Both stringtables exist in **`en` and `de`**.

### Where the games differ

These differences are not gaps in the fixture. The fixture follows each game's own format:

| | PoE1 (XML) | PoE2 (JSON) |
|---|---|---|
| Enums | names (`Bark`, `ShowAlways`) | integers (`2`, `1`) |
| Link conditions | **don't exist**: `DialogueLink` has no `Conditionals` | yes |
| Script conditions | don't exist | per-script `Conditional` |
| Script call | bare `<ScriptCall>`, no `xsi:type` (#111) | `Data` with `FunctionHash`/`ParameterHash` |
| Voice-over | `VOFilename` (empty here) | `HasVO` / `ExternalVO` + `.wem` files |
| Stringtable entry | `Language`, `ID`, `DefaultText`, `FemaleText`, `GenderNeutralText` | `ID`, `DefaultText`, `FemaleText` |

## Known gaps

Unchanged saves of both fixtures are exact. Writing up the PoE1 fixture turned up three bugs.
They only appear once a link is **edited**, so an unchanged save can't catch them;
[#138](https://github.com/kjmikkel/PillarsDialogEditor/issues/138) (canonical mods) is meant to:

- [#139](https://github.com/kjmikkel/PillarsDialogEditor/issues/139): PoE1 QuestionNodeTextDisplay is saved as `Always`/`Never`, which the game can't load.
- [#140](https://github.com/kjmikkel/PillarsDialogEditor/issues/140): Random Weight accepts fractions, but both games store an integer.
- [#141](https://github.com/kjmikkel/PillarsDialogEditor/issues/141): PoE1 link conditions are editable and saved, but PoE1 links have none.

## Changing a fixture

- Keep the node map above and `CanonicalConversationTests` in step. The coverage tests fail if a
  construct disappears.
- Follow the property/element order of shipped files. The PoE2 round-trip test requires identical
  JSON after an unchanged save, so an order the game never writes shows up as a false failure.
- Keep every value original. The repository is public.
