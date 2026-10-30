# PoE1 voice-over preview — plan

Spec: `docs/superpowers/specs/2026-10-29-poe1-vo-preview-design.md`. Issue #4 (step 1). Branch
`feat/4-poe1-vo-preview`. Strict red/green TDD; headless tests only:
`dotnet test DialogEditor.Tests --filter "Category!=Gui"` (never bare `dotnet test`).

## Findings that shape the plan

- `VoAudioPlayer.Play(path)` sends every non-`.wav` path through vgmstream, so an `.fsb` file plays with no player
  change.
- In `NodeDetailViewModel`, `_voCheck is not null` is the PoE2 gate for import (`IsVoImportVisible`, `CanImportVo`,
  `ShowImportVoMenuItem`) and the VO alias UI (`HasVoAlias` neighbourhood, `CanStartVoAliasPick`). A PoE1 result
  must not open those: they get an explicit `!_voIsPoe1` guard.
- Extraction is file I/O, so it happens at **play time**, not on every selection. Selection only does a dictionary
  lookup.
- The pattern for a game-wide index built in the background is the static `VoAliasIndexService` (`Rebuild`,
  `IsReady`, `RegisterForTests`, `Clear`), started from `MainWindowViewModel` where the game folder becomes
  active (around `Detail.GameRoot = path`). PoE1 voice follows it.

## Task 1 — Core reader (`DialogEditor.Core/Audio/`)

- [x] Add NuGet `AssetsTools.NET` 3.0.5 to `DialogEditor.Core`; add an entry to `THIRD_PARTY_LICENSES.md` (MIT).
- [x] `Poe1AudioClipRecord` + `Parse(AssetsFileReader)` (spec §3), `IsPlaceholder`. Tests build the bytes with
      `AssetsFileWriter` over a `MemoryStream`.
- [x] `Poe1VoiceIndex`: internal ctor (records, data folder); `Find(conv, nodeId, female)`; `Count`;
      `Extract(clip, cacheDir)`; `static Load(gameRoot)` with the 2019.4 version guard. Tests: naming, case,
      `_fem`, placeholder, non-VO skipped, extract slice/reuse/bad magic/past end, missing file → null.
- [x] GameData test (`[GameInstallFact]` PoE1 variant — see `GameInstallFactAttribute`): ≥ 10,900 distinct names;
      `00_cv_beodmar` node 89 found; its extracted file starts `FSB5`. Writes only under a temp dir.

## Task 2 — ViewModels

- [x] `Poe1VoiceService` (static, ViewModels/Services) mirroring `VoAliasIndexService`: `Rebuild(gameRoot)`
      (background), `IsReady`, `Changed` event (raised on build/clear), `Find`, `Extract(clip)` → cache path or
      null (logged), `RegisterForTests(Poe1VoiceIndex?)`, `Clear()`. Cache dir `<temp>/PillarsDialogEditor/poe1-vo`.
- [x] `MainWindowViewModel`: on game activation `Poe1VoiceService.Clear()`; if poe1, `Rebuild(path)`.
- [x] `NodeDetailViewModel`:
  - PoE1 branch in `NotifyAllProxies` building `VoCheckResult(Found, femFound, primaryCachePath, femCachePath)`
    from the index (paths are where `Extract` will write); `null` when no clip; no `WithLocalVoFallback`.
  - `_voIsPoe1` guard on import and alias getters.
  - `PlayPrimary`/`PlayFem`: when `_voIsPoe1`, call `Poe1VoiceService.Extract` first; on null, do nothing.
  - Subscribe to `Poe1VoiceService.Changed` → re-run the VO proxies (marshal like other service events).
  - Tests (`StubVoAudioPlayer`, `RegisterForTests`): found → `VoStatusIsFound`, `CanPlayAudio`, play calls the
    player with an existing `.fsb`; no clip → `HasVoStatus == false`; fem only with female text and clip; import
    and alias hidden; PoE2 tests unchanged.

## Task 3 — verify and ship

- [x] Full headless suite green.
- [x] GUI: open the PoE1 install, open `00_cv_beodmar`, select node 89, see "found", press Play (check the log
      for no vgmstream error). Use `Invoke-CaptureMatrix` (backs up settings and layout.json).
- [x] Trail note (what took digging: AudioClip layout, names, placeholder rule), handoff below, PR with Auto-fix.

## Handoff — 2026-10-30

**Done** (branch `feat/4-poe1-vo-preview`):
- `8549a9d1` Core: `Poe1AudioClipRecord`, `Poe1VoiceIndex` (AssetsTools.NET 3.0.5), unit + GameData tests.
- `04179faf` trail note on the PoE1 clip layout.
- `bedf49e5` ViewModels: `Poe1VoiceService` (background build, generation guard, weak UI-context
  subscription), MainWindowViewModel start-up, Node Details PoE1 branch, import/alias gated off for PoE1.
- Follow-up filed: #355 (96 names shared by two different real clips show no voice; resolve via VOAsset paths).
- Verified in the app on the real PoE1 install: index logs 10,995 clips / 96 ambiguous; `00_cv_beodmar` node 89
  shows "VO file found", ▶ M starts playback, the extracted `.fsb` decodes with vgmstream (20.4 s, Vorbis).

**Deviations from the spec:** GameData floor is ≥ 10,900 distinct names (11,267 records include duplicates);
ambiguous duplicate names resolve to no voice (spec §2 assumed names were unique).

**Remaining:** owner review of the PR. Steps 2–3 of #4 (import/replace PoE1 voice) are a separate decision.

**Next step:** address review comments on the PR, if any.

**Do not redo:** the AudioClip layout and name mapping are settled (see the trail note); don't read FSB5 bank
names or the `assetbundles/vo` bundles for lookup.

## Handoff — 2026-10-30 (#355)

**Done** (branch `feat/355-poe1-vo-ambiguous`): `Poe1VoiceIndex` now follows the game's VOAsset route
(ResourceManager container in `globalgamemanagers` → VOAsset `VOClip.clip` → AudioClip) via new
`Poe1ResourceContainer`, `Poe1VoAssetRecord`, `Poe1VoiceRoutes`. On the shipped game 77 of the 96 ambiguous names
resolve; the other 19 have no VOAsset or an empty one (silent in-game too). Lines whose VOAsset reuses another
conversation's clip (23) now play that clip. Cache files are `<name>.<offset:x>.fsb` so same-name clips no longer
collide. Spec §2 updated.

**My call:** a VOAsset with an empty or unresolvable clip (13 because of an `Irensî`/`irensi` mismatch in the game
data, 34 empty with a same-name recording) keeps the name match instead of going silent.

**Not done:** no GUI run; covered by unit tests and the GameData test (`07_cv_deathguard` 29 resolves,
`px2_00_cv_rabble_rouser` 130 plays `px2_02_cv_final_battle_prep_0183`).

**Next step:** owner review of the PR. Open question: should empty-clip VOAssets go silent, as in the game?
