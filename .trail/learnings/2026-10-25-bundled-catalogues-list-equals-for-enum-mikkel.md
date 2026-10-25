---
title: "Bundled catalogues list \"Equals\" for Enum:Operator, but the game's enum has EqualTo"
author: Mikkel
date: 2026-10-25
branch: fix/266-operator-labels
cost: { minutes: 34, tokens: 399162 }
id: l-30d8751d
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Resources/Strings.axaml
  - DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml
  - DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml
  - DialogEditor.Tests/ViewModels/ConditionOperatorLabelTests.cs
  - DialogEditor.Tests/Views/OperatorChoiceMarkupTests.cs
  - DialogEditor.ViewModels/ViewModels/ConditionRowViewModel.cs
  - DialogEditor.ViewModels/ViewModels/ScriptEditorViewModel.cs
  - DialogEditor.Avalonia.Shared/EmptyState.axaml
  - DialogEditor.Avalonia.Shared/EmptyState.axaml.cs
  - DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml
---

## Decided
GitHub issue 266. `DialogEditor.ViewModels/Resources/conditions.json` and `scripts.json` type operator parameters `Enum:Operator` and list the options `Equals, GreaterThan, LessThan, NotEqualTo, GreaterThanOrEqualTo, LessThanOrEqualTo` (default `EqualTo`). The game's enum (`Game.GameData.Operator` in PoE2, `Conditionals.Operator` in PoE1, from the decompiled sources) is `EqualTo, GreaterThan, LessThan, NotEqualTo, GreaterThanOrEqualTo, LessThanOrEqualTo`: there is no `Equals`, so a condition saved with it can't be parsed by the game.

`ParameterValueViewModel.Choices` (in `ConditionRowViewModel.cs`) therefore ignores the catalogue's options for operator types and builds a labelled enum from `OperatorNames` (the game's names, in the game's order) with plain labels from the `ConditionOperator_*` strings. `ParameterValueViewModel.Display` reads a stored `Equals` as `EqualTo`, so re-saving repairs old conditions.

## Watch out
- Operators are typed `Enum:Operator` in the real catalogues, not `Operator`. A test built on a hand-made `Operator`-typed parameter passes while the real data is untouched; `IsOperatorType` accepts both.
- The catalogue JSON still lists `Equals`. It is unused for operators now, but anything new that reads catalogue options directly will see it.
