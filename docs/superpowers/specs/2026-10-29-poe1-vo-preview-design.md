# PoE1 voice-over preview — design

Issue: [#4](https://github.com/kjmikkel/PillarsDialogEditor/issues/4), step 1 of the investigation comment (read-only
preview). The owner asked for this to be built unattended and reviewed as a PR, so choices not taken from the
investigation are **my call** (the implementing agent's), marked _(my call)_.

## 1. Intent

PoE2 writers can hear a node's line in Node Details; PoE1 writers cannot. The investigation on #4 showed PoE1 voice
is fully readable: each line is a Unity AudioClip in `PillarsOfEternity_Data/resources.assets`, named
`<conversation>_<node:0000>` (`_fem` for the English female variant), whose audio is one FSB5 bank in
`resources.resource` at the offset/size the clip record gives. The bundled `vgmstream-cli` decodes those banks.

**Goal:** when a PoE1 game folder is open, a node that has a shipped voice line shows the same "voice-over found"
status and Play (and Play female) controls PoE2 nodes get, and playing works.

**Non-goals:** importing, replacing or adding PoE1 voice (steps 2–3 on #4); VO validation and batch VO rows for
PoE1 (they stay PoE2-only); languages other than English (PoE1 ships English voice only).

**Anti-goals:** writing anything to the game folder; a wrong clip for a node; a slow or frozen UI while indexing.

## 2. How the game names clips (from the decompiled game, see #4)

`GameResources.GetDialogueAudio(conversationFile, nodeId, female)` looks up
`<conversation file name without extension>_<nodeId:0000>`, plus `_fem` only when the language is English and
the female text exists. The `VOAsset` it finds names the AudioClip to play by Resources path (`VOClip.clip`).

The editor follows the same route (issue [#355](https://github.com/kjmikkel/PillarsDialogEditor/issues/355)):
the ResourceManager container in `globalgamemanagers` maps
`audio/vocalization/vo assets/<conversation>/<name>` to the VOAsset (a MonoBehaviour in `resources.assets` with no
type tree: `Poe1VoAssetRecord` hard-codes its 2019.4 layout), and the VOAsset's clip path, normalised as the game
does (lower case, every `data/` dropped, extension removed), maps to the AudioClip. When that route reaches a known
AudioClip it decides the line, including lines that reuse another conversation's clip. Otherwise the editor falls
back to matching the AudioClip name directly, case-insensitively. A name shared by two different real clips with no
route resolves to no voice. On the shipped game 96 names are shared; 77 resolve through their VOAsset, and the rest
(soundset barks, one empty VOAsset) are silent in the game's conversations too. _(my call: VOAssets with an empty
or unresolvable clip, 13 of them only because of an `Irensî`/`irensi` folder mismatch in the game data, keep the
name match rather than going silent.)_

Placeholder clips: the game skips clips with frequency 44,100 Hz and exactly 14,976 samples. The editor treats
those as "no voice" (sample count = round(length × frequency)).

## 3. Core: `DialogEditor.Core/Audio/`

- **Dependency:** `AssetsTools.NET` (MIT, NuGet) in `DialogEditor.Core`, used only to open the serialized file and
  enumerate objects of class 83 (AudioClip). Add it to any third-party notices file the repo keeps.
- **`Poe1AudioClipRecord`** — `record (string Name, int Frequency, float Length, string Source, long Offset, long
  Size)` with `static Poe1AudioClipRecord Parse(AssetsFileReader r)` reading the Unity 2019.4 AudioClip layout:
  `m_Name` (aligned string), `m_LoadType`, `m_Channels`, `m_Frequency`, `m_BitsPerSample` (int32 each),
  `m_Length` (float), `m_IsTrackerFormat`, `m_Ambisonic` (bool, then align), `m_SubsoundIndex` (int32),
  `m_PreloadAudioData`, `m_LoadInBackground`, `m_Legacy3D` (bool ×3, then align), `m_Resource.m_Source` (aligned
  string), `m_Offset`, `m_Size` (uint64), `m_CompressionFormat` (int32). `IsPlaceholder` per §2.
- **`Poe1VoiceIndex`**
  - `static Poe1VoiceIndex? Load(string gameRoot)` — opens `<gameRoot>/PillarsOfEternity_Data/resources.assets`.
    Returns null (after `AppLog.Warn`) when the file is missing, the Unity version is not `2019.4.*` (the layout is
    version-specific) _(my call)_, or reading fails.
  - Internal constructor taking the records and the data folder, for tests.
  - `Poe1VoiceClip? Find(string conversationName, int nodeId, bool female)`; null for placeholders and misses.
  - `string Extract(Poe1VoiceClip clip, string cacheDir)` — copies `[Offset, Offset+Size)` of the clip's source
    file (resolved next to `resources.assets`) to `<cacheDir>/<name>.<offset:x>.fsb` (two clips can share a name); reuses an existing file of the right
    size; throws `InvalidDataException` when the slice does not start with `FSB5` or runs past the file end.
- Only VO-named clips (`_\d{4}(_fem)?$`) are indexed.

## 4. ViewModels

- **`Poe1VoiceService`** (ViewModels/Services): one index per game root per session, built on a background task
  when a PoE1 game folder becomes active; raises `Ready` when built. Until then, PoE1 nodes show no VO row.
  Cache dir: `<temp>/PillarsDialogEditor/poe1-vo` _(my call: temp, not cleaned; a few hundred KB per played line)_.
- **Node Details** (`NodeDetailViewModel.NotifyAllProxies`): for `ActiveGameId == "poe1"`, build `_voCheck` from
  the service instead of `VoPathResolver.Check` — Found with the extracted primary path (and female path when
  `HasFemaleText` and a `_fem` clip exists); `null` when there is no clip (no row, not "missing": most PoE1 nodes
  are unvoiced). Skip `WithLocalVoFallback` for PoE1. Re-run on `Ready`.
- Import controls stay hidden for PoE1 (they already require PoE2; keep a test pinning it).
- `VoPathResolver.Check` is unchanged (its PoE1 → null contract keeps validation and batch rows PoE2-only).
- Extraction failure: `AppLog.Warn`, treat as no VO for that node.

## 5. Playback

`VoAudioPlayer.Play(path)` already routes non-`.wav` files through vgmstream; an `.fsb` path needs no player
change. The builder confirms this and adds a test if one is missing.

## 6. Tests

- Unit: `Poe1AudioClipRecord.Parse` over synthetic bytes; `Poe1VoiceIndex` naming (case, `_fem`, 4-digit node,
  placeholder, non-VO names skipped), `Extract` exact slice / reuse / bad magic / past end, version guard.
- GameData category (`DIALOGEDITOR_POE1_DIR`, see `docs/game-data-tests.md`): real index has ≥ 11,000 VO clips;
  `00_cv_beodmar` node 89 resolves and its slice starts with `FSB5`.
- VM: PoE1 node with a clip → `VoStatusIsFound`, `CanPlayAudio`; without → `HasVoStatus == false`; fem path only
  with female text; import hidden.

## 7. Left to the builder
Exact service lifetime wiring (where the game folder becomes active), and status text reuse vs a PoE1 wording.
