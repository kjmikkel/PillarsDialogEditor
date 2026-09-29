# Game-data fidelity tests

`DialogEditor.Tests/GameData/ShippedConversationRoundTripTests.cs` checks the editor against a
real game install. Every shipped conversation and stringtable goes through an **unchanged save**,
using the same serializers that Test Patch (F5), `dialog-patcher` and the Patch Manager use. It
must come back intact. These tests found the save-fidelity bugs #111–#116, and they are the
regression net for them ([#117](https://github.com/kjmikkel/PillarsDialogEditor/issues/117)).

They are **opt-in**. Unless you point them at an install, they are reported as *skipped*, never
failed. They carry `Category=GameData`, and CI filters that category out, because game data must
never be put on a public runner.

## Running them

Set one or both variables to the game's install folder (the folder you pick in the editor), then
run the category:

```powershell
$env:DIALOGEDITOR_POE1_DIR = 'D:\GOG Games\Pillars of Eternity'
$env:DIALOGEDITOR_POE2_DIR = 'D:\GOG Games\Pillars of Eternity II Deadfire'
dotnet test DialogEditor.Tests --filter "Category=GameData"
```

It takes about 20 seconds for both games. **Nothing is written to the install.** Every save
happens in memory, and the result is only compared.

## What is checked

| Test | PoE1 | PoE2 |
|---|---|---|
| The editor's model of every conversation is identical after the save | ✅ | ✅ |
| The saved file matches the original | equivalent **in the game's own model** | **identical JSON** |
| Every stringtable in every installed language keeps every entry | ✅ | ✅ |

- **PoE1, game-model equivalence.** The original and the saved XML are both deserialized with
  `XmlSerializer` over `OEIFormats.ConversationData`, loaded at run time from the install's
  `PillarsOfEternity_Data/Managed/OEIFormats.dll`, and serialized again. Formatting differences
  (`<X />` vs `<X></X>`) and elements the game ignores drop out. Anything the game would load
  differently fails. The DLL is never referenced or redistributed by this repository.
- **PoE2, identical JSON.** The game's bundle parser needs Unity, so there's no game-loader check.
  Exact equality of the whole document is stricter anyway.
- **Stringtables** are written back through `StringTableSerializer.SerializeTranslations`, the
  code path F5 and the patcher use, and compared entry by entry. A file in the install that isn't
  XML at all is listed in the test output and skipped, not failed. A GOG Deadfire install has one
  such file ([#119](https://github.com/kjmikkel/PillarsDialogEditor/issues/119)).

There is no allow-list. On failure, the test reports how many files failed and what differs in the
first 25 of them.

## The canonical conversations: the same checks, no install needed

For a small, deterministic baseline that runs on every `dotnet test`, CI included, use the
canonical conversations in
[`DialogEditor.Tests/Fixtures/Canonical`](../DialogEditor.Tests/Fixtures/Canonical/README.md)
([#122](https://github.com/kjmikkel/PillarsDialogEditor/issues/122)). There is one per game,
written to cover every construct, and each is laid out as a miniature install.
`CanonicalConversationTests` runs the checks above on them through the shared
`Helpers/RoundTripChecks`. The one exception is the PoE1 game-model check. It needs
`OEIFormats.dll`, so it still waits for `DIALOGEDITOR_POE1_DIR`.

## When to run them

- Before every release, as part of the release checklist.
- After any change to a parser or serializer in `DialogEditor.Core/Parsing` or
  `DialogEditor.Core/Serialization`.

Last verified passing on 2026-09-28 against:

- Pillars of Eternity, GOG and Epic (1,434 conversations, 8 languages);
- Deadfire, GOG (1,130 conversations, 10 languages).
