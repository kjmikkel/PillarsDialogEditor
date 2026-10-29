---
title: "PoE1 voice lines are AudioClips in resources.assets, found by name, audio as FSB5 slices of resources.resource"
author: Mikkel
date: 2026-10-29
branch: feat/4-poe1-vo-preview
cost: { minutes: 26, tokens: 180488 }
id: l-dde7195e
scope: repo
status: active
touches:
  - DialogEditor.Core/Audio/Poe1AudioClipRecord.cs
  - DialogEditor.Core/Audio/Poe1VoiceIndex.cs
  - DialogEditor.Core/DialogEditor.Core.csproj
  - DialogEditor.Tests/Audio/Poe1AudioClipRecordTests.cs
  - DialogEditor.Tests/Audio/Poe1VoiceIndexTests.cs
  - DialogEditor.Tests/GameData/Poe1VoiceIndexInstallTests.cs
  - Directory.Packages.props
  - THIRD_PARTY_LICENSES.md
  - docs/superpowers/plans/2026-10-29-poe1-vo-preview.md
  - docs/superpowers/specs/2026-10-29-poe1-vo-preview-design.md
---

## Decided
- PoE1 VO preview (#4) reads `PillarsOfEternity_Data/resources.assets` with AssetsTools.NET and matches AudioClip names `<conversation>_<node:0000>` (+ `_fem`), case-insensitively (`DialogEditor.Core/Audio/Poe1VoiceIndex.cs`). The game's own lookup goes through VOAssets, which the editor skips.
- AudioClips there have no type tree, so `Poe1AudioClipRecord.Parse` hard-codes the Unity 2019.4 layout; `Load` refuses other Unity versions rather than misreading bytes.
- Each clip's audio is one FSB5 bank at m_Resource offset/size in `resources.resource`; the bundled vgmstream-cli decodes the sliced bank directly, so playback needs no player change.
- Placeholder clips (44,100 Hz, 14,976 samples) count as no voice, as in the game.
- Names shared by two different real clips (96 on the shipped install) resolve to no voice: picking one can play the wrong speaker's line. Resolving them is #355.

## Tried and ruled out
- Reading names from the FSB5 banks: the banks carry no usable names; map through AudioClip records instead.
- `assetbundles/vo/` bundles: they have VOAsset copies with type trees, but the installed game never loads them.

## Watch out
- Distinct VO names (~10,995) are fewer than VO-named records (~11,267): duplicates include placeholders and identical copies.
- `dotnet add package` rewrites `Directory.Packages.props` formatting (central package management); add the PackageVersion line by hand.
