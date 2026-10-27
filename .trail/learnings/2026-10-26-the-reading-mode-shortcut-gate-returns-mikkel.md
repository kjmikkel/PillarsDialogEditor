---
title: The reading-mode shortcut gate returns without setting e.Handled
author: Mikkel
date: 2026-10-26
branch: feat/234-reading-mode
cost: { minutes: 159, tokens: 557675 }
id: l-46a3709f
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
- `MainWindow.OnKeyDownTunnel` and `ConversationView.Editor_KeyDown` check `ReadingModeShortcuts.IsAllowed(key, modifiers)`. A blocked key returns early and does NOT set `e.Handled`. Setting it in the tunnel would swallow Delete, Ctrl+Z, Ctrl+A and similar keys before a focused TextBox (the filter box, read-only Node Details text) could handle them. Menu `InputGesture`s are display-only, so the tunnel handler is the only shortcut route.
- View › Reading mode and the mode badge use a Click handler, not `Command=`. It calls `MainWindow.CommitFocusedTextEdit()` (clears focus so a LostFocus binding commits a half-typed Node Details edit) before awaiting the toggle. The menu item then re-syncs its check with `SetCurrentValue`, because the toggle is async and can be refused.

## Watch out
- Window tests that open and close `ModeChooserWindow` without a choice save Mode=Reading. Any such test must set `AppSettings.SettingsPathOverride`, or it leaks Reading into the shared settings file and breaks unrelated tests.
