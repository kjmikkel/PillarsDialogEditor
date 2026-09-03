# UI Automation Addressability Audit — 2026-09-03

Phase 1 of [issue #15](https://github.com/kjmikkel/PillarsDialogEditor/issues/15).

Live walk of the app's UI Automation (UIA) control-view tree, taken to establish what is
actually unreachable, ambiguous, or anonymous — rather than inferring it from source greps.
Two probes were run against a Debug build via `tools/ui-automation/DriveApp.ps1`:

1. the shell projectless (189 elements) and with a project open (190 elements);
2. each of the five app menus expanded in turn (194–205 elements per surface).

Method: recursive `TreeWalker.ControlViewWalker` descent from the main window, recording
`Name`, `ControlType`, `AutomationId`, `ClassName`, `IsEnabled`, `IsKeyboardFocusable`,
`BoundingRectangle`, and supported patterns for every element, plus the ancestor path so a
repeated `Name` can be judged as ambiguous-within-a-surface rather than merely reused across
surfaces that are never visible at once.

---

## Correction to the issue's original premise

The issue as filed claimed the docking panes carry no automation names. **That is wrong.**
The live tree shows the docking shell is named throughout, sourced from the Dock `Id`/title
in `DialogEditor.Avalonia/Docking/EditorDockFactory.cs` rather than from
`AutomationProperties.Name` in XAML — which is why a grep for `AutomationProperties` in
`MainWindow.axaml` missed it:

| ControlType | Name | AutomationId | ClassName |
|---|---|---|---|
| Pane | `Dock host` | — | `DockControl` |
| Pane | `Root dock` | — | `RootDockControl` |
| Pane | `Conversations` | `LeftPane` | `ToolControl` |
| Pane | `Canvas` | `Documents` | `DocumentControl` |
| Pane | `Node Details` | `RightPane` | `ToolControl` |
| Pane | `Pinned dock host` | — | `PinnedDockControl` |
| Tab | `Tool tabs` | `LeftPane` / `RightPane` | `ToolTabStrip` |
| Tab | `Document tabs` | `Documents` | `DocumentTabStrip` |
| TabItem | `Canvas` / `Node Details` / `Condition search` | `Canvas` / `Details` / `ConditionSearch` | — |

Pane scoping is therefore already possible. This is exactly why Phase 1 was made blocking:
Phase 2 would have churned names that were never broken.

---

## Finding 1 — The conversation tree is not programmatically operable (blocker)

The Conversations pane holds **37 `TreeItem` elements, every one of them with an empty
`Name` and no supported patterns at all** — no `SelectionItem`, no `ExpandCollapse`, no
`Invoke`. The visible label lives on a child `Text` element instead:

```
TreeItem  name=''  focusable=True  patterns=<none>
  Button  name=''            id='PART_ExpandCollapseChevron'
  Text    name='00_prototype' id=''
```

Consequences:

- `FindFirst(Name == '07_neketaka_temple_district')` returns the inner **`Text`**, which
  cannot be selected or expanded.
- Even once found, the `TreeItem` exposes no pattern, so there is no programmatic way to
  select or expand it. Synthetic clicking at its bounding rectangle is the only option.
- All 37 expand chevrons are anonymous and share `AutomationId='PART_ExpandCollapseChevron'`.

This is the app's primary navigation surface — choosing which conversation to open — and it
is unreachable by name and inoperable by pattern. It is equally a screen-reader and
keyboard-navigation defect, not only an automation one, so it should be treated as the
highest-priority item in the issue.

## Finding 2 — Six pane-chrome buttons are named after an Avalonia type

Each `ToolControl` pane header exposes three buttons whose accessible name is the literal
string `Avalonia.Controls.Viewbox`:

| Name | AutomationId | Count |
|---|---|---|
| `Avalonia.Controls.Viewbox` | `PART_MenuButton` | 2 |
| `Avalonia.Controls.Viewbox` | `PART_PinButton` | 2 |
| `Avalonia.Controls.Viewbox` | `PART_CloseButton` | 2 |

A leaked type name is meaningless to a screen reader and untranslatable, so this violates the
**Localisation** rule as well as identifiability. The `AutomationId`s are duplicated across
the left and right panes, so id does not disambiguate them either — only pane scoping does.

These are template parts of the docking library, not controls declared in this repo's XAML,
which is why `AutomationNameTests` (an `.axaml` scanner) cannot see them. **Any structural
test for this class of defect has to assert against the live UIA tree, not the markup.**

## Finding 3 — Undo/Redo menu items are bare glyphs

The Edit menu exposes Undo and Redo as `MenuItem`s named `↩` and `↪`. `AutomationNameTests`
covers `Button`/`ToggleButton` only, so `MenuItem` glyph headers slip through the existing
enforcement. Announced as punctuation by a screen reader; matched by an opaque glyph in an
automation script.

## Finding 4 — No menu item carries an AutomationId

Across all five menus (51 menu items: 5 top-level, plus File 15, Edit 12, View 4, Test 7,
Help 8), **every single item has an empty `AutomationId`** — 0 of 51. Menu items are
addressable *only* by their localised `Name`.

This means every GUI verification run is silently coupled to the UI language and to exact
label wording, including the ellipsis character — `Merge Projects…` with `…`, not three
dots. Rewording a label or running under a non-English locale breaks the harness with a
"not found" error that looks like a missing control rather than a renamed one.

Adding a stable, non-localised `AutomationId` to each menu item would decouple addressing
from display text. It is invisible to users and to assistive tech, so it costs nothing in
UX terms.

## Finding 5 — The OS system menu is indistinguishable from the app's menus

`Get-MenuItemStates` in `DriveApp.ps1` matches on `ControlType == MenuItem` window-wide, so
it returns the title bar's `System Menu Bar` item (`Name='System'`, `AutomationId='Item 1'`)
alongside File/Edit/View/Test/Help. It appears in all five menu dumps.

This is not cosmetic: the first audit probe enumerated menu names that way, clicked
`System`, opened the OS window menu, and **wedged the run** — the main-window walk collapsed
to 12 elements and every subsequent lookup for `File`, `Edit`, `View`, `Test`, `Help` failed
with "not found". The probe had to be rewritten to scope enumeration to the app's own menu.

The app's own menu can only be selected by `ClassName='Menu'` today, because that `Menu`
element is **anonymous** — it is the one genuinely unnamed container in the shell. Giving it
a name (and/or an `AutomationId`) would let the harness scope menu queries without relying
on a class name.

## Finding 6 — Confirmed same-surface Name collisions

Duplicated `Name`s within the single project-open surface, i.e. genuinely ambiguous for a
`FindFirst` by name:

| Name | Count | ControlTypes | AutomationIds |
|---|---|---|---|
| `Avalonia.Controls.Viewbox` | 6 | Button | `PART_MenuButton`, `PART_PinButton`, `PART_CloseButton` |
| `Canvas` | 3 | Pane / TabItem / Text | `Documents`, `Canvas`, — |
| `Node Details` | 3 | Pane / TabItem / Text | `RightPane`, `Details`, — |
| `Condition search` | 2 | TabItem / Text | `ConditionSearch`, — |
| `Language:` | 2 | Text / ComboBox | —, — |
| `Opened project '…' (0 patches)` | 2 | Text / Text | `StatusLiveRegion`, — |
| `Position` | 2 | Thumb / Thumb | —, — |
| `Tool tabs` | 2 | Tab / Tab | `LeftPane`, `RightPane` |

Two of these deserve comment:

- **`Language:`** — the label `TextBlock` and the `ComboBox` it labels share a name. This is
  the collision `DriveApp.ps1` currently works around by filtering lookups on
  `ControlType.Edit`. The workaround is coincidental, not guaranteed: here the control is a
  `ComboBox`, not an `Edit`, so the filter would not have saved it.
- **`Opened project '…'`** — the status text is duplicated between `StatusLiveRegion` and an
  anonymous sibling `TextBlock`. A screen reader may announce the status twice.

The pane/tab/text triples (`Canvas`, `Node Details`, `Condition search`) are arguably benign
— a pane and its tab legitimately share a title — but they still mean a bare name lookup
must specify a `ControlType` to be deterministic.

### Duplicated AutomationIds

`AutomationId` is not unique either, so it cannot be used as a sole address:

| AutomationId | Count | Distinct names |
|---|---|---|
| `PART_ExpandCollapseChevron` | 37 | *(none — all anonymous)* |
| `PART_MenuButton` / `PART_PinButton` / `PART_CloseButton` | 2 each | `Avalonia.Controls.Viewbox` |
| `LeftPane` / `RightPane` | 2 each | `Conversations`/`Tool tabs`, `Node Details`/`Tool tabs` |
| `Documents` | 2 | `Canvas`, `Document tabs` |
| `PART_LineUpButton` / `PART_LineDownButton` | 2 each | `Line up`/`Column left`, `Line down`/`Column right` |
| `PART_PageUpButton` / `PART_PageDownButton` | 2 each | `Page up`/`Column left`, `Page down`/`Column right` |

---

## What this changes about the plan

1. **Phase 2 loses its pane-naming work** — panes are already named. What remains is
   resolving the collisions in Finding 6 and naming the anonymous app `Menu` (Finding 5).
2. **A new highest-priority item**: make the conversation `TreeItem`s named and
   pattern-operable (Finding 1). This was not in the original four phases.
3. **Phase 3 grows a `MenuItem` glyph rule** (Finding 3) — the existing icon-only-Button
   rule generalises directly to menu headers.
4. **A new item: stable `AutomationId`s on menu items** (Finding 4), to decouple the harness
   from localised display text.
5. **A structural test cannot live in the XAML scanner alone.** Findings 1, 2, and 6 involve
   template parts and generated items that no `.axaml` scan can see. Enforcing them needs an
   assertion against a live UIA tree — a heavier and slower kind of test than
   `AutomationNameTests`, and a design decision in its own right.
6. **`DriveApp.ps1` needs two fixes** regardless of app changes: scope
   `Get-MenuItemStates` to the app's menu rather than every `MenuItem` in the window, and
   stop relying on the `ControlType.Edit` filter as a general disambiguator.

## Not yet covered

The audit did not walk: the modal dialogs and secondary windows (Settings, Find/Replace,
Diff, Branches, and the other ~40 views), the canvas with dialogue nodes present (the
scratch project has zero nodes, so Phase 4's per-node addressability is untested here), the
Condition search tool tab, or the Patch Manager. Extending the walk to those is worthwhile
before Phase 2 is considered fully specified.
