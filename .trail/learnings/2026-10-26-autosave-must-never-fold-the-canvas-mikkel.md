---
title: Autosave must never fold the canvas into the in-memory project; _project is the last-saved state
author: Mikkel
date: 2026-10-26
branch: feat/234-reading-mode
cost: { minutes: 151, tokens: 530242 }
id: l-efba1ea1
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Resources/Strings.axaml
  - DialogEditor.Tests/ViewModels/MainWindowViewModelDeferredReloadTests.cs
  - DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
  - DialogEditor.Avalonia/Views/ConversationView.axaml.cs
  - DialogEditor.Avalonia/Views/MainWindow.axaml.cs
  - DialogEditor.Avalonia/Views/RepDispositionBalanceWindow.axaml.cs
  - DialogEditor.Tests/Services/AppSettingsEditorModeTests.cs
  - DialogEditor.Tests/Theming/PaletteContrastTests.cs
  - DialogEditor.Tests/ViewModels/ConversationViewModelUndoGateTests.cs
  - DialogEditor.Tests/ViewModels/GuidedTourViewModelTests.cs
---

## Decided
- `MainWindowViewModel.AutosaveTick` serialises a folded COPY (`BuildFoldedProject`), and `_project` stays the last-saved state. Only Save and Save As fold for real (`FoldCanvasIntoProject`). Autosave reads ID remaps with `PeekPendingRemaps()`; only a real save takes them.
- Discard (`DiscardAndProceed`) reverts the open conversation to its saved state (`ReloadCurrentConversationView`) and clears `IsModified` *before* the continuation runs. A conversation switch is the exception, because it loads the target anyway.
- `FinishLoad` reloads the open conversation from the new project whenever the project is swapped (open, autosave Restore, conflict merge, branch switch). It keeps a `!IsModified` guard only for a failed Save and for Create Sample Project. The comment there names both.

## Tried and ruled out
- Folding in autosave (the old behaviour): Discard reloaded the conversation from `_project`, which re-applied the autosaved patch. The edit survived Discard, and a later Save wrote it.
- Clearing only the dirty flag on Discard: the discarded edits stayed on the canvas, and the next Save persisted them.

## Watch out
- Merge Projects, translation export and Test patch read `_project` and so see only saved state (issue 326). New Project and Merge Projects don't reload the canvas (issue 327).
