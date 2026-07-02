# VO Import Dialog Layout Consolidation Design

**Date:** 2026-07-02
**Status:** Approved
**Scope:** `VoImportDialog.axaml`, `VoImportDialog.axaml.cs`, `Strings.axaml` (+ translated
resource dictionaries). View layer only — `IVoImporter`, `VoImportPaths`, `IVoAudioPlayer`,
`NodeDetailViewModel`, and `BatchVoImportDialog` are unchanged.

---

## Problem

The dialog grew across three specs (import → preview/encoding → batch) into two stacked
sections — "Current voice-over" on top, "Import" below — each repeating the same
Primary/Female rows. One variant's state is split across two places with up to four
disconnected play buttons. The layout groups by *action* (listen vs. import) where users
think in *entities* (the Primary line, the Female line).

## Solution — "Current → New" grid (Option B)

Mockup reviewed and approved 2026-07-02 (artifact `vo-import-layout-options`,
version `initial-three-options`).

One grid, one row per variant, columns reading left-to-right as a replacement:

```
┌─ Import Voice-Over ────────────────────────────────────────────────┐
│  Pick a .wem (copied directly) or .wav (encoded with Wwise).       │
│                                                                    │
│            CURRENT                    NEW                          │
│  Primary   [▶] testline_0001.wem      [▶] eder_take3.wav           │
│                                           [Browse…] [✕]            │
│  Female    [▶] testline_0001_fem.wem  No file chosen  [Browse…]    │
│                                                                    │
│  Quality   ○ Low   ● Medium   ○ High                               │
│                                                                    │
│                                            [Cancel]  [Import]     │
└────────────────────────────────────────────────────────────────────┘
```

### Layout

- Single `Grid` with columns `[label 68px] [Current *] [New ~1.35*]`; one row per variant
  plus a header row of small uppercase column labels (Current / New).
- The italic instruction line stays above the grid — it is the only place the dual-format
  story (.wem copied / .wav encoded) is told.
- Below the grid, unchanged from today: quality radios (visible only when any picked source
  is `.wav`), Wwise warning panel (`.wav` picked and Wwise absent), Cancel/Import buttons.
- The two `SemiBold` section headers and the `Separator` are removed.

### Current-column collapse

- The Current column **and** the header row are visible only if at least one current file
  exists on disk when the dialog opens (`primaryExists || femExists` — the same check the
  constructor performs today). Visibility is fixed per-open; files cannot appear mid-dialog.
- Collapsed: the grid is `[label] [New]` with no column headers, and the window opens at
  its current 500px width. Expanded: the window opens at ~640px so both filename columns
  have room. Width is set once in the constructor.
- Within a visible Current column, a variant that has a resolvable path but no file on disk
  shows a muted em-dash placeholder (localised string) and no play button.

### Cell behaviour

- **Current cell:** ▶ button + filename, secondary foreground. Play button visible only
  when the file exists. Same `TogglePlay` / `PlayingSlot` logic as today — all four slot
  values (`CurrentPrimary`, `CurrentFem`, `SourcePrimary`, `SourceFem`) survive unchanged.
- **New cell:** ▶ preview button (hidden until a file is picked), filename or
  "No file chosen" placeholder, `Browse…` button, and a `✕` icon button replacing the
  text "Clear" button. The ✕ keeps the existing `ToolTip_VoImportClear_*` tooltip and
  gains an `AutomationProperties.Name`.
- Female row visible only when `FemDestinationPath` is not null (node has female text) —
  unchanged.
- OK gating, quality selection, Wwise warning, and file-picker filters are behaviourally
  identical to today; only their surrounding layout changes.

### Strings (`Strings.axaml` + translations)

Added:

| Key | English value |
|-----|---------------|
| `VoImport_ColCurrent` | `Current` |
| `VoImport_ColNew` | `New` |
| `VoImport_NoCurrentFile` | `—` |
| `AutomationName_VoImportClear_Primary` | `Clear chosen primary file` |
| `AutomationName_VoImportClear_Fem` | `Clear chosen female file` |

Removed (no longer referenced): `VoImport_CurrentSection`, `VoImport_ImportSection`.

All other keys (`VoImport_PrimaryLabel`, `VoImport_FemaleLabel`, `VoImport_NoFileChosen`,
`VoImport_ImportInstruction`, tooltips, automation names, quality labels, …) are reused.

## Testing

No ViewModel surface changes — `NodeDetailViewModelImportTests` are unaffected and must
stay green. The change is XAML plus constructor visibility/width logic in code-behind,
verified manually:

- [ ] Node with existing primary + fem VO → three-column grid, headers visible, both
      Current ▶ buttons play, window ~640px
- [ ] Node with no VO on disk → Current column and headers absent, window 500px
- [ ] Node with fem path but only primary file on disk → fem Current cell shows the
      muted "—" placeholder without a play button
- [ ] Node without female text → Female row absent entirely
- [ ] Pick a `.wav` → preview ▶ appears, quality radios appear; Wwise absent adds warning
      and disables Import
- [ ] ✕ clears the picked file, hides its preview button, stops playback if that slot
      was playing
- [ ] All four play buttons toggle ▶/■ correctly and stop each other via the shared player
- [ ] Import/Cancel behave exactly as before (`Result` populated / null)

## Out of Scope

- `BatchVoImportDialog` (separate window, separate layout)
- Any change to import mechanics, encoding, or `_vo/` conventions
- Entry points (🎤 button, context menu)
