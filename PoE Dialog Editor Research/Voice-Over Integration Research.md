---
tags: [research, audio, voice-over, poe1, poe2]
status: third-pass
date: 2026-06-21
updated: 2026-06-27
---

# Voice-Over Integration Research

Research into how Pillars of Eternity 1 and 2 store, reference, and play voiced dialogue lines — to assess feasibility of path validation and audio preview in the Dialog Editor.

---

## TL;DR — Feasibility Summary

| Feature | PoE2 | PoE1 |
|---|---|---|
| Locate a VO file for a given node | ✅ Deterministic formula | ⚠️ Unity asset bundle required |
| Validate that a VO file exists | ✅ Direct filesystem check | ⚠️ Only if unpacked |
| Preview audio in the editor | ✅ With a .wem decoder | ⚠️ Would need asset extraction |
| Know at edit time that VO is missing | ✅ Yes | ⚠️ Partial |

**Bottom line:** PoE2 VO integration (locate + validate + preview) is highly feasible. PoE1 is blocked on Unity asset packaging — audio is not stored as loose files.

---

## Audio Middleware

**PoE1** uses **Unity's native audio pipeline** with a custom `VOAsset`/`VOBankClip` layer on top. There is no Wwise in PoE1. Audio assets are compiled into Unity `Resources` archives and loaded via `Resources.LoadAll<VOAsset>()`.

**PoE2** uses **Audiokinetic Wwise** (`AkSoundEngine`, `AkBankManager`, `AkEvent`). All audio goes through Wwise. Voice-over lines are streamed as external source files (`.wem`) rather than being embedded in `.bnk` SoundBanks.

> [!important]
> The switch from Unity audio (PoE1) to Wwise (PoE2) is the single biggest factor in feasibility. Wwise's external-source architecture means VO files are **loose on disk**, directly addressable by path — a lucky design decision from the editor's perspective.

---

## File Formats

| Format | Game | Role |
|---|---|---|
| `.bnk` | PoE2 | Wwise SoundBanks — SFX and music. **Not used for story VO.** |
| `.wem` | PoE2 | Wwise Encoded Media — all voice-over lines, stored loose on disk. Vorbis codec (idCodec = 4, RIFF tag 0xFFFF). **48000 Hz, mono for story dialogue.** |
| Unity `VOAsset` | PoE1 | ScriptableObject wrapping a `VOBankClip` (Unity `AudioClip`). Packed inside Unity `Resources`. |

PoE1 does **not** use `.wem` files. The audio is inside Unity's compiled asset archive.

---

## How VO Is Linked to Dialogue Nodes

The link is **purely by naming convention** — neither game embeds an audio path directly in the conversation node. Instead, the engine computes the path at runtime from:

1. The **conversation filename** (without extension)
2. The **NodeID** (the integer on each `FlowChartNode`)
3. The **speaker's name/prefix**

The NodeID is the shared key across all three assets: the conversation file, the stringtable, and the audio file. This is not documented officially — it was reverse-engineered by the modding community and confirmed in the source code.

### PoE1 VO Lookup

Source: `GameResources.cs`, line 554–582

```csharp
// Asset name formula:
string assetName = conversationName + "_" + nodeId.ToString("0000").ToLowerInvariant();
// e.g.: "14_cv_iovara_0073"

// Unity Resources path:
string resourcePath = "Audio/Vocalization/VO Assets/" + conversationName + "/" + assetName;
// e.g.: "Audio/Vocalization/VO Assets/14_cv_iovara/14_cv_iovara_0073"

// Female variant (English only):
assetName += "_fem";  // "14_cv_iovara_0073_fem"
```

The engine bulk-loads all `VOAsset[]` for a conversation at once (`LoadDialogueAudio`), caches them, then matches by asset name when a node plays (`GetDialogueAudio`). This means the file granularity is per-conversation, not per-node.

**The Unity `VOAsset` has no path we can check from outside the game** — it is a compiled Unity ScriptableObject.

### PoE2 VO Lookup

Source: `ConversationAudioManager.cs`, line 186–200

```csharp
// Story dialogue VO path formula:
string path = "Voices" + sep + "English(US)" + sep
            + speakerName.ToLowerInvariant() + sep
            + conversationName.ToLowerInvariant() + "_" + nodeId.ToString("0000");
// e.g.: "Voices\English(US)\iovara\14_cv_iovara_0073"

// Female variant (checks for existence first, falls back to male):
if (useFemaleVersion && FileExists(path + "_fem.wem"))
    return path + "_fem.wem";
return path + ".wem";
```

**Full absolute path on disk:**
```
{GameRoot}\PillarsOfEternityII_Data\StreamingAssets\Audio\Windows\Voices\English(US)\
    {speakerName}\{conversationName}_{nodeId:0000}.wem
```

**ExternalVO override:** `TalkNode.ExternalVO` (a string field in PoE2's `TalkNode`) can override the entire path:
```csharp
if (!string.IsNullOrEmpty(externalVO))
    path = "Voices" + sep + "English(US)" + sep + externalVO;
```
This is used for special cases where a line needs a non-standard path.

---

## PoE2 Chatter vs. Story VO

There are **two separate naming conventions** in PoE2. Only story dialogue VO is directly addressable from the editor; chatter is unrelated to the conversation file.

### Story dialogue VO (relevant to the editor)
```
Voices\English(US)\{speakerName}\{conversationFileName}_{nodeId:0000}.wem
```
Source: `GetConversationVOPath()` — called by `Conversation.PlayVO()`.

### Chatter / ambient banter VO (not relevant to the editor)
```
Voices\English(US)\{audioFileName}.wem
```
Source: `GetChatterVOPath()` — called by the chatter system, independent of conversation nodes. The `{audioFileName}` often follows the pattern `{speakerPrefix}\ch_{speakerPrefix}_{eventType}_{index}` but this is a different pipeline entirely.

---

## VO Playback Timing

Both games use the **same 0.4-second inter-line gap** after VO ends:

| Game | Timing | Source |
|---|---|---|
| PoE1 | `clip.length + 0.4f` | `Conversation.cs:258` — `OnVOClipLoaded` callback sets timer |
| PoE2 | `fDuration * 0.001f + 0.4f` | `ConversationAudioManager.cs` — `VODurationCallback` from `AK_Duration` Wwise callback |

PoE2 receives duration via a Wwise event callback (`AkCallbackType.AK_Duration`), so it doesn't need to load the file to know its length.

---

## Speaker Name Resolution (PoE2)

The `speakerName` in the VO path comes from the **speaker game data**, not from the conversation node. In the `SpeakerComponent` attached to an NPC's `speakers.gamedatabundle`:
- `ChatterPrefix` — the exact folder name used for VO files on disk (e.g., `"eder"`, `"pallegina"`, `"fd_mirke"`)
- `Gender` — `"Male"` or `"Female"`; used at runtime to decide whether to try the `_fem` variant

**Confirmed:** `ChatterPrefix` is the verbatim directory name under `Voices\English(US)\`. It is already lowercase for all shipped speakers. It can contain spaces (e.g., `"03 cartographer kid"`) or underscores. The `.ToLowerInvariant()` call in the game code is a defensive no-op for the shipped data.

**`ChatterPrefix` is NOT currently parsed by `Poe2SpeakerNameParser`.** That parser reads only `ID` and `DebugName` from the top-level `GameDataObjects` array. The `ChatterPrefix` field lives one level deeper, inside `Components[0]`:

```json
{
  "$type": "Game.GameData.SpeakerGameData, Assembly-CSharp",
  "DebugName": "SPK_Companion_Eder",
  "ID": "9c5f12c9-e93d-4952-9f1a-726c9498f8fb",
  "Components": [
    {
      "$type": "Game.GameData.SpeakerComponent, Assembly-CSharp",
      "Gender": "Male",
      "ChatterFile": "faf94d62-...",
      "ExternalChatterVOID": "00000000-...",
      "ChatterPrefix": "eder",
      "WwiseChatterEventOverride": "",
      "WwiseChatterVoiceOverride": ""
    }
  ]
}
```

The `speakers.gamedatabundle` on disk (GOG install) contains **1002 speaker entries**, of which **976 have a non-empty `ChatterPrefix`**. Of those, **771 have a corresponding folder on disk** under `Voices\English(US)\` — many NPC entries have no story VO files (chatter-only or unused).

### Special cases

| Speaker | `SpeakerGuid` | `ChatterPrefix` | VO folder |
|---|---|---|---|
| Narrator | `6a99a109-0000-0000-0000-000000000000` | hardcoded `"narrator"` in `Conversation.cs:342` — NOT from the bundle | `narrator\` ✅ |
| Player/Watcher | `b1a8e901-0000-0000-0000-000000000000` | `"Player"` in the bundle | no `player\` folder — player is silent |

The narrator's `ChatterPrefix` is actually `"narrator"` in the bundle too (confirmed), so a bundle lookup would give the same answer — but the game hardcodes it rather than looking it up.

### `_fem` variant logic

The `_fem` variant (e.g., `eder\00_cv_himuihi_0185_fem.wem`) is NOT gated by the **speaker's** gender. It is used when:
1. The **player character** is female (`StringTableManager.PlayerGender == Female`), AND
2. A female-text entry exists for that node in the stringtable (`StringTableManager.FemaleVersionExists`)

Source: `GameResources.ShouldUseFemaleVersion()` → `ConversationAudioManager.PlayConversationVO(... useFemaleVersion ...)`.

For the editor, the practical approach is: check `File.Exists(path.wem)` for the main line, and additionally check `File.Exists(path_fem.wem)` to report whether a female variant also exists — without trying to predict which will play at runtime.

### Runtime resolution chain (game)

At runtime: `TalkNode.SpeakerGuid` → find NPC `GameObject` by name → `CharacterStats.Speaker` (`SpeakerGameData`) → `.ChatterPrefix`.

For the editor (static): `TalkNode.SpeakerGuid` → look up in `speakers.gamedatabundle` → `Components[0].ChatterPrefix`. Requires extending `Poe2SpeakerNameParser` to also read the `Components` array.

> [!note]
> `GameDataNameService` already holds speaker entries (for GUID autocomplete), but only `DebugName`-derived display names — not `ChatterPrefix`. A new `GetChatterPrefix(Guid)` lookup (a `Dictionary<string, string>` keyed by speaker ID) would be a small addition to the parser and a new entry in `Poe2GameDataProvider.LoadGameDataNames()`.

---

## PoE1 — Why It's Blocked

PoE1 audio is loaded via Unity's `Resources.LoadAll<VOAsset>()` system. This means:

1. Assets are packed inside Unity's compiled `resources.assets` / `.unity3d` archive files.
2. There is no path on disk you can point to for a single node's audio.
3. The only way to get at them is with a Unity asset extraction tool (e.g., `AssetStudio`, `uTinyRipper`, `UnityPy`).
4. Even if extracted, the output is a Unity `AudioClip` serialized object, not a standard audio file — you'd need to convert it.

**VO lines do exist as loose `.ogg` files in some PoE1 installations** (the community tool `vmilea/PoE-dlg` references scanning a `vocalization\vo wav files\` folder), but this depends on whether the game was installed with the unpacked data — not guaranteed.

**PoE1 VO preview is deferred** until a strategy for Unity asset extraction is designed. This is independent of the PoE2 work.

---

## Community Tools

| Tool | Purpose | Notes |
|---|---|---|
| **ww2ogg** + **revorb** | `.wem` → `.ogg` conversion | Requires `packed_codebooks_aoTuV_603.bin`. CPU-only, no licence needed. |
| **vgmstream** / `vgmstream-cli` | Decode and play `.wem` directly | Supports many Wwise variants. Can output to PCM. Best option for in-editor preview. |
| **BnkExtract** / **bnkextr** | Unpack `.bnk` → numbered `.wem` | For SFX/music banks. Not needed for story VO (loose files). |
| **RingingBloom** | Edit `.bnk` files for Deadfire | Obsidian forums 2023+. Handles the size-constraint problem. |
| **wwiser** (`bnnm/wwiser`) | Parse `.bnk` HIRC chunks | Useful for understanding event → `.wem` mapping. Not needed for story VO. |
| **vmilea/PoE-dlg** | Parse PoE1 `.conversation` XML + play VO | Its `ResourceLocator.cs` is the reference implementation for PoE1 audio lookup. |
| **AssetStudio** / **UnityPy** | Extract assets from Unity archives | Would be needed for PoE1 VO extraction — not yet investigated. |

---

## Implementation Path for PoE2 VO Preview

Given that:
- VO files are loose `.wem` on disk at a deterministic path (confirmed against GOG install)
- The editor already has the NodeID and the game root folder path
- `ExternalVO` and `HasVO` are already parsed and persisted in `ConversationNode`
- `speakers.gamedatabundle` is already read by `Poe2GameDataProvider`; `ChatterPrefix` is one parser change away

A VO preview feature for PoE2 would require:

1. **Parser extension** — extend `Poe2SpeakerNameParser` (or add `Poe2ChatterPrefixParser`) to also read `Components[0].ChatterPrefix` and `Components[0].Gender` from `speakers.gamedatabundle`. Register the result as a new `"ChatterPrefix"` kind in `GameDataNameService`, or as a separate `Dictionary<string, string>` keyed by speaker ID GUID.

2. **Narrator special case** — when `node.SpeakerGuid == "6a99a109-0000-0000-0000-000000000000"`, use `"narrator"` as the prefix directly (mirrors game's hardcoded string in `Conversation.cs:342`).

3. **Path construction** — given `chatterPrefix` (from step 1/2), `conversationName` (from the open conversation file), `nodeId`, and game root:
   ```
   {GameRoot}\PillarsOfEternityII_Data\StreamingAssets\Audio\Windows\Voices\English(US)\
       {chatterPrefix.ToLowerInvariant()}\{conversationName.ToLowerInvariant()}_{nodeId:0000}.wem
   ```

4. **`ExternalVO` override** — if `node.ExternalVO` is non-empty, substitute it for the `{chatterPrefix}\{conversationName}_{nodeId:0000}` portion:
   ```
   {GameRoot}\PillarsOfEternityII_Data\StreamingAssets\Audio\Windows\Voices\English(US)\{externalVO}.wem
   ```
   (The game appends `.wem`; if `ExternalVO` already ends in `.wem`, this would double it — check the game's `GetConversationVOPath` source: it does NOT strip `.wem` from `externalVO`, so the stored value must be the bare path without extension.)

5. **Existence check** — `File.Exists(path + ".wem")` for the primary line, and optionally `File.Exists(path + "_fem.wem")` to show whether a female variant exists. This alone gives path validation even without playback.

6. **Playback** — shell out to `vgmstream-cli` (if bundled or discovered) or convert via `ww2ogg` + NAudio. The cleanest option is `vgmstream-cli` piped to NAudio's WaveStream.

> [!warning]
> Bundling a `.wem` decoder (vgmstream-cli or ww2ogg) would add a dependency and potentially a binary to the distribution. This is a packaging and licensing decision that needs resolving before implementation.

> [!note]
> Steps 1–5 (parser + existence check) can ship as a standalone **"VO path validation"** feature with no external dependencies. Playback (step 6) is a separate, additive concern.

---

## Confirmed .wem Audio Format (from binary inspection)

Inspected 11 story VO `.wem` files across multiple speakers and conversations (GOG install, 2026-06-27).

| Property | Value | Notes |
|---|---|---|
| Container | RIFF/WAVE | Standard RIFF header |
| RIFF codec tag | `0xFFFF` (65535) | Wwise's RIFF-level tag for Vorbis |
| AkCodecID (game source) | `4` | Used in `AkExternalSourceInfo`; maps to Vorbis |
| Sample rate | **48000 Hz** | Consistent across all inspected files |
| Channels | **1 (mono)** for story dialogue | Narrator endgame slides and player chatter use 2 (stereo) |
| fmt chunk size | 66 bytes | 18 standard + 48 Wwise-extended bytes |
| Wwise bank version | **120** | From `BKHD` section of `voice.bnk` and `abl_acid.bnk` — consistent across all banks |

> [!important]
> **Sample rate is 48000 Hz, not 44100 Hz.** Encoding new VO at 44100 Hz would cause the game to pitch-shift audio during playback. Mod authors must target 48000 Hz.

> [!important]
> **Wwise bank version 120 = Wwise 2017.1.x.** The game's runtime cannot load banks compiled with a newer Wwise SDK. For mod `.bnk` authoring (SFX/music mods), the author must use Wwise ≤ 2017.1.x. Story dialogue VO does **not** require a `.bnk` at all — see below.

---

## Mod VO Authoring Pipeline

This section describes the pipeline for mod authors who want to ship custom voice-over for new dialogue written in the Dialog Editor.

### Key insight: story dialogue VO requires no `.bnk`

Story VO is loaded via Wwise external sources (`AkExternalSourceInfo`) — the game resolves the path at runtime and streams the file directly. No `.bnk` is needed to register new story dialogue events. A mod only needs to place correctly named and encoded `.wem` files at the right path.

The `.bnk` is only required for **chatter/ambient events** that must be registered with the Wwise event system. For story dialogue, skip it.

### Pipeline for story dialogue VO

```
1. Record or produce audio as .wav (or any lossless/uncompressed format)
2. Convert .wav → .wem  (see encoding options below)
3. Name the file:  {conversationName}_{nodeId:0000}.wem
   e.g.:  my_mod_conversation_0003.wem
4. Place under mod's audio folder:
   Voices\English(US)\{speakerChatterPrefix}\{filename}.wem
5. Mod package makes this path available at game root:
   PillarsOfEternityII_Data\StreamingAssets\Audio\Windows\Voices\English(US)\...
```

The `speakerChatterPrefix` is the `ChatterPrefix` field from the speaker's entry in `speakers.gamedatabundle` (see Speaker Name Resolution section). For existing vanilla speakers this is known; for new NPC speakers introduced by a mod it must be chosen by the mod author and registered in a custom `speakers.gamedatabundle`.

### .wem encoding options

| Option | Tooling | Notes |
|---|---|---|
| **Wwise authoring** (recommended) | Wwise 2017.1.x + minimal `.wproj` | Guaranteed format compatibility. Free for indie use. Produces exact match to game's format. |
| **Wwise CLI** | `WwiseCLI.exe` with a project template | Same quality as above; Dialog Editor can invoke automatically if Wwise is installed. |
| **Third-party** | `ffmpeg` + custom Vorbis-in-RIFF packer | Brittle. Wwise's `.wem` has non-standard extended fmt chunk that simple Vorbis packers don't produce correctly. Not recommended. |

### Minimal Wwise project for encoding

A fresh Wwise project needs only:
- **Platform:** Windows
- **Conversion settings:** Vorbis, 48000 Hz, mono (for speech), quality ~6–7 (matches game's avg ~85 kbps for mono dialogue)
- **No events, banks, or hierarchy needed** for external-source VO conversion — just set the conversion settings and run the file conversion tool

The Dialog Editor can generate a ready-to-use minimal `.wproj` scaffold with the correct platform/codec settings pre-filled, so mod authors with Wwise installed can click "Generate" without understanding Wwise's project structure.

### Identifying the correct Wwise version

- Wwise BKHD version **120** = **Wwise 2017.1.x**
- For `.wem`-only encoding (no `.bnk` needed), any modern Wwise version works because `.wem` files are not versioned the same way `.bnk` files are — the Vorbis format inside is stable
- For `.bnk` authoring (chatter/SFX mods), must use **Wwise 2017.1.x** to generate version-120 banks

### Event naming convention for mods

To avoid colliding with vanilla Wwise event IDs (FNV-1 hashes of event names), mod events should use a clearly namespaced prefix:

```
\Voice\{mod_prefix}\{conversationName}_{nodeId:0000}
```

e.g.: `\Voice\mymod\my_mod_conversation_0003`

The numeric event ID = FNV-1 hash of the name string. This can be computed in C# without Wwise. Collision with vanilla is unlikely but worth namespacing defensively.

### What the Dialog Editor needs to generate

For each conversation node with a custom VO assignment:
1. The expected `.wem` file path (deterministic from speaker + conversation name + node ID)
2. A manifest of `(eventName, eventId, sourceWav, targetWem)` tuples for the encoder step
3. Optionally: a minimal `.wproj` template with correct conversion settings

---

## Open Questions

- [x] **Does the Dialog Editor's data model currently persist `TalkNode.ExternalVO`?** ✅ Yes. `ConversationNode` (`DialogEditor.Core/Models/ConversationNode.cs`) has both `ExternalVO = ""` (string) and `HasVO = false` (bool) as named parameters. `Poe2ConversationParser` reads them at lines 67–68.
- [x] **What is the exact mapping from `SpeakerGuid` → `ChatterPrefix`?** ✅ Resolved. `ChatterPrefix` lives in `Components[0]` of each `SpeakerGameData` entry in `speakers.gamedatabundle`. `Poe2SpeakerNameParser` does NOT currently read it — requires a parser extension (see implementation path above).
- [ ] Can `vgmstream-cli` be redistributed freely? (It is MIT-licensed — check binary distribution rules.)
- [ ] **Are `.wem` files encoded by Wwise 2022.1 LTS compatible with Deadfire's 2017.1.x Wwise runtime?** The `.bnk` version gate (v120) is well understood, but `.wem` Vorbis compatibility is less certain. Wwise uses packed codebooks whose format has evolved between SDK versions — if the 2022.1 encoder produces a codebook variant the game's decoder doesn't recognise, audio will be silent or crash. Counterevidence: community VO mods on Nexus Mods appear to work, suggesting cross-version encoding is viable in practice. **Must verify during implementation:** encode a test WAV with the 2022.1 template, patch it into a vanilla conversation, confirm it plays in-game before shipping the template.
- [x] **Is there a known case in shipped conversations where `ExternalVO` is populated?** ✅ Yes — extensively. **193 of 1130 conversations** contain non-empty `ExternalVO`; **1000 nodes** across the game use it. Two main patterns: (1) **cross-conversation VO reuse** — a node in conversation A uses audio originally recorded for conversation B (e.g., `03_cv_deiko` reuses lines from `03_cv_aenalys`); (2) **`sh_` shared cutscene lines** — companion reactions to a shared cutscene event stored in the speaker's own folder but named `sh_{speaker}_{cutscene}_{nodeId}`. ExternalVO values never end in `.wem` — the game appends `.wem` at path-construction time (confirmed across all 1000 occurrences).
- [ ] For PoE1: is there a subset of installations where loose `.ogg` files are present? Worth checking the GOG/Steam install to see if `vocalization\vo wav files\` exists.

---

## Source Code References

| File | Content |
|---|---|
| `PoE1 Code/Assembly-CSharp/GameResources.cs:495–582` | PoE1 VO loading: `LoadDialogueAudio`, `GetDialogueAudio`, path formula |
| `PoE1 Code/Assembly-CSharp/Conversation.cs:219–260` | PoE1 `PlayVO()` and `OnVOClipLoaded` timer |
| `PoE1 Code/Assembly-CSharp/VOAsset.cs` | PoE1 VOAsset structure (`VOBankClip`, `StringID`) |
| `PoE1 Code/OEIFormats/.../TalkNode.cs` | PoE1 TalkNode — `SpeakerGuid`, `ListenerGuid`, `ActorDirection` |
| `PoE2 Code/Assembly-CSharp/ConversationAudioManager.cs:186–214` | PoE2 `GetConversationVOPath()` and `GetChatterVOPath()` — **the path formula** |
| `PoE2 Code/Assembly-CSharp/ConversationAudioManager.cs:216–314` | Wwise event selection by `DisplayType` and `VOPositioningType` |
| `PoE2 Code/Assembly-CSharp/Onyx/GlobalAudioManager.cs:610–625` | `PlayExternalSource()` — sets up `AkExternalSourceInfo`, codec=4 (Vorbis) |
| `PoE2 Code/Assembly-CSharp/Game/Conversation.cs:322–376` | PoE2 `PlayVO()` and `VODurationCallback` timer |
| `PoE2 Code/Assembly-CSharp/Game.GameData/SpeakerGameData.cs` | `ChatterPrefix`, `Gender`, `WwiseChatterEventOverride` — proxied from `SpeakerComponent` |
| `PoE2 Code/Assembly-CSharp/Game.GameData/SpeakerComponent.cs` | `SpeakerComponent.Parse()` — reads `ChatterPrefix`, `Gender`, `ChatterFile`, `ExternalChatterVOID`, `WwiseChatterEventOverride`, `WwiseChatterVoiceOverride` from JSON |
| `PoE2 Code/Assembly-CSharp/Game/SpecialCharacterInstanceID.cs:47–53` | Special GUIDs: `NarratorGuid = 6a99a109-0000-0000-0000-000000000000`, `PlayerGuid = b1a8e901-0000-0000-0000-000000000000` |
| `PoE2 Code/Assembly-CSharp/Game/Conversation.cs:322–376` | `PlayVO()` — resolves `ChatterPrefix` via `CharacterStats.Speaker.ChatterPrefix`; narrator hardcoded to `"narrator"` at line 342 |
| `PoE2 Code/Assembly-CSharp/GameResources.cs:323–330` | `ShouldUseFemaleVersion()` — checks player gender + `StringTableManager.FemaleVersionExists`; `_fem.wem` is about **player** character gender, not the NPC speaker's gender |
| `PoE2 Code/OEIFormats/.../TalkNode.cs:37,41` | `ExternalVO` (string), `HasVO` (bool) |
| `PoE2 Code/OEIFormats/.../DialogueNode.cs:48–84` | `PlayVOAs3DSound`, `VOPositioning`, `DisplayType` |
| `PoE2 Code/OEIFormats/.../VOPositioningType.cs` | Enum: `Default`, `Force2D`, `Force3D` |
| `PoE2 Code/OEIFormats/.../DisplayType.cs` | Enum: `Hidden=0`, `Conversation=1`, `Bark=2`, `Overlay=3` |

> [!note] Note on PoE2 DisplayType enum
> The PoE2 `OEIFormats` `DisplayType` enum has **different values** from what the conversation JSON stores. In PoE2 `.conversationbundle` JSON, `DisplayType: 1` is `Conversation` (the most common), `DisplayType: 2` is `Bark`. The earlier find-longest-bark scripts used `DisplayType == 1` — but in the JSON format, `1` maps to `Conversation`, not `Bark`. The exclude-pattern filtering in those scripts compensated for this inconsistency by filename. Worth double-checking if this is used in the editor's bark detection logic.

---

## External Sources

- [vmilea/PoE-dlg — GitHub](https://github.com/vmilea/PoE-dlg) — C# PoE1 dialogue explorer; `ResourceLocator.cs` is the reference PoE1 audio lookup
- [bnnm/wwiser — GitHub](https://github.com/bnnm/wwiser) — Wwise `.bnk` parser
- [The Great Sound File Purge — Nexus Mods article](https://www.nexusmods.com/pillarsofeternity2/articles/2) — documents PoE2 `.bnk` extraction workflow
- [Custom Voice Resources — Nexus Mods mod 463](https://www.nexusmods.com/pillarsofeternity2/mods/463) — lists all 121 PoE2 voice set event type filenames
- [Adding a Custom Voice — Obsidian Forums](https://forums.obsidian.net/topic/110080-adding-a-custom-voice/)
- [RingingBloom for editing .bnk — Obsidian Forums](https://forums.obsidian.net/topic/132425-ringingbloom-for-editing-bnk-files/)
- [Game Data Formats — eternity.obsidian.net](https://eternity.obsidian.net/game-data-formats/concepts)
