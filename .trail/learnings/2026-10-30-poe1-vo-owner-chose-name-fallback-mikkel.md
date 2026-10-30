---
title: "PoE1 VO: owner chose name fallback over game-faithful silence for empty or unresolvable VOAssets"
author: Mikkel
date: 2026-10-30
branch: feat/355-poe1-vo-ambiguous
cost: { minutes: 6, tokens: 22271 }
id: l-423ef96e
scope: repo
status: active
touches:
  - docs/superpowers/plans/2026-10-29-poe1-vo-preview.md
  - DialogEditor.Core/Audio/Poe1AudioClipRecord.cs
  - DialogEditor.Core/Audio/Poe1ResourceContainer.cs
  - DialogEditor.Core/Audio/Poe1VoAssetRecord.cs
  - DialogEditor.Core/Audio/Poe1VoiceIndex.cs
  - DialogEditor.Core/Audio/Poe1VoiceRoutes.cs
  - DialogEditor.Tests/Audio/Poe1ResourceContainerTests.cs
  - DialogEditor.Tests/Audio/Poe1VoAssetRecordTests.cs
  - DialogEditor.Tests/Audio/Poe1VoiceIndexTests.cs
  - DialogEditor.Tests/Audio/Poe1VoiceRoutesTests.cs
---

## Decided
- Issue 355, owner decision 2026-10-30. In `Poe1VoiceIndex.Find`, a VOAsset whose clip is empty (34 with a same-name recording on the shipped game) or does not resolve in the container (13, from the game data's `Irensî/` vs `irensi/` folder mismatch) falls back to the AudioClip name match. The game itself most likely plays nothing for these lines; the owner prefers that writers still hear the recording.

## Watch out
- Don't "fix" this towards game-faithful silence without asking: it was asked and answered. Only a route that reaches a known AudioClip is authoritative (a placeholder there does mean no voice).
