---
title: PoE1 VO override works as a BepInEx 5 plugin patching GetDialogueAudio and Coroutine_StreamClip
author: Mikkel
date: 2026-10-31
branch: spike/poe1-vo-plugin
cost: { minutes: 10, tokens: 239268 }
id: l-acd306e1
scope: repo
status: active
touches:
  - tools/poe1-vo-plugin/Plugin.cs
  - tools/poe1-vo-plugin/Poe1VoOverride.csproj
  - DialogEditor.slnx
---

## Decided
- Issue 4, option 3 spike (branch spike/poe1-vo-plugin, `tools/poe1-vo-plugin/`). BepInEx 5.4.23.5 win_x64 loads on PoE1 (Unity 2019.4.41 Mono, 64-bit). A Harmony postfix on `GameResources.GetDialogueAudio` returns a runtime `VOAsset` (ScriptableObject.CreateInstance, VOClip.clip = marker + file path) when the game has no VOAsset or an empty clip; a prefix on `GlobalAudioPlayer.Coroutine_StreamClip` loads marked paths with UnityWebRequestMultimedia and otherwise copies the game's coroutine. Verified in game: conversation lines and overhead barks both play from `<game>/VO Override/<conv>/<conv>_<node:0000>.wav`.
- Spike runs on the Epic install so the GOG install the GameData tests read stays untouched; patch-target signatures are identical in both builds.

## Tried and ruled out
- Writing new clips into resources.assets/globalgamemanagers (option 2): shared files, patches overwrite them, no free FSB5 encoder.

## Watch out
- The plugin project is not in DialogEditor.slnx: it references the game's Managed DLLs and BepInEx/core, which CI lacks. Build with `-p:Poe1Dir=<game folder>`.
- `Resources.UnloadAsset` refuses a runtime AudioClip; use `Object.Destroy`.
- A real VOAsset whose clip is the 44,100 Hz / 14,976-sample placeholder is only skipped after loading, so the GetDialogueAudio postfix cannot fall back for it.
- No OGG encoder on the dev machine; only WAV was verified.
