---
title: "Canvas context menus open on key-up: Avalonia's ContextMenu closes itself when the Menu key is released"
author: Mikkel
date: 2026-11-09
branch: feat/361-link-keyboard-access
cost: { minutes: 19, tokens: 287609 }
id: l-df57d1ee
scope: repo
status: active
touches:
  - DialogEditor.Tests/ViewModels/CanvasLinkFocusTests.cs
  - DialogEditor.Tests/Views/ConversationViewLinkKeyboardTests.cs
  - DialogEditor.Tests/Input/ReadingModeShortcutsTests.cs
  - DialogEditor.Avalonia/Views/ConversationView.axaml.cs
  - DialogEditor.Avalonia/Input/ReadingModeShortcuts.cs
---

## Decided
GitHub issue 361. `ConversationView.Editor_KeyUp` (not `Editor_KeyDown`) handles the Menu key and Shift+F10 for both the node menu and a focused link's menu. Avalonia's `ContextMenu.PopupKeyUp` closes an open menu when the context-menu gesture is released, which is why Avalonia itself opens context menus on key-up. A menu opened on key-down was closed by its own key release. The node menu had this bug from the start: the Menu key never left it open in the real app.

Links reach the keyboard as `CanvasConnection` with `Focusable=true, IsTabStop=false`, plus a plain `ControlAutomationPeer`. L / Shift+L cycle `ConversationViewModel.FocusedLink` through the selected node's links in `Connections` order. The peer appears in the window's UIA tree with no help from the Grid or Nodify layers.

## Tried and ruled out
Tests that raise a `KeyDown` RoutedEvent on the editor: they passed while the real app showed no menu. Only `window.KeyPress` + `window.KeyRelease` (Avalonia.Headless) reproduce it, because `PopupKeyUp` needs the real key-up routed to the open popup.

## Watch out
The test helpers `Press` in `ConversationViewKeyboardTests` and `ConversationViewLinkKeyboardTests` now raise down AND up for every key. A keyboard-opened menu is placed at the mouse pointer (ContextMenu's default Placement), not at the node or link.
