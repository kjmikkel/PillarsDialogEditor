---
title: "PoE1 VO: follow the VOAsset route through the globalgamemanagers ResourceManager container"
author: Mikkel
date: 2026-10-30
branch: feat/355-poe1-vo-ambiguous
cost: { minutes: 13, tokens: 180660 }
id: l-dc5296c1
scope: repo
status: active
touches:
  - DialogEditor.Tests/Audio/Poe1ResourceContainerTests.cs
  - DialogEditor.Tests/Audio/Poe1VoAssetRecordTests.cs
  - DialogEditor.Tests/Audio/Poe1VoiceRoutesTests.cs
---

## Decided
- Issue 355. `Poe1VoiceIndex` resolves a line the way the game does (GameResources.GetDialogueAudio + GlobalAudioPlayer.Coroutine_StreamClip): ResourceManager m_Container in `PillarsOfEternity_Data/globalgamemanagers` maps `audio/vocalization/vo assets/<conv>/<conv>_<node:0000>[_fem]` to a VOAsset MonoBehaviour in resources.assets; its `VOClip.clip` string, lower-cased with every `data/` removed and the extension stripped, is another container path that maps to the AudioClip. Files: `Poe1ResourceContainer.cs`, `Poe1VoAssetRecord.cs`, `Poe1VoiceRoutes.cs`.
- The route wins only when it reaches a known AudioClip; otherwise the old name match runs. Shipped game: 96 ambiguous names, 77 resolved, the other 19 are soundset barks with no VOAsset or one empty VOAsset.
- Cache file is `<name>.<offset:x>.fsb`: two different clips share names.

## Tried and ruled out
- Reading VOAssets via the `assetbundles/vo` type trees: not needed; the hand layout (PPtr m_GameObject, m_Enabled+align, PPtr m_Script, m_Name, DatabaseString 3x int32, VOClip.clip string) was verified on shipped bytes and is pinned in `Poe1VoAssetRecordTests.ReadClipPath_ShippedBytes`.

## Watch out
- globalgamemanagers externals: resources.assets is fileID 3 on the GOG build; compute it from Metadata.Externals, don't hard-code.
- The container has ~7.7k duplicate paths (art), none under audio/vocalization; first entry wins.
- 23 VOAssets point at a clip with a different name (e.g. px2_00_cv_rabble_rouser_0130 plays px2_02_cv_final_battle_prep_0183); 13 Irensi VOAssets say `Irensî/` while the container folder is `irensi/`, so they fall back to the name; 716 VOAssets have an empty clip.
- One VOAsset path has m_Script pathID 0 (missing script); skip it before reading further.
