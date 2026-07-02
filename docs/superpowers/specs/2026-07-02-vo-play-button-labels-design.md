# VO Play Button Variant Labels Design

**Date:** 2026-07-02
**Status:** Approved
**Scope:** `NodeDetailViewModel`, `NodeDetailView.axaml`, `Strings.axaml`,
`NodeDetailViewModelPlaybackTests`. Detail panel only — the Import Voice-Over and
batch import dialogs already disambiguate variants with row/column labels.

---

## Problem

The VO status row in the node detail panel shows two visually identical play buttons —
both render `▶` (or `■` while playing). Only the hover tooltip says which is the
primary (male) file and which is the female variant.

## Solution — letter suffix inside the button

Approved over full words (too wide for the narrow detail panel, worse when localised)
and gender symbols (♂/♀ — poorly readable at small sizes).

```
Voice-over: ✓ Found   [▶ M] [▶ F]  [🎤]

playing primary:
Voice-over: ✓ Found   [■ M] [▶ F]  [🎤]
```

### ViewModel (`NodeDetailViewModel`)

- Rename `PlayPrimaryGlyph` → `PlayPrimaryLabel`, `PlayFemGlyph` → `PlayFemLabel`.
- Values compose the existing play/stop state with a localised variant letter:
  - `PlayPrimaryLabel`: `"▶ " + Loc.Get("VoPlay_MaleLetter")` idle,
    `"■ " + Loc.Get("VoPlay_MaleLetter")` while primary is playing.
  - `PlayFemLabel`: same with `VoPlay_FemaleLetter`, keyed to female playback.
- `SetPlaying` raises `OnPropertyChanged` for the renamed properties.
- Rationale for composing in the ViewModel instead of XAML: one source of truth next
  to the existing glyph state machine, and directly unit-testable (strict TDD applies).

### View (`NodeDetailView.axaml`)

- The two buttons' `Content` bindings rename to `PlayPrimaryLabel` / `PlayFemLabel`.
- Tooltips, `AutomationProperties`, commands, visibility (`CanPlayAudio` /
  `CanPlayFem`), margins — all unchanged.

### Strings (`Strings.axaml`)

| Key | English value |
|-----|---------------|
| `VoPlay_MaleLetter` | `M` |
| `VoPlay_FemaleLetter` | `F` |

## Testing (red first)

In `NodeDetailViewModelPlaybackTests`:

- `PlayPrimaryLabel_Idle_ShowsPlayGlyphWithMaleLetter` — `"▶ M"`.
- `PlayPrimaryLabel_WhilePlayingPrimary_ShowsStopGlyph_FemUnaffected` — primary
  `"■ M"`, female still `"▶ F"`.
- `PlayFemLabel_WhilePlayingFem_ShowsStopGlyph_PrimaryUnaffected` — female `"■ F"`,
  primary `"▶ M"`.

Existing tests referencing the old property names are updated mechanically.

## Out of Scope

- Import and batch VO dialogs (already labelled).
- Any playback behaviour change.
