---
title: "Canvas screen-reader announcements ride on focus moving to the node's ItemContainer, not on a UIA selection peer"
author: Mikkel
date: 2026-10-25
branch: fix/242-announce-selected-node
cost: { minutes: 9, tokens: 68610 }
id: l-e037be4b
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Resources/Strings.axaml
  - DialogEditor.Avalonia/Views/ConversationView.axaml
  - DialogEditor.Avalonia/Views/ConversationView.axaml.cs
  - DialogEditor.Tests/ViewModels/NodeViewModelTests.cs
  - DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs
  - DialogEditor.ViewModels/ViewModels/NodeViewModel.cs
  - DialogEditor.Tests/ViewModels/PatchManagerViewModelInstallTests.cs
  - DialogEditor.ViewModels/ViewModels/PatchManagerViewModel.cs
  - DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml
---

## Decided
GitHub issue 242: `FollowSelection` in `DialogEditor.Avalonia/Views/ConversationView.axaml.cs` moves focus to `Editor.ContainerFromItem(node)` after each keyboard move. The container is named from `NodeViewModel.AutomationName` through the `ItemContainerTheme` in `ConversationView.axaml`. Containers are `Focusable=True, IsTabStop=False`, so the editor stays the canvas's single Tab stop (issue 238). Arrow keys pressed on a focused container still bubble to `Editor_KeyDown`. `ArrowKeys_StillNavigate_WhenSentFromTheFocusedNode` guards that.

## Tried and ruled out
Exposing a UIA "selected" state (SelectionItem pattern). Avalonia only lets you set a peer by overriding `OnCreateAutomationPeer`, which means subclassing Nodify's `ItemContainer`. Not worth it while focus only lands on a node when it is selected.

## Watch out
`FollowSelection` only moves focus when `Editor.IsKeyboardFocusWithin`, so selecting from the view model or by mouse never steals focus from other panes. Headless view tests that read `AutomationName` must call `Loc.Configure(...)` themselves: run alone, the test has no provider and throws.
