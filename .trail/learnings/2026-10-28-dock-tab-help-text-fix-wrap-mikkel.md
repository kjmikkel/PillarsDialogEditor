---
title: "Dock tab help-text fix: wrap Dock's peer and replace the DocumentControl/ToolControl templates with a Style"
author: Mikkel
date: 2026-10-28
branch: fix/264-dock-tab-helptext
cost: { minutes: 19, tokens: 197623 }
id: l-e808fe49
scope: repo
status: active
touches:
  - tools/ui-automation/CaptureSurfaces.ps1
  - DialogEditor.Tests/Docking/DockTabAutomationTests.cs
  - DialogEditor.Avalonia/Docking/AccessibleDockTabs.cs
  - DialogEditor.Avalonia/Docking/DockTabTemplates.axaml
  - DialogEditor.Avalonia/App.axaml
---

## Decided
GitHub issue 264 (F-10) is fixed in `DialogEditor.Avalonia/Docking/AccessibleDockTabs.cs` and `DockTabTemplates.axaml`. Strip subclasses create item subclasses. Each item's peer (`DockTabItemAutomationPeer`) wraps Dock's own peer from `base.OnCreateAutomationPeer()` and delegates name, id, class, control type, Invoke and SelectionItem to it. Only the help text is overridden, and it comes from `AutomationProperties.HelpText`. Once wieslawsoltes/Dock issue 1201 is fixed, delete both files and the `StyleInclude` in `App.axaml`.

## Tried and ruled out
- Subclassing Dock's peers: they are internal sealed.
- Overriding the template's resource key: the Fluent `DocumentControl` theme sets its Template with `StaticResource`, which resolves at load time. A `Style` setter on `DocumentControl`/`ToolControl` works instead, because style setters outrank ControlTheme setters.
- Changing the strips only through `StyleKeyOverride`: the strips create containers with `new DocumentTabStripItem()`, so `CreateContainerForItemOverride` has to be overridden.

## Watch out
- The templates in `DockTabTemplates.axaml` are copies of Dock Fluent 11.3.12.1's templates. Re-copy them on every Dock upgrade.
- Dock raises the SelectionItem IsSelected event only when the element's peer is its own type, so the wrapper raises it itself.
- The GUI capture tooling (`tools/ui-automation/CaptureSurfaces.ps1`) needs a saved Mode, or launch stops at the first-run mode chooser.
