---
title: "Canvas nodes need their own peer: Nodify's ItemContainer gets NoneAutomationPeer and drops out of UIA"
author: Mikkel
date: 2026-10-26
branch: fix/311-canvas-node-uia
cost: { minutes: 76, tokens: 440420 }
id: l-b0b624bd
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Behaviors/SpellSquiggles.cs
  - DialogEditor.Avalonia/Controls/SquiggleGeometry.cs
  - DialogEditor.Avalonia/Views/NodeDetailView.axaml
  - DialogEditor.Tests/Services/SpellCheckServiceTests.cs
  - DialogEditor.Tests/ViewModels/NodeDetailViewModelValidationTests.cs
  - DialogEditor.Tests/Views/SpellSquigglesBehaviorTests.cs
  - DialogEditor.ViewModels/Services/SpellCheckService.cs
  - DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs
  - docs/superpowers/specs/2026-07-11-spell-checker-design.md
  - DialogEditor.Avalonia/Resources/Strings.axaml
---

## Decided
GitHub issue 311. `DialogEditor.Avalonia/Controls/CanvasEditor.cs` subclasses `NodifyEditor` (overrides `GetContainerForItemOverride()` to return `CanvasNodeContainer`) and `CanvasNodeContainer.cs` subclasses `ItemContainer` with a `ListItem` peer that implements `ISelectionItemProvider`. `ConversationView.axaml` uses `ctrl:CanvasEditor`. Both subclasses set `StyleKeyOverride` to the Nodify base type, or no theme matches and nothing renders. This reverses the issue 242 learning's "don't subclass ItemContainer": UIA addressability (CLAUDE.md) is the reason it was worth it.

## Tried and ruled out
Setting `AutomationProperties.Name` on the container (already done for issue 242) is not enough: without an `OnCreateAutomationPeer` override Avalonia uses `NoneAutomationPeer`, which is neither a control nor a content element. The node's header text then hangs straight off the canvas list, UIA hit-testing at a node returns the whole `NodifyEditor`, and `GetClickablePoint()` fails even for a fully visible node. It was first misread as an off-screen or DPI coordinate problem.

## Watch out
The stock `ItemsControlAutomationPeer` reports Nodify's template as several nested `NoneAutomationPeer` layers, so `CanvasEditorAutomationPeer.GetChildrenCore` flattens them to make the nodes direct children of the canvas. Scripts should select a node with the SelectionItem pattern (`Select()`) rather than clicking. Filter `Find-EditorElements` results only after `ForEach-Object { $_ }`.
