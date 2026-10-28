---
title: Unsaved-prompt continuation opens the pending conversation via OpenConversationView and falls back to the previous one
author: Mikkel
date: 2026-10-28
branch: fix/328-pending-project-conversation
cost: { minutes: 5, tokens: 82089 }
id: l-39de5881
scope: repo
status: active
touches:
  - DialogEditor.Tests/ViewModels/MainWindowViewModelPendingSwitchTests.cs
  - DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
---

## Decided
GitHub issue 328. `MainWindowViewModel.Proceed` (after Save/Discard in the unsaved-changes prompt) opens a pending conversation switch through `OpenPendingConversation`, which calls `OpenConversationView`, not `LoadConversationFile`. `LoadConversationFile` reads the game's own file, so a conversation the project created (no game file) failed with an error dialog.

## Watch out
`DiscardAndProceed` deliberately skips reverting the open conversation when a switch is pending, and `LoadConversationFile` sets `_currentFile` to the target before it can fail. So a failed target load used to leave the discarded edits on the canvas with `_currentFile` already pointing at the target; a later Save wrote them there. `OpenPendingConversation` restores `_currentFile` and reopens the previous conversation as saved. Any new caller that loads a conversation after Discard needs the same fallback. Tests: `DialogEditor.Tests/ViewModels/MainWindowViewModelPendingSwitchTests.cs`.
