---
title: "Node counts go by SpeakerCategory over the canvas, not the loaded game file"
author: Mikkel
date: 2026-10-25
branch: fix/256-node-counts
cost: { minutes: 8, tokens: 71422 }
id: l-95a782ae
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
GitHub issue 256: every node count uses `SpeakerCategory`, the node type the serializers write (issue 160), and counts the live canvas.
- `ConversationViewModel.Statistics`, `FlowAnalysisService` and the status bar all follow this.
- `IsPlayerChoice` is not used to categorise.
- Words are counted only by `DialogEditor.Core/Analytics/WordCount.Of`.

## Watch out
In `MainWindowViewModel.OnConversationSelected`, `conversation` is the game file before the project patch is applied. `Canvas.Load` gets the patched conversation. Any count or lookup shown after a load should read `Canvas.Nodes`, not `conversation.Nodes`, or it misses nodes the project added.

A test fixture that sets `IsPlayerChoice=true` with `SpeakerCategory.Npc` does not match real data, because `NodeViewModel.SetCategory` keeps the two in step.
