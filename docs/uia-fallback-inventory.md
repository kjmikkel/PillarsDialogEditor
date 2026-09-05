# UIA Fallback Inventory

Every place the MCP server resorts to synthetic input because the app exposes no usable UI
Automation pattern. Each entry names the issue that removes it. **Deleting a fallback is part
of the definition of done for the corresponding issue phase** — otherwise these calcify into
permanent furniture.

| # | Fallback | Why it exists | Removed when | Test that will fail first |
|---|---|---|---|---|
| 1 | Synthetic click to open a top-level menu | App `MenuItem`s expose `ScrollItem` only — no `Invoke`, no `ExpandCollapse` | [#15](https://github.com/kjmikkel/PillarsDialogEditor/issues/15) gives menu items an operable pattern | `ActionToolsGuiTests.TopLevelMenuItemsStillLackInvokeAndExpandCollapse` |
| 2 | Synthetic click to activate a menu command | Same — the leaf item has no `Invoke` either | #15, same fix | same test |
| 3 | Synthetic click to select a conversation row | `TreeItem`s expose `Scroll`/`ScrollItem` only — no `SelectionItem` | #15 finding 1 | `ActionToolsGuiTests.ConversationRowsStillLackSelectionItem` |
| 4 | Menu addressing by localised `Name` | 0 of 51 menu items carry an `AutomationId` | #15 finding 4 | none yet — add one with the fix |
| 5 | App menu located by `ClassName='Menu'` | The app's `Menu` element is anonymous | #15 names the menu | `ActionToolsGuiTests.TheAppMenuIsFoundAndExcludesTheOsSystemItem` (would need inverting) |
| 6 | `FocusAndType` in `set_value` | Some fields expose no `Value` pattern | per-control, as found | none — data-dependent |
| 7 | `{ESC}` dismissal before every menu walk | Synthetic clicking is stateful; a leftover popup swallows the next click | fallbacks 1–2 gone | none — becomes unnecessary rather than wrong |

## How to remove one

1. Fix the app so the pattern or name exists.
2. Run the Gui test named above. **It should now fail** — that is the signal, by design.
3. Delete the fallback branch in `ActionStrategy` and the warning text with it.
4. Invert the Gui test to assert the pattern is now present.
5. Delete the row from this table.

## Why the fallbacks warn instead of staying quiet

An element reachable only by clicking its rectangle is a defect a screen-reader user hits
too. A harness that silently routed around it would let the app get less accessible while
verification runs got greener. Reporting keeps the signal load-bearing.

**Warnings must not cry wolf, though.** A false positive is as corrosive as a missing
warning, because it teaches the reader to skim past them. Phase 3 shipped one by accident:
`Activate` originally preferred only `Invoke`/`Toggle`/`SelectionItem`, so a `ComboBox` —
which legitimately exposes `ExpandCollapse` and no `Invoke` — was clicked and reported as an
addressability defect. Activating a ComboBox *means* expanding it. `ExpandCollapse` is now
the last `Activate` preference, and `ActionToolsGuiTests.AComboBoxIsOperatedByPatternWithoutAWarning`
guards against the regression.

The rule that follows: before adding a warning, check that the pattern the control *does*
expose isn't the semantically correct route.

## Not a fallback, but a known blind spot

The server's tree is rooted at the main window, so secondary windows — dialogs, Settings,
Diff, Branches, the Patch Manager — are invisible to `read_tree`, `find`, `menu` and every
tool built on them, and `screenshot` captures only the main window's rect. `invoke_menu` can
therefore *open* a dialog it cannot then inspect. Tracked separately as
[#16](https://github.com/kjmikkel/PillarsDialogEditor/issues/16); it is a limitation of the
tooling rather than of the app's addressability.
