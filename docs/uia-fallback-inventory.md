# UIA Fallback Inventory

Every place the MCP server resorts to synthetic input because the app exposes no usable UI
Automation pattern. Each entry names the issue that removes it. **Deleting a fallback is part
of the definition of done for the corresponding issue phase** — otherwise these calcify into
permanent furniture.

| # | Fallback | Why it exists | Removed when | Test that will fail first |
|---|---|---|---|---|
| 1 | Synthetic click to open a top-level menu | App `MenuItem`s expose `ScrollItem` only — no `Invoke`, no `ExpandCollapse` | [#15](https://github.com/kjmikkel/PillarsDialogEditor/issues/15) gives menu items an operable pattern | `ActionToolsGuiTests.TopLevelMenuItemsStillLackInvokeAndExpandCollapse` |
| 2 | Synthetic click to activate a menu command | Same — the leaf item has no `Invoke` either | #15, same fix | same test |
| ~~3~~ | ~~Synthetic click to select a conversation row~~ | **RESOLVED 2026-09-05.** Rows are named from their data and a custom automation peer supplies `SelectionItem`/`ExpandCollapse` | — | `ActionToolsGuiTests.ConversationRowsAreSelectableAndExpandableByPattern` + `ActivatingAConversationRowUsesAPatternNotASyntheticClick` |
| ~~4~~ | ~~Menu addressing by localised `Name`~~ | **RESOLVED 2026-09-05.** All 51 menu items (and 9 context-menu items) now carry stable non-localised `AutomationId`s; `MenuNavigator` matches id first, then name | — | `MenuItemAutomationIdTests` (markup) + `ActionToolsGuiTests.MenuItemsExposeStableAutomationIdsToUia` (live tree) |
| ~~5~~ | ~~App menu located by `ClassName='Menu'`~~ | **RESOLVED 2026-09-05.** The menu bar carries `AutomationId='MainMenu'` and the name "Main menu"; all six call sites now address it by id | — | `MenuItemAutomationIdTests.EveryMenuContainerCarriesAnAutomationId` + `ActionToolsGuiTests.TheAppMenuIsFoundAndExcludesTheOsSystemItem` |
| 6 | `FocusAndType` in `set_value` | Some fields expose no `Value` pattern | per-control, as found | none — data-dependent |
| 7 | `{ESC}` dismissal before every menu walk | Synthetic clicking is stateful; a leftover popup swallows the next click | fallbacks 1–2 gone | none — becomes unnecessary rather than wrong |

## Retired fallbacks

**#4 — menu addressing by localised name (2026-09-05).** The first fallback retired, and it
followed the procedure below exactly. `AutomationProperties.AutomationId` was added to all 51
`MainWindow.axaml` menu items and the 9 `ConversationView.axaml` context-menu items, enforced
by `MenuItemAutomationIdTests`, and `MenuNavigator` now resolves a path segment against the
id before the name. `invoke_menu ["MenuHelp","MenuHelp_About"]` opens the About window with no
localised text anywhere in the call.

Worth noting what the enforcement test had to get *right*: the rule is the exact inverse of
`AutomationNameTests`. A `Name` is spoken to the user so it MUST be a localised resource; an
`AutomationId` is never shown so it MUST NOT be, or the locale coupling comes straight back.
Both directions are now structurally enforced, plus id uniqueness within a view — a duplicated
id could not disambiguate anything, which is the entire point of adding them.

**#3 — synthetic click to select a conversation row (2026-09-05).** Issue #15 finding 1, in
two halves.

The *name* half was markup: `AutomationProperties.Name` bound to `DisplayName` on the
`TreeViewItem` container via `ItemContainerTheme`. Without it a row's accessible name was
empty, the label living only on the templated `TextBlock`, so a lookup for a conversation
resolved to that inner `Text` — and a screen reader announced nothing.

The *pattern* half was an upstream framework gap. Avalonia 11.3's
`TreeViewItemAutomationPeer` overrides only `GetAutomationControlTypeCore()` and implements no
provider interfaces, so rows exposed neither `ISelectionItemProvider` nor
`IExpandCollapseProvider`. `ListItemAutomationPeer` (used by `TabItem`) *does* implement
selection, which is why tabs were operable and tree rows were not — the Win32 bridge was never
the problem. `DialogEditor.Avalonia/Controls/ConversationTreeView.cs` supplies the missing
providers locally; **delete all three types when Avalonia ships them upstream.** The upstream
report is drafted and parked in
[#17](https://github.com/kjmikkel/PillarsDialogEditor/issues/17), not yet filed.

Two traps worth remembering if this is ever revisited:

- `TreeViewItem.CreateContainerForItemOverride` delegates to its owning `TreeView`, so one
  override on the `TreeView` subclass covers containers at every depth.
- Subclassing a templated control changes its style key. Without `StyleKeyOverride` pointing
  back at the base type, the subclass matches no `ControlTheme`, gets no template, and renders
  nothing — the tree went from 37 rows to zero UIA elements before that was added.

Verified live: `SelectionItem` and `ExpandCollapse` both present, `Select()` flips `IsSelected`
False → True, `invoke` reports `ok: used the SelectionItem pattern.` with no warning, and a
screenshot confirms the row highlights with no visual regression.

**#5 — app menu located by `ClassName='Menu'` (2026-09-05).** Issue #15 finding 5. The menu
bar was anonymous, so the only handle was its framework class name — and that scoping is what
keeps the title bar's OS "System" item from being treated as a peer of
File/Edit/View/Test/Help. Depending on an Avalonia class name for something that load-bearing
was fragile.

It now carries `AutomationProperties.AutomationId="MainMenu"` plus a localised
`AutomationProperties.Name` ("Main menu") for assistive tech. The `ClassName` lookup is
**deleted**, not softened, from all six call sites: `MenuNavigator`, `InspectionTools`, two Gui
tests and `DriveApp.ps1`'s `Get-MenuItemStates`. `MenuNavigator.FindAppMenu` is now the single
shared lookup rather than the same predicate written twice.

The three `ContextMenu` containers in `ConversationView.axaml` were equally anonymous and got
ids too (`CanvasContextMenu`, `NodeContextMenu`, `ConnectionContextMenu`), matching their
items' prefixes — a caller can now scope a query to one context menu. They get **no** spoken
name on purpose: assistive tech announces a context menu by role, and their items carry the
meaning, so a name would only be announced redundantly.

Verified live via both paths — the server's `menu` tool and `DriveApp.ps1` — each returning
exactly five top-level items with no `System`, and the bar announcing "Main menu".

**Finding 6 — same-surface name collisions (2026-09-05): investigated, no app change.**
Not a retired fallback but worth recording, because the honest answer was "our markup is not
the problem".

Six collisions became four, and the two that went were **false positives in our own warning
logic**: a `Window` with its own `TitleBar`, and the two splitter `Thumb`s both named
"Position". Neither is something a caller can act on, so neither could ever be "the one you
meant". Ambiguity is now judged only among elements exposing an *actionable* pattern
(`Invoke`, `Toggle`, `SelectionItem`, `ExpandCollapse`, `Value`, `RangeValue`, `Selection`) or
keyboard focus — `Scroll`/`ScrollItem` deliberately do not count, since nearly everything has
them and counting them would make every label look operable.

`DuplicateLabel` is now restricted to genuine `Text`. It had briefly reported the window and
its title bar as "duplicated static elements", which was wrong twice over. It also ignores
zero-size elements, because `MainWindow` deliberately pairs a hidden
`AutomationProperties.LiveSetting="Polite"` region with the visible status label so a change
is ANNOUNCED while the visible one is READ. UIA's `LiveSetting` is not exposed by the .NET
client, so `ElementInfo.HasSize` (empty bounding rectangle) is the discriminator.

The four survivors are all outside our markup or benign:

| Collision | Source | Verdict |
|---|---|---|
| `Tool tabs` ×2 | Dock.Avalonia `ToolTabStrip` — the string is not in our source at all | third-party, see finding 2 |
| `Avalonia.Controls.Viewbox` ×6 | Dock.Avalonia template parts | third-party, IS finding 2 |
| `Canvas` (Pane + TabItem) | our dock titles | benign: a pane and its tab share a title, and the tab text is what the user reads |
| `Node Details` (Pane + TabItem) | our dock titles | benign, same |

All four are disambiguated by `controlType` or `automationId`, which the resolver already
demands — so the practical cost is one narrowing argument, not a broken lookup. Renaming a
pane to differ from its own tab would make the UI worse to fix a non-problem.

**Finding 2 — `Avalonia.Controls.Viewbox` chrome buttons (2026-09-05): fixed in our styles,
no upstream report needed.** Dock.Avalonia's `ToolChromeControl` template gives each pane
chrome button a `<Viewbox>` as its `Content`, and Avalonia's `ButtonAutomationPeer` derives
the accessible name from `Content.ToString()` — so all six announced
"Avalonia.Controls.Viewbox".

Unlike finding 1's pattern half, this needed no subclassing and no upstream change. A
`Style` in `App.axaml` reaches into the third-party template the same way the Dock theme
styles its own parts:

```xml
<Style Selector="dock|ToolChromeControl /template/ Button#PART_CloseButton">
    <Setter Property="AutomationProperties.Name" Value="{DynamicResource AutomationName_PaneClose}"/>
</Style>
```

Two ordering constraints: the styles must come **after** the `DockFluentTheme` `StyleInclude`
to win, and the `dock` xmlns must be on the root element because this Avalonia version rejects
`xmlns` on non-root elements (AXN0002).

The buttons now announce "Pane options", "Pin pane" and "Close pane", and the `TypeNameLeak`
warning is gone. Note the collision count did **not** drop: one warning about six buttons
sharing a meaningless name became three warnings about pairs sharing meaningful names, one per
pane. That is the real structure surfacing rather than a regression — the accessibility defect
is fixed, and `withinPane` scoping is the natural way to address pane chrome anyway.

*Possible follow-up:* pane-qualified names ("Close Node Details pane") would remove the
residual duplication for screen-reader users too, but need a localisable format string bound
to the pane title — real complexity for marginal gain, so not done.

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
