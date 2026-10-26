---
title: Restoring a dock layout must carry PinnedBounds; saving must skip RootDock.PinnedDock
author: Mikkel
date: 2026-10-26
branch: fix/318-windows-follow-text-size
cost: { minutes: 35, tokens: 178660 }
id: l-95994754
scope: repo
status: active
touches:
  - DialogEditor.PatchManager/MainWindow.axaml
  - DialogEditor.Tests/Theming/WindowSizingTests.cs
  - DialogEditor.Tests/Views/NodeDetailViewTextWarningsTests.cs
  - DialogEditor.Tests/Docking/DockLayoutPinnedPaneTests.cs
  - DialogEditor.Avalonia/Docking/EditorDockFactory.cs
---

## Decided
GitHub issue 319.

`EditorDockFactory.ReplaceInList` copies `GetPinnedBounds` from each deserialized wrapper (Browser, Details, ConditionSearch, Canvas) onto the fresh live instance. Dock sizes a pinned pane's pop-out from those bounds. Without them it sizes to content, which hits Dock's 50 px minimum.

`DockLayoutStore.Save`:
- nulls `RootDock.PinnedDock` for the save and restores it in `finally`;
- writes `layout.json.tmp`, then `File.Move(..., overwrite: true)`.

## Tried and ruled out
"Proportion 0 on LeftPane" in layout.json is not the cause. Dock empties the pane and zeroes it once its only tool is pinned; resetting proportions doesn't help.

## Watch out
- `RootDock.PinnedDock` exists only while a pop-out is open. Serializing it reaches its Factory's `IDockable`-keyed control dictionaries, which System.Text.Json can't write ("not a supported dictionary key … $.PinnedDock.Factory.VisibleDockableControls"). Headless tests only reproduce this once a control is registered.
- A failed save used to leave a 0-byte layout.json, and the next launch silently used the default layout. The log shows "input does not contain any JSON tokens".
- `DockLayoutStore` has an internal `IDockSerializer` constructor so tests can fail a save part-way through.
