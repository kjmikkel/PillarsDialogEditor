# Table columns that scale with the font (issue 178)

## Problem

The app's hand-built tables are two grids: a header grid, and a row grid repeated in a
ListBox item template. Both copy the same fixed pixel widths (`160,50,110,90,*` in Find in
Project). Text grows with the font scale; the widths do not. At 2.0× headers and cells run
into each other ("NodeField", "CounShare", "12-10-202"). Headers are also drawn at
`FontSize.Small`/`Caption`, smaller than the rows beneath them.

Affected surfaces:

| Window | Columns today |
|---|---|
| Find in Project | `160,50,110,90,*` |
| Attribution (Blame) | `160,60,130,110,90,*` |
| History | `110,140,90,*` (rows only, no header) |
| Speaker Lines | `70,180,90,*` |
| Rep/Disposition Balance | `*,50,70,110` (two tables) |
| Batch VO Import | `140,60,*,50,220,220,50` |
| VO Validation | node cell `Width="70"` |
| Flow Analytics | stat columns and "Playthrough stats" heading: re-check at 2.0× (issue 177 moved it into a scrolling body) and fix whatever still overlaps |

## Decision

**Column widths become tokens that scale with the font scale.** Considered and set aside:

- Content-sized columns shared across rows (`SharedSizeGroup`): only realized rows take part,
  so columns change width while scrolling a long list.
- Avalonia's `DataGrid`: a new package and a rewrite of eight windows. **If overlapping or
  clipped table columns come back after this fix, revisit `DataGrid`** (noted on issue 178
  and its PR).

## Design

### Width tokens

- A new `ColumnWidthTokens.BaseValues` (in `DialogEditor.Avalonia.Shared/Theming`, beside
  `FontSizeTokens`) maps `ColumnWidth.*` keys to their 1.0× width in DIPs. `Tokens.axaml`
  declares the same keys as `x:Double`; a test pins the two together, as `FontSizeTokens` is.
- `FontScaleApplier.Apply(scale)` writes `base × scale` for every `ColumnWidth.*` key, in the
  same loop and dictionary as the font sizes. Live rescale therefore works for free.
- Keys are semantic where a column means the same thing in several windows
  (`ColumnWidth.Conversation`, `ColumnWidth.NodeId`, `ColumnWidth.Author`, `ColumnWidth.Date`,
  `ColumnWidth.CommitSha`, `ColumnWidth.Count`, `ColumnWidth.Share`, …) and window-specific
  otherwise (`ColumnWidth.BatchVo.File`, …).
- Base values start from today's widths and grow only where a header, now at body size, does
  not fit at 1.0× (the clipping test decides).

### Where the width goes

The width sits on the **cell**, not the `ColumnDefinition`: the fixed columns become `Auto`
and each header cell and row cell gets `Width="{DynamicResource ColumnWidth.X}"`. A
`ColumnDefinition` is not a `StyledElement`, so a `DynamicResource` on its `Width` is not
reliably resolved; a cell is. Star columns stay `*`. Header and row cells in the same column
use the same token and the same horizontal margin, so they line up by construction; a test
checks it.

Batch VO Import's VO and Alias columns hold a nested grid (text plus buttons): the width goes
on that nested grid.

### Long values and headers

- Every table cell that can be wider than its column keeps or gains
  `TextTrimming="CharacterEllipsis"` and gets `ToolTip.Tip` bound to the same value, so a
  trimmed value is still readable.
- Column headers use `FontSize.Body`, the row size. Find in Project, Rep/Disposition Balance
  and Speaker Lines change their `col-header` style; Attribution and Batch VO Import change
  their inline header sizes.

### Comments

Each changed table carries a comment citing issue 178 and explaining that the widths are
font-scaled tokens shared by header and rows.

## Testing

All headless, at 1.0× and 2.0×:

1. **Clipping (existing `NoClippedTextTests`).** Remove all `TrackedByIssue178` exemptions; they
   are the first failing tests. Add the table windows to its `Populated` cases with one or two
   rows each, so row cells are checked as well as headers.
2. **Header/row alignment (new).** In each populated table, each header cell starts at the
   same x as the row cell beneath it (within 0.5 DIP).
3. **Headers at row size (new).** Each header cell's `FontSize` ≥ its row cell's.
4. **Width tokens scale (new).** `FontScaleApplier.Apply(2.0)` doubles every `ColumnWidth.*`
   resource; `Tokens.axaml` and `ColumnWidthTokens.BaseValues` agree.
5. **Trimmed cells have tooltips (new).** In the populated tables, every `TextBlock` with
   `TextTrimming` other than `None` has a non-empty `ToolTip.Tip`.

Done when: those pass, the full headless suite passes, and a re-capture with
`tools/ui-automation/Capture-Windows.ps1` at 1.0× and 2.0× shows no overlap in the surfaces
above.

## Out of scope

- Resizable or sortable columns (that is the `DataGrid` question).
- Tables outside the list above; the clipping test will catch new ones.
