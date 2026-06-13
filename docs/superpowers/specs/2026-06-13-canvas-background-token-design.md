# Canvas Backdrop Colour — `Brush.Canvas.Background`

**Goal:** Rename the misleadingly-named `Brush.Accent.Badge` token (its sole
consumer is the dialog canvas backdrop, not any "badge") to
`Brush.Canvas.Background`, and retint its underlying primitive from a muted
mauve to a neutral grey across all four themes, matching the reference colour
(`#838585`) from the Obsidian dialog editor's canvas.

## Background

`ConversationView.axaml`'s `NodifyEditor` (`x:Name="Editor"`) sets
`Background="{DynamicResource Brush.Accent.Badge}"` — the area behind the
dialog node cards. Despite the name, this is its only consumer; there is no
actual "badge" UI element in the application. The underlying primitive,
`Palette.Mauve.500`, is a muted purple (`#7A6A8E` in Dark/Light/Colourblind,
`#332A47` in High-Contrast).

While reviewing a screenshot of the Obsidian dialog editor (the original
Pillars of Eternity dev tool), the user noted its canvas backdrop is a flat
neutral grey, `#838585`. Since the canvas backdrop colour was already under
discussion, the user decided to move all four themes toward this neutral
reference rather than keep the mauve tint, and approved renaming the token to
reflect what it actually is.

Two mockups were reviewed and approved via the brainstorming visual companion:
Dark-theme A/B comparison (mauve vs. neutral grey, node cards unchanged), and
a four-theme comparison showing the final proposed values for Dark, Light,
Colourblind, and High-Contrast.

## Design

### 1. Semantic token rename

In `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`, rename the brush
key:

```xml
<!-- before -->
<SolidColorBrush x:Key="Brush.Accent.Badge" Color="{StaticResource Palette.Mauve.500}"/>

<!-- after -->
<SolidColorBrush x:Key="Brush.Canvas.Background" Color="{StaticResource Palette.Neutral.520}"/>
```

The single consumer, `DialogEditor.Avalonia/Views/ConversationView.axaml`
(`NodifyEditor`'s `Background`), is updated from
`{DynamicResource Brush.Accent.Badge}` to
`{DynamicResource Brush.Canvas.Background}`.

### 2. Primitive rename + retint

`Palette.Mauve.500` is renamed to `Palette.Neutral.520`, following the
existing numeric Neutral-ramp naming convention (e.g. `Palette.Neutral.225`,
`.400`, `.535` already exist in `Palette.Dark.axaml` with no collision at
`520`). "Mauve" no longer describes a grey value, so the name must change
alongside the colour.

New values, defined per-palette in `Palette.Dark.axaml`, `Palette.Light.axaml`,
`Palette.Colourblind.axaml`, and `Palette.HighContrast.axaml`:

| Palette | Old (`Palette.Mauve.500`) | New (`Palette.Neutral.520`) |
|---|---|---|
| Dark | `#FF7A6A8E` | `#FF838585` |
| Light | `#FF7A6A8E` | `#FF838585` |
| Colourblind | `#FF7A6A8E` | `#FF838585` |
| High-Contrast | `#FF332A47` | `#FF3A3A3A` |

Dark/Light/Colourblind all share the reference grey `#838585` directly (as the
mauve was shared today). High-Contrast keeps a distinct, darker neutral
(`#3A3A3A`) chosen to preserve roughly the same lightness as the old
`#332A47`, so the existing grid-line (`Palette.Line.Default`, `#A8A8A8`) and
white node-card contrast ratios carry over. The High-Contrast comment in
`Palette.HighContrast.axaml` explaining *why* this value is darker than the
other three (grid-line and node-card contrast) is preserved, updated to
reference the new hex value.

### 3. Dependent test/doc updates

- **`DialogEditor.Tests/Theming/PaletteContrastTests.cs`** — the
  `HcBorderPairs` entry `("Palette.Line.Default", "Palette.Mauve.500")` (with
  its explanatory comment about the canvas backdrop / GridSplitter visibility)
  is updated to reference `Palette.Neutral.520`. The test itself computes the
  contrast ratio from whatever hex is currently defined, so it will validate
  `#A8A8A8` vs `#3A3A3A` (≈4.78:1) against the ≥4.5:1 gate automatically — no
  hand-coded ratio to update.
- **`DialogEditor.Tests/Theming/TokenRegistryTests.cs`** — the `AllTokens`
  list entry `"Brush.Accent.Badge"` is renamed to `"Brush.Canvas.Background"`.
  No `InlineData`-driven exact-colour assertion currently exists for this
  token, so no further change is needed there.
- **`DialogEditor.Tests/Theming/palette-golden.approved.txt`** — the four
  `Palette.<Set>\tPalette.Mauve.500\t#FF<value>` lines (one per theme) are
  regenerated to `Palette.Neutral.520\t#FF<new value>` via the standard
  approval workflow: run `PaletteGoldenTests`, inspect the generated
  `.received.txt` for correctness, then copy it over `.approved.txt`.

### Out of scope

- `Brush.Node.*` tokens (NPC/Player/Narrator/Script/Bark header/body/footer
  colours) are unaffected — only the canvas backdrop behind the node cards
  changes.
- `PaletteSetParityTests` is unaffected: the rename applies uniformly across
  all four palettes, so parity (every palette defines the same set of keys)
  is preserved.
- No new localized strings, no layout changes, no new UI elements.

## Testing

Following TDD: first update `PaletteContrastTests.cs` and
`TokenRegistryTests.cs` to reference the new names (this turns those tests
red, since `Palette.Neutral.520` / `Brush.Canvas.Background` don't exist yet),
then perform the rename/retint across the five `.axaml` resource files
(turning them green), then regenerate `palette-golden.approved.txt` via the
approval workflow, then run the full `dotnet test` suite to confirm no
regressions.

## Tech Stack

C#/.NET 8, Avalonia 11.3.14, xUnit (`PaletteContrastTests`,
`TokenRegistryTests`, `PaletteGoldenTests` — `DialogEditor.Tests/Theming`).
