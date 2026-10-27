---
title: DockPanel children declared first to dock right become the first Tab stops
author: Mikkel
date: 2026-10-26
branch: fix/255-tab-order
cost: { minutes: 24, tokens: 231465 }
id: l-3968c488
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Views/MainWindow.axaml
  - DialogEditor.Tests/Views/MainWindowTabOrderTests.cs
  - DialogEditor.Tests/Services/SpeakerNameServiceTests.cs
  - DialogEditor.Tests/Analytics/PathStatsServiceTests.cs
  - DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs
  - DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs
---

## Decided
GitHub issue 255: in `DialogEditor.Avalonia/Views/MainWindow.axaml` the ? and ⚙ buttons were declared first in the menu row's DockPanel so they would dock right. Avalonia's Tab order follows declaration order, not layout, so they became the window's first stops, ahead of the menu. They now sit in one right-docked StackPanel declared last, with `LastChildFill="False"` so the group doesn't stretch.

## Watch out
The UI/UX pass's walk reported the menu as stop 39 because it started from wherever focus happened to be (inside the dock). Nothing focuses a control at startup, so reason about the Tab cycle's order, not its first stop.
Main-window Tab order can be tested headless: `MainWindowTabOrderTests` sets `_activeGameId`, calls `RebuildConditionSearch`, sets `_dockRestored = true` (so the real dock-layout file isn't read), then calls `MainWindow.BuildDock` via reflection and walks `KeyboardNavigationHandler.GetNext`. Dock pane chrome buttons (Pane options/Pin/Close) come after the pane's content; that order comes from Dock.Avalonia's templates.
