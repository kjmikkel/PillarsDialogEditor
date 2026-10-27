---
title: Reading mode defers project-open prompts until editing; a pending git conflict must never stay open in Editing
author: Mikkel
date: 2026-10-26
branch: feat/234-reading-mode
cost: { minutes: 151, tokens: 530242 }
id: l-cca5b44d
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
- In reading mode, opening a project defers three things until the switch to Editing (`OnEnteredEditingModeAsync`): the autosave-recovery offer (`_deferredOpenPath`), the git-conflict merge modal (pending conflict, with the "mine" side shown), and the first-run backup offer (`_deferredBackupPath`). Reading never writes, so the "mine" view is safe there.
- On entering Editing, if the user answers Later to the autosave offer, or cancels the conflict merge, the project is closed (`CloseProjectCore` plus `ClearCanvasAfterClose`) and `LastProjectPath` is kept. This keeps main's invariant: Editing never has a project open whose autosave copies are at risk, or whose own file holds unresolved conflict markers.
- `SaveProject` refuses while the open file's conflict is pending (`Status_SaveRefusedConflictPending`). Resolve Conflicts refuses while there are unsaved edits, because Save would write "mine" over the markers.
- The editing tour starts only after the deferred prompts have run.

## Watch out
- Apply from diff, Undo apply and Merge Projects write the project file without going through `SaveProject` (issue 329).
