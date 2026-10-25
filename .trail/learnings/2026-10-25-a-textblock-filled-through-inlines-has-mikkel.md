---
title: A TextBlock filled through Inlines has a null Text and loses its automation name
author: Mikkel
date: 2026-10-25
branch: fix/256-node-counts
cost: { minutes: 8, tokens: 71422 }
id: l-57b77ed6
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Resources/Strings.axaml
  - DialogEditor.Core/Analytics/FlowAnalysisService.cs
  - DialogEditor.Core/Analytics/WordCount.cs
  - DialogEditor.Tests/Analytics/NodeCountConsistencyTests.cs
  - DialogEditor.Tests/ViewModels/ConversationStatisticsTests.cs
  - DialogEditor.ViewModels/Models/ConversationStatistics.cs
  - DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs
  - DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
---

## Decided
GitHub issue 258: `DialogEditor.Avalonia/Behaviors/MarkupText.cs` (the `MarkupText.Source` attached property) builds `Run`s into `TextBlock.Inlines`. It also sets `AutomationProperties.Name` to the plain text from `TextMarkup.Strip`, because Avalonia's TextBlock automation peer names the control by `Text`, which stays null when Inlines are used.

## Watch out
Tests that find a TextBlock by `t.Text!` throw on such a TextBlock. Use `t.Text?.…`, as `CanvasCardRenderTests.CardRender_LongTitle_IsTrimmedInsideTheCard` now does. To assert the content of a markup-rendered TextBlock, read `Inlines.OfType<Run>()`.
