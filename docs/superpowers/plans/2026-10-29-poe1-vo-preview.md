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

- [ ] Add NuGet `AssetsTools.NET` 3.0.5 to `DialogEditor.Core`; add an entry to `THIRD_PARTY_LICENSES.md` (MIT).
- [ ] `Poe1AudioClipRecord` + `Parse(AssetsFileReader)` (spec §3), `IsPlaceholder`. Tests build the bytes with
      `AssetsFileWriter` over a `MemoryStream`.
- [ ] `Poe1VoiceIndex`: internal ctor (records, data folder); `Find(conv, nodeId, female)`; `Count`;
      `Extract(clip, cacheDir)`; `static Load(gameRoot)` with the 2019.4 version guard. Tests: naming, case,
      `_fem`, placeholder, non-VO skipped, extract slice/reuse/bad magic/past end, missing file → null.
- [ ] GameData test (`[GameInstallFact]` PoE1 variant — see `GameInstallFactAttribute`): ≥ 11,000 clips;
      `00_cv_beodmar` node 89 found; its extracted file starts `FSB5`. Writes only under a temp dir.

## Task 2 — ViewModels

- [ ] `Poe1VoiceService` (static, ViewModels/Services) mirroring `VoAliasIndexService`: `Rebuild(gameRoot)`
      (background), `IsReady`, `Changed` event (raised on build/clear), `Find`, `Extract(clip)` → cache path or
      null (logged), `RegisterForTests(Poe1VoiceIndex?)`, `Clear()`. Cache dir `<temp>/PillarsDialogEditor/poe1-vo`.
- [ ] `MainWindowViewModel`: on game activation `Poe1VoiceService.Clear()`; if poe1, `Rebuild(path)`.
- [ ] `NodeDetailViewModel`:
  - PoE1 branch in `NotifyAllProxies` building `VoCheckResult(Found, femFound, primaryCachePath, femCachePath)`
    from the index (paths are where `Extract` will write); `null` when no clip; no `WithLocalVoFallback`.
  - `_voIsPoe1` guard on import and alias getters.
  - `PlayPrimary`/`PlayFem`: when `_voIsPoe1`, call `Poe1VoiceService.Extract` first; on null, do nothing.
  - Subscribe to `Poe1VoiceService.Changed` → re-run the VO proxies (marshal like other service events).
  - Tests (`StubVoAudioPlayer`, `RegisterForTests`): found → `VoStatusIsFound`, `CanPlayAudio`, play calls the
    player with an existing `.fsb`; no clip → `HasVoStatus == false`; fem only with female text and clip; import
    and alias hidden; PoE2 tests unchanged.

## Task 3 — verify and ship

- [ ] Full headless suite green.
- [ ] GUI: open the PoE1 install, open `00_cv_beodmar`, select node 89, see "found", press Play (check the log
      for no vgmstream error). Use `Invoke-CaptureMatrix` (backs up settings and layout.json).
- [ ] Trail note (what took digging: AudioClip layout, names, placeholder rule), handoff below, PR with Auto-fix.
