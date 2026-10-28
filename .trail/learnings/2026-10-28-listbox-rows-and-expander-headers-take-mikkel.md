---
title: ListBox rows and Expander headers take their UIA name from a style; their peers read AutomationProperties.Name
author: Mikkel
date: 2026-10-28
branch: fix/325-uia-row-names
cost: { minutes: 16, tokens: 127455 }
id: l-d84e8ce2
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Resources/Strings.axaml
  - DialogEditor.Avalonia/Views/NodeDetailView.axaml
  - DialogEditor.Avalonia/Views/RepDispositionBalanceWindow.axaml
  - DialogEditor.Avalonia/Views/SpeakerLineBrowserWindow.axaml
  - DialogEditor.Avalonia/Views/SpeakerLineBrowserWindow.axaml.cs
  - DialogEditor.Tests/Views/ListRowAutomationNameTests.cs
  - DialogEditor.ViewModels/ViewModels/RepDispositionBalanceViewModel.cs
  - DialogEditor.Tests/ViewModels/MainWindowViewModelDeferredReloadTests.cs
  - DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
  - DialogEditor.Tests/ViewModels/MainWindowViewModelTranslationExportUnsavedTests.cs
---

## Decided
GitHub issue 325. A ListBoxItem with no name of its own is announced as its item's `ToString()` (a record dump or a type name). The fix is a `ListBoxItem` style whose setter binds `AutomationProperties.Name` to an `AutomationName` property on the row model (`SpeakerLineBrowserWindow.axaml`, `RepDispositionBalanceWindow.axaml`). Expander headers whose content isn't a string read as "Avalonia.Controls.Grid". `NodeDetailView.axaml` styles `Expander.detail-group /template/ ToggleButton` with `{Binding $parent[Expander].(AutomationProperties.Name)}`.

## Watch out
This does not contradict the Nodify and Dock learnings that `trail check` matches it to. Those controls' peers ignore the attached property: Nodify's `ItemContainer` gets a `NoneAutomationPeer`, and Dock's tab peers override `GetHelpTextCore`. ListBoxItem and ToggleButton have standard peers that read it. Verify with `ControlAutomationPeer.CreatePeerForElement(c).GetName()` against `AvaloniaStringProvider` (`DialogEditor.Tests/Views/ListRowAutomationNameTests.cs`). Strip markup (`TextMarkup.Strip`) from line text before putting it in a name.
