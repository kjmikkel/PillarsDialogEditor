# PoE2 DLC conversations (issue 198)

## Problem

PoE2 keeps each DLC's data in its own folder beside the base game's, under
`PillarsOfEternityII_Data`: `exported` for the base game, and `lax1_exported`, `lax2_exported`,
`laxi_exported` and so on for the DLC. The editor only reads `exported`, so the 273 DLC
conversations on a fully installed game never reach it:

| Folder | Count |
|---|---|
| `lax1_exported` | 79 |
| `lax2_exported` | 97 |
| `lax3_exported` | 86 |
| `laxd_exported` | 7 |
| `laxi_exported` | 3 |
| `laxa_exported` | 1 |

None share a name with a base-game conversation. The README's PoE2 rows (1,130 conversations
"with all DLC") and its DLC note are wrong for PoE2. PoE1 is unaffected: its White March folders
sit inside the normal conversations folder.

## What the game does

The decompiled `ProductConfiguration` lists the game's data folders and, in the same order, their
file prefixes:

```
exported, bb_exported, lax1_exported, lax2_exported, lax3_exported, lax4_exported,
laxa_exported, laxb_exported, laxc_exported, laxd_exported, unused_exported,
laxe_exported, laxf_exported, laxg_exported, laxh_exported, laxi_exported
```

Each one mirrors the base layout:
- `design/conversations`, `design/gamedata`, `design/globalvariables` and `design/quests`;
- `localized/<lang>/text/conversations/...`, with the same relative path as the conversation;
- a `manifest.json`.

## Goals

1. **Reading.** On a PoE2 install, every conversation in every data folder that exists is listed,
   opens, and shows:
   - its text in every installed language, from its own folder's `localized`;
   - speaker names, from every folder's `gamedata`;
   - condition and script lookups (global variables, quests, game data) that include DLC entries.
2. **Writing.** Test Patch (F5), Restore (F6), the Patch Manager and the CLI patcher edit a DLC
   conversation in place, in its own data folder, as they do a base conversation today. The same
   backup rules apply, and string tables go to that folder's `localized`. New conversations
   still go to the base `exported` folder, with their manifest as today.
3. **Voice-over.** Playing, importing and exporting VO works for DLC lines. VO is one shared pool
   of hashed `.wem` files. The plan's first task confirms how a DLC conversation's lines map into
   it, and adjusts `VoPathResolver`, `VoAliasIndexService` and `VoPackExporter` only if the
   mapping differs from the base game's.
4. **The dock groups DLC content by title** (approved mapping):

   | Key | Title |
   |---|---|
   | lax1 | Seeker, Slayer, Survivor |
   | lax2 | Beast of Winter |
   | lax3 | The Forgotten Sanctum |
   | laxa | Rum Runner's Pack |
   | laxb | Beard and Hair Pack |
   | laxc | Scalawags Pack |
   | laxd | Deck of Many Things Pack |
   | laxe | Critical Role Pack |
   | laxf | Mega Bosses (laxf1 The Crystal Empress, laxf2 The Voracious Mountain, laxf3 Sigilmaster Auranic) |
   | laxg | Mega Boss – The Hellfire Colossus |
   | laxi | Woedica's Book |

   - **A conversation's group comes from:**
     - the data folder it was read from;
     - for base-game folders, the DLC prefix in their name (`lax02_01_iceberg_dungeon` and `laxi`).
       This is the user's choice: writers think in DLCs, not in which folder the game put a file.
   - **Prefix spelling:** folder names use two-digit numbers (`lax02_`), and data folders use one
     digit (`lax2_`); both map to the same key.
   - **The mega bosses:** their data folders (`laxf_`, `laxg_`) hold no conversations, so they only
     need a title if that changes. `laxf` covers three bosses, so its group is "Mega Bosses".
   - **Unknown keys:** `bb_`, `lax4_`, `unused_` and `laxh_` have no title. Content from one of them
     is grouped under its raw key, tidied, rather than mislabelled or hidden.
   - **Order:** DLC groups follow the base game's folders in the order of the table above, then the
     prototype and test group (#85 part D).
   - **Inside a group:** a DLC folder's own folder structure nests as usual, and `lax`-prefixed
     folder names drop the prefix (as `px1_` does for PoE1).
5. **Documentation.** The README's PoE2 rows give the real counts, and its DLC note describes both
   games correctly.

## Non-goals

- No change for PoE1, which already reads its DLC.
- No new-conversation support inside a DLC folder. New conversations stay in the base game, which
  loads them through their manifest.
- No DLC ownership checks. A data folder that isn't installed simply isn't there, as today.

## Design

### Data roots (DialogEditor.Core)

- **`Poe2DataRoots`:** a pure helper.
  - It holds the game's ordered folder list (above, with a comment citing `ProductConfiguration`).
  - It returns the roots that exist under a game directory.
  - It maps a file path back to the root that contains it.
- **`ConversationFile`** gains `DataRoot` (the `*_exported` folder name). It defaults to `exported`
  for PoE1 and existing callers, so serialized projects and patches don't change: a project names
  conversations by name, and names are unique across folders (verified above). The tests pin that
  uniqueness against the shipped list.
- **`Poe2GameDataProvider`:**
  - `EnumerateConversations` walks every root's `design/conversations`, with `FolderPath` relative
    to that root's conversations folder;
  - `StringTablePathFor`, speakers, global variables, quests and game data each resolve per root.
    Lookups merge every root's entries, base first, so a DLC entry with the same key wins, matching
    the game's later-folder-wins order. The plan verifies that rule in the decompiled loader, and a
    test pins whatever the plan finds.
- **Saving:** writes through `ConversationFile.ConversationPath` and the per-root string table
  path, so the existing backup and restore code needs no new branch beyond the path. The plan
  checks every place that builds a PoE2 path from `ExportedRoot` (`VoPackExporter`,
  `VoPathResolver`, `VoAliasIndexService`, `GameDataProviderFactory`, the patch delivery paths).

### Dock grouping (DialogEditor.ViewModels)

- **`ConversationTreeBuilder` takes a game.**
  - Its White March rule becomes one table of DLC groups per game:
    - PoE1: `px1`, `px2`;
    - PoE2: the table above, with the PoE2 `lax` key normalisation.
  - For PoE2, a conversation's group key comes from its `DataRoot`. For base-root files, it comes
    from a `lax…` prefix on its top-level folder.
  - Group display names are localised strings, `Browser_Dlc_<Key>`.

### Strings

- New `Browser_Dlc_*` entries for each PoE2 title.
- The existing White March strings keep their keys.

## Testing (TDD)

- **`Poe2DataRootsTests`:**
  - order;
  - only existing roots returned;
  - path-to-root mapping, including a root-name prefix trap (`lax1_exported` vs `lax10_…`).
- **Provider tests,** on a temp game folder with an `exported` and a `lax2_exported` root holding
  one conversation and string tables each:
  - both are listed, with the right `DataRoot` and `FolderPath`;
  - the DLC conversation loads its text in two languages;
  - DLC speaker names resolve;
  - saving the DLC conversation writes in `lax2_exported` and nowhere else.
- **`ConversationTreeBuilderTests`:**
  - PoE2 grouping by data root and by folder prefix (`lax02_01_iceberg_dungeon` → Beast of Winter;
    base `laxi` → Woedica's Book);
  - unknown keys tidied;
  - group order;
  - PoE1 unchanged.
- **Real-install tests** (`Category=GameData`, opt-in via `DIALOGEDITOR_POE2_DIR`):
  - every PoE2 conversation (1,403 on a full install) loads with text;
  - names are unique across roots;
  - a DLC conversation round-trips through save.
- **In-game check:** Test Patch on a DLC conversation (a `lax2` line), seen in game, then Restore.
  This is manual, recorded in the PR.

## Open questions for the plan (answered by reading code, not by the user)

1. **Later-folder-wins:** do same-key entries across roots override in the game's folder order,
   for gamedata, speakers and global variables?
2. **VO:** what is the hash input for a DLC line's `.wem`, and does it differ from the base game's?
3. **Stale backups:** does the backup or restore manifest assume `exported` anywhere?
