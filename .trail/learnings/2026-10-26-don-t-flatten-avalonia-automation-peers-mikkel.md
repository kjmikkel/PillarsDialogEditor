---
title: "Don't flatten Avalonia automation peers: a peer's parent is whoever listed it last"
author: Mikkel
date: 2026-10-26
branch: fix/311-uia-tree-walk
cost: { minutes: 18, tokens: 187668 }
id: l-3347bf4c
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Controls/CanvasEditor.cs
  - DialogEditor.Tests/Views/CanvasNodeAutomationTests.cs
---

## Decided
GitHub issue 311, follow-up to PR 314. `CanvasEditorAutomationPeer` (`DialogEditor.Avalonia/Controls/CanvasEditor.cs`) is a plain `ItemsControlAutomationPeer` with no `GetChildrenCore` override. Nodify's peer-less layers (Border, Canvas, ItemsPresenter, NodifyCanvas) stay in the tree as `NoneAutomationPeer`s. The UIA control view skips them, so the node ListItems still read as the canvas's items.

## Tried and ruled out
PR 314 flattened those layers so the nodes were the editor peer's direct children. That passed headless tests but broke the running app: a real UIA walk reached no node at all, and a node's parent chain stopped at a Border.

Why: in Avalonia 11.3, `ControlAutomationPeer.GetOrCreateChildrenCore` calls `TrySetParent(this)` on whatever it lists. `EnsureConnected` asks every visual ancestor for its children, and adding nodes invalidates only the `NodifyCanvas` peer, which then re-parents them. The Win32 bridge (`Avalonia.Win32.Automation.AutomationNode.Navigate`) finds a node's next sibling through `GetParent().GetChildren()`, so a peer that lists children it doesn't own splits the tree.

## Watch out
- Headless tests only reproduce this if the peers exist before the nodes are added. `CanvasNodeAutomationTests` walks the tree the bridge's way after adding a node late.
- In the GUI, a node's UIA Name is "<speaker>: <text> (node N)", not "Node N".
- Node Details' spelling-issue list (with Add to project dictionary) is inside the DISPLAY expander, which starts collapsed.
