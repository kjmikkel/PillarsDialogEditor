# Batch VO Import — Discoverable Menu Entry Point

**Date:** 2026-07-04
**Status:** Approved
**Gaps.md entries:** "Batch VO import is only reachable via canvas right-click" (resolved by this design);
"Project-wide batch VO import has no entry point" (explicitly out of scope — kept open).

## Problem

The per-conversation "Batch import VO…" feature is reachable only through the canvas
right-click context menu (`ConversationView.axaml`). Context menus are invisible until
explored, and the item additionally hides itself when `CanBatchImportVo` is false — so a
user who right-clicks at the wrong moment sees nothing and may never learn the feature
exists.

## Decision

Add a "Batch import VO…" item to the **Test** menu in `MainWindow.axaml`, directly after
Validate Voice-Over (the Test menu is the established home of VO tooling: F5/F6 sync,
backup restore, Validate Voice-Over). The canvas context-menu item stays as a shortcut.

Rejected alternatives: a toolbar button (toolbar is for canvas-editing actions; an
icon-only button needs more explanation than a labelled menu item) and menu + toolbar
combined (duplicates the toolbar downsides for no discoverability gain over the menu item).

## Design

### Menu item

- Location: `MainWindow.axaml`, Test menu, after the `Menu_ValidateVO` item (same
  separator block — it is VO tooling).
- Header: reuse the existing `Menu_BatchImportVo` string so the menu and context-menu
  labels can never drift apart.
- `Command="{Binding Canvas.BatchImportVoCommand}"` — the command lives on
  `ConversationViewModel` (`Canvas` property of `MainWindowViewModel`).
- **No `IsEnabled` binding and no `IsVisible` binding.** Avalonia `MenuItem`s auto-disable
  from `ICommand.CanExecuteChanged`; `BatchImportVoCommand.NotifyCanExecuteChanged()` is
  already raised on project-path change (`ConversationViewModel.cs:28`) and node-collection
  change (`ConversationViewModel.cs:83`). Visible-but-disabled is the deliberate
  discoverability behaviour — users see the feature exists even when it cannot run.

### Tooltip

New string `ToolTip_Menu_BatchImportVo_Main` (the context item keeps its existing
`ToolTip_Menu_BatchImportVo` — it hides rather than disables, so it never needs to explain
being greyed out):

> Open the batch voice-over import dialog for this conversation. Requires an open project
> and a conversation with at least one voiced node.

Set as both `ToolTip.Tip` and `AutomationProperties.HelpText`, per project rules.

### ViewModel

No changes. `BatchImportVoCommand`, its `CanBatchImportVo` gate, and the
`ShowBatchVoImport` delegate wiring in `MainWindow.axaml.cs` are shared by both entry
points as-is.

### Gaps.md

Rewrite the "only reachable via canvas right-click" entry to record the resolution
(Test-menu item added 2026-07-04; context menu kept as shortcut). The "all conversations"
entry stays open — scope decision 2026-07-04: entry point only, orphaned
`Menu_BatchImportVoAll` strings kept for the future variant.

## Testing

`CanBatchImportVo` is already unit-covered and no new logic is introduced — the change is
declarative XAML plus strings. Verification is manual:

1. Test menu shows "Batch import VO…" after Validate Voice-Over.
2. Disabled with no project open; disabled on a conversation with no voiced nodes.
3. Enabled on `08_cv_atsura` with a project open; opens the same dialog as the context item.
4. Tooltip appears and names the enablement conditions.
5. Canvas context-menu path still works unchanged.
