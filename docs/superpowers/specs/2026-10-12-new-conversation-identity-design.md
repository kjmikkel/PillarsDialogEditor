# New conversations the games can start — design

GitHub issue 162. Found by the canonical mods (issue 138).

## Goal

A conversation made with **File › New Conversation** (or imported), then installed by
Test Patch, `dialog-patcher` or the Patch Manager, can be started in the game from its
first line:

- in Deadfire, by name, from a script or the console;
- in Pillars of Eternity, by path, at the default start node.

Removing the mod (F6, "Remove all mods", `dialog-patcher --restore`) leaves no trace of it.

## What the games need

Taken from the decompiled game code.

**Deadfire** (`ConversationDataManager`):
1. **Finding the conversation.** At start-up the game reads every
   `design/manifests/*conversations.manifest`. Each entry maps a conversation `ID` (GUID)
   to a `Filename` relative to `design/`.
2. **Name to ID.** `FindConversationIDByName(name)` returns the ID of the entry whose
   `Filename`, minus folder and extension, equals `name` (case-insensitive).
3. **ID to data.** `LoadConversationData(id)` loads the bundle at that `Filename` and uses
   the conversation in it whose `ID` equals `id`. A bundle without a matching `ID` gives
   nothing.
4. **Text.** The conversation's text comes from a stringtable path derived from the
   bundle's inner `Filename`: the part after `conversations/`, with `.stringtable`, under
   `localized/<lang>/text/conversations` (`Conversation.GetStringTableName`, issue 138).
5. **The start node.** `StartConversation(…, nodeID = 0)` starts at node 0 unless told
   otherwise.

**Pillars of Eternity:** conversations are found by path, so there is no manifest.
`ConversationManager.StartConversation` also defaults to node 0.

## What the editor does today

| | Deadfire | Pillars of Eternity |
|---|---|---|
| New conversation file | `{"Conversations":[{"Nodes":[]}]}`: no `ID`, no `Filename` | `<NextNodeID>0</NextNodeID><Nodes />` |
| Registered with the game | no | not needed |
| First node | node 1 (`NodeIdAllocator`) | node 1 |

So in Deadfire the new conversation can't be found by name, and wouldn't load even if it
were. In both games it can't start at its first line.

## Design

### 1. Identity: a name-based ID

`NewConversationId.For(name)` returns an RFC 9562 version 5 UUID: a SHA-1 hash of a fixed
editor namespace GUID plus the conversation name, lower-cased (ordinal, UTF-8). Lower-casing
matches the game's case-insensitive name lookup.

- **Why derived, not random.** Every install first restores the originals and rebuilds new
  conversations from the template, so an ID generated at write time would change on every
  install. Save games record conversation IDs (lines already read, for example), so the ID
  must be the same on every install and every machine. Deriving it from the name achieves
  that with no change to the project format.
- **Two mods creating the same name** get the same ID. That is harmless: they already write
  the same file, and the later one in the load order wins, as for any other conversation.
- **The namespace GUID** is a constant in the code, chosen once, with a comment saying it
  must never change.

### 2. The Deadfire template

`Poe2GameDataProvider.InitializeConversationFile` writes the conversation-level shape a
shipped bundle has. Only `Nodes` stays empty for the edits to fill.

| Property | Value |
|---|---|
| `ID` | `NewConversationId.For(name)`, formatted `D` (lower-case, hyphens) |
| `Filename` | `Conversations/<name>.conversation`, so the stringtable rule (fact 4) finds `localized/<lang>/text/conversations/<name>.stringtable`, which is where the editor writes it |
| `ConversationScriptNode` | an empty `ScriptNode` with `NodeID` -200, in the shape the canonical fixture uses |
| `Nodes` | holds only the matching -200 `ScriptNode`. The serializer copies -200 nodes verbatim (issue 115), and the parser never shows them |
| `ConversationType` | `0` |
| `CharacterMappings` | `[]` |
| `ClassExtender` | `{"ExtendedProperties":["DisplayType,Conversation"]}` |
| `UObjectName` | `<name>_Base Game` |

The Pillars of Eternity template is unchanged.

### 3. Registration (Deadfire only)

**The provider.** `IGameDataProvider` gains two members:
- `string? ConversationManifestPath(string name)`: `null` for Pillars of Eternity. For
  Deadfire it is `…/exported/design/manifests/dialogeditor_<name>.conversations.manifest`.
- `void WriteConversationManifest(string name)`: writes one entry.

**The entry:**
```json
{
  "Entries": [
    {
      "$type": "OEIFormats.Tools.ManifestData+ConversationManifestEntry, OEIFormats",
      "DisplayType": 0,
      "IncludesPlayer": true,
      "ID": "<NewConversationId.For(name)>",
      "Filename": "/conversations/<name>.conversationbundle"
    }
  ]
}
```

**Why one file per conversation.**
- The `dialogeditor_` prefix means it can't collide with a shipped or DLC manifest.
- It still matches `*conversations.manifest`.
- A Test Patch of one project can't overwrite the entries of conversations the patcher
  installed from another, because they are separate files.

**Who writes it.** Both write paths write the manifest for every conversation the project
or load order marks as new (`IsNewConversation`). This doesn't depend on whether the
conversation's file already exists: once the patcher has installed the conversation, its
bundle is there and `FindConversation` finds it, but a Test Patch must still track and write
the manifest.
- **`PatchInstaller.Install`** treats the manifest path as one more target: backed up with
  `EnsureBackedUp` before writing (it didn't exist, so restore deletes it), then
  `RecordWritten`.
- **Test Patch** (`MainWindowViewModel`) treats it as one more `Track`ed path, so F6
  deletes it.

A manifest is written only when the conversation is new. Edits to existing conversations
never touch manifests.

### 4. Node 0

- **`NodeIdAllocator.Next`** returns **0** for an empty conversation. That covers File › New
  Conversation and the sample project, in both games. A conversation whose nodes were all
  deleted also restarts at 0, which is the start node it needs.
- **The Yarn and Articy importers** number from 0 instead of 1. CSV and JSON imports keep
  the IDs in the imported file.
- **Not in scope:** renumbering projects that already start at node 1, and warning about a
  conversation with no node 0. Either would be a separate issue.

## Testing

All tests are written first (strict TDD).

**Core and Patch:**
- `NewConversationId`:
  - the same name gives the same ID;
  - names differing only in case give the same ID;
  - different names give different IDs;
  - the ID is a valid version 5 UUID;
  - one known name gives one fixed ID, a golden value guarding against any accidental change
    to the namespace or the algorithm.
- The Deadfire template has every property in the table above. The parser reads it as an
  empty conversation. The serializer keeps `ID`, `Filename` and the -200 node through a save.
- `ConversationManifestPath` / `WriteConversationManifest`:
  - Deadfire writes the entry above;
  - Pillars of Eternity returns `null`.
- `NodeIdAllocator`: an empty list gives 0, and the existing cases stay the same.
- The Yarn and Articy importers' first node is 0.
- `PatchInstaller`:
  - installing a new conversation writes its manifest;
  - restore deletes it;
  - reinstalling writes the same ID;
  - an existing conversation gets no manifest.
- Test Patch:
  - F5 writes the manifest, and F6 deletes it;
  - F5 still tracks it when the conversation's bundle already exists (installed by the patcher).

**Canonical mods (issue 138):**
- These tests are no longer skipped:
  - `NewConversation_HasTheConversationLevelProperties` (Deadfire);
  - `NewConversation_StartsAtNode0` (both games).
- The goldens are regenerated, because the new conversation's node IDs now start at 0.
  The diff is reviewed before it is committed.
- A new raw check: the Deadfire install writes the manifest with the bundle's ID, and the
  manifest's `Filename` name matches `canonical_mod_new`.
- `ChecklistTests`' table of issues skipped elsewhere drops issue 162.
- The existing `RemoveAllMods_RestoresTheFixtureExactly` already compares the whole game
  folder with a fresh install, so a manifest left behind after restore fails it.

**In-game checklist:** the `conversation.new` rows give the real expectation, start node 0
in both games:
- **Deadfire:** `StartConversation oei_player canonical_mod_new 0`.
- **PoE1:** `StartConversationFacingListener oei_player …/canonical_mod_new.conversation 0`.

The NPC says "Hello from a conversation the mod created.", with the two choices as before.
The clean-up list loses nothing: `--restore` now removes the manifest too.

## Risks

- **Changing the namespace or the hashing later** would give every installed new
  conversation a new ID and break save games that refer to it. The golden-value test makes
  that a deliberate act.
- **Encoding.** The manifest is written as UTF-8, like the bundle. The game parses both with
  the same JSON loader as its shipped files.
- **Collision with a real game conversation's name.** File › New Conversation already
  refuses a name that `FindConversation` knows, so a new conversation never shadows a
  shipped one.
