---
title: Nodify connections never raise ContextRequested; canvas links use CanvasConnection
author: Mikkel
date: 2026-11-01
branch: feat/359-reorder-links
cost: { minutes: 41, tokens: 176264 }
id: l-bd39a41b
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Views/ConversationView.axaml
  - DialogEditor.Tests/Views/ConversationViewReadingModeTests.cs
  - DialogEditor.Avalonia/Controls/CanvasConnection.cs
  - DialogEditor.Avalonia/Resources/Strings.axaml
  - DialogEditor.Tests/Views/CanvasConnectionContextMenuTests.cs
  - DialogEditor.Tests/ViewModels/LinkLabelPointTests.cs
  - DialogEditor.ViewModels/ViewModels/ConnectionViewModel.cs
  - DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs
  - DialogEditor.ViewModels/ViewModels/LinkLabelGeometry.cs
  - DialogEditor.Tests/ViewModels/CanvasLinkPositionTests.cs
---

## Decided
- A link's right-click menu on the canvas never opened: Nodify 6.6.0 `BaseConnection.OnPointerReleased` overrides Avalonia's `Control.OnPointerReleased` without calling base, and that base method is what raises `ContextRequested` on a right-button release. Fix: `DialogEditor.Avalonia/Controls/CanvasConnection.cs` (subclass, `StyleKeyOverride => typeof(Nodify.Connection)` so styles still match) raises it itself. The canvas ConnectionTemplate uses `ctrl:CanvasConnection`.
- Link position numbers sit on each link's own Bézier at t=0.3 (`LinkLabelGeometry`), mirroring Nodify's control points; all of a node's links share one output-port anchor, so a fixed offset from `Source.Anchor` stacks every number on the same spot.

## Tried and ruled out
- Blaming the Grid wrapper added around the connection for the label: restoring the old template changed nothing; the menu was broken before.

## Watch out
- Headless tests that open a ContextMenu programmatically pass even when real right-clicks can't reach it; test with `window.MouseDown/MouseUp(..., MouseButton.Right)`.
- Nodify connections have no automation peer, so links are absent from the UIA tree (issue 361); GUI scripts can't find them by name, only by pixel.
- A Nodify upgrade or a change to `Connection` Spacing/arrow offset breaks `LinkLabelGeometry`'s placement; a test pins plain Nodify's missing ContextRequested so an upgrade that fixes it is noticed.
