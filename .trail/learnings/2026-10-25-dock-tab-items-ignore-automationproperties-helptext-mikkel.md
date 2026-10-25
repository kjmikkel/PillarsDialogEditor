---
title: Dock tab items ignore AutomationProperties.HelpText; their peer builds it from dockable state
author: Mikkel
date: 2026-10-25
branch: fix/264-copy-pass
cost: { minutes: 18, tokens: 148807 }
id: l-40d82a74
scope: repo
status: active
touches:
  - DialogEditor.Patch/Install/InstallModels.cs
  - DialogEditor.Patch/Install/PatchInstaller.cs
  - DialogEditor.PatchCli/PatcherCommand.cs
  - DialogEditor.Tests/PatchCli/PatcherCommandTests.cs
  - DialogEditor.Avalonia/Resources/Strings.axaml
  - DialogEditor.Avalonia/Views/ConditionSearchView.axaml
  - DialogEditor.Tests/ViewModels/ConditionSearchGuidanceTests.cs
  - DialogEditor.ViewModels/ViewModels/ConditionSearchViewModel.cs
  - DialogEditor.Tests/ViewModels/NodeViewModelTests.cs
  - DialogEditor.Tests/ViewModels/TextlessNodePlaceholderTests.cs
---

## Tried and ruled out
GitHub issue 264 (F-10): Narrator reads a debug string such as "Selected=true; Active=false; CanClose=…" as each dock tab's help text. Setting `AutomationProperties.HelpText` in a style in `DialogEditor.Avalonia/App.axaml` does not fix it. That approach works for the pane buttons (issue 261), but not for the tabs.

## Watch out
Decompiling Dock.Avalonia 11.3.12.1 shows that `DockTabStripItemAutomationPeer.GetHelpTextCore` returns `BuildStateText(dockable)` and never reads the attached HelpText. `DockControlAutomationPeer`, `DocumentControlAutomationPeer` and `DockTabStripAutomationPeer` override `GetHelpTextCore` the same way.

There are two real fixes:
- Subclass `DocumentTabStripItem` and `ToolTabStripItem`, give the subclasses their own automation peers, and swap them into Dock's templates.
- Fix it upstream in Dock. This belongs with the upstream automation-peer issue, #17.
