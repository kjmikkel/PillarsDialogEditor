---
title: Adding a DialogProject field changes the canonical-mod golden files; Game State saves as a metadata edit
author: Mikkel
date: 2026-10-29
branch: claude/objective-goldstine-e0803d
cost: { minutes: 13, tokens: 152562 }
id: l-7bfe408c
scope: repo
status: active
touches:
  - DialogEditor.Tests/Patch/GameStateSnapshotTests.cs
  - DialogEditor.Tests/ViewModels/MainWindowViewModelGameStatePersistenceTests.cs
  - DialogEditor.Patch/GameStateSnapshot.cs
  - DialogEditor.Patch/DialogProject.cs
  - PROJECT-FORMAT.md
---

## Decided
- GitHub issue 344: the Game State panel's assumed state is saved as `DialogProject.GameState`, a `GameStateSnapshot` DTO in `DialogEditor.Patch/GameStateSnapshot.cs` (the live `GameState` keys played nodes by tuple and relies on comparers JSON can't carry). Optional field, no SchemaVersion bump: System.Text.Json ignores unknown members, so older editors load it.
- Quest status is stored by name. This serializer writes enums as ordinals (e.g. `DuplicateKind`), which would misread old files if `QuestStatus` were reordered. An unknown name leaves that quest unset.
- `MainWindowViewModel.OnAssumedStateChanged` follows `AddProjectWord`: it updates `_project` directly and sets `IsModified`, with no SetProject and no undo. It is skipped in reading mode (Save needs Editing) and when the JSON is unchanged (Clear All on an empty state still notifies). `FinishLoad` restores via `RestoreAssumedState`, which replaces the state, so a project saved without one clears it.

## Watch out
- `DialogProjectSerializer` uses `JsonIgnoreCondition.Never`, so a new optional field is written as `null` in every project. `CanonicalModGoldenTests` then fails until you regenerate with `DIALOGEDITOR_REGENERATE_CANONICAL_MOD=1` and commit the fixture diff.
- Like `ProjectWords`, Game State edits land in `_project` before Save, so Discard does not revert them (the autosave learning's "_project is last-saved" holds for canvas edits only).
