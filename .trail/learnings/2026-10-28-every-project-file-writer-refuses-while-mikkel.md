---
title: "Every project-file writer refuses while the open project's git conflict is pending"
author: Mikkel
date: 2026-10-28
branch: fix/329-conflict-pending-writes
cost: { minutes: 6, tokens: 40676 }
id: l-326e43ed
scope: repo
status: active
touches:
  - DialogEditor.Tests/ViewModels/MainWindowViewModelDeferredReloadTests.cs
  - DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
---

## Decided
GitHub issue 329. `MainWindowViewModel.RefuseWriteWhileConflictPending` is the one guard for writing the open project's file while its git conflict is unresolved (reachable when Restore on the autosave offer keeps a conflicted project open). `SaveProject`, `ApplyFromDiff`, `UndoApply` and `MergeProjects` all call it, with `Status_SaveRefusedConflictPending`. This supersedes the Oct 26 watch-out that the last three bypass the refusal.

## Watch out
Each writer calls the guard before it changes anything: `ApplyFromDiff` before its save prompt, `UndoApply` before it clears `_preApplyProject` (so the undo survives), `MergeProjects` before the unsaved prompt and the picker. A new direct `DialogProjectSerializer.SaveToFile(_projectPath, …)` needs the same call first. Pass `nameof(...)` as the action: a string literal trips `NoNewHardcodedUserVisibleTextInCSharp`.
