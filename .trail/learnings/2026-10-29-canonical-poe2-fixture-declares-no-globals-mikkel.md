---
title: Canonical PoE2 fixture declares no globals; GUI checks of start values must add a bundle to the temp copy
author: Mikkel
date: 2026-10-29
branch: feat/345-fill-start-of-game
cost: { minutes: 9, tokens: 116232 }
id: l-87a026d5
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Resources/Strings.axaml
  - DialogEditor.Avalonia/Views/GameStateView.axaml
  - DialogEditor.Tests/ViewModels/GameStateViewModelTests.cs
  - DialogEditor.Tests/ViewModels/MainWindowViewModelGameStateTests.cs
  - DialogEditor.ViewModels/ViewModels/GameStateViewModel.cs
  - DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
---

## Decided
GitHub issue 345. Game State "Fill start-of-game values" (`GameStateViewModel.FillStartOfGame`) fills only rows that are still unset and keeps typed values. The panel has no undo; "Clear all" then Fill is the full reset. `MainWindowViewModel.LoadDirectory` feeds `SetInitialGlobalValues` once per folder open.

## Watch out
`DialogEditor.Tests/Fixtures/Canonical/poe2` has no `design/globalvariables/*.globalvariablesbundle`, so `LoadGlobalVariableInitialValues()` returns empty for it and the Fill button stays disabled. For a GUI run, write a bundle into the TEMP copy (`PillarsOfEternityII_Data/exported/design/globalvariables/`, JSON `{"GlobalVariablesSets":[{"GlobalVariables":[{"ID":"...","Tag":"canonical_flag_a","InitialValue":1}]}]}`). Do not add it to the fixture itself, because other tests count its game data.
The canonical conversation tests `canonical_flag_a`..`e` and `canonical_stage`, but the Game State panel shows no row for `canonical_stage` (not investigated).
`trail check` matches `AutomationProperties.Name`/`HelpText` on a plain Button against the Nodify-container and Dock-tab peer learnings. Those don't apply to ordinary controls; a GUI run found the button by its UIA name.
