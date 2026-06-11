# Layer 2 — Runtime Theme / Palette Selection (design)

**Status:** Implemented 2026-06-11.
**Predecessors:** Layer 0 (`2026-06-07-colour-token-taxonomy-design.md`), Layer 1
(`2026-06-08-layer1-palette-sets-design.md`, `2026-06-09-layer1-highcontrast-design.md`).

## 1. Goal

Let the user pick one of the four Layer 1 palettes (Dark / Light / Colourblind /
High-Contrast) at runtime, persist the choice, and apply it live **across both GUI apps**
(the editor and the standalone PatchManager). Layer 1 authored the palettes but merged them
nowhere; this layer is the "bulk of the work" that turns them on.

Non-goal: Layer 2.5 (redundant non-colour encoding) — separate gap.

## 2. The switching mechanism

`Tokens.axaml` resolves palette colours with `{StaticResource Palette.*}`, which binds **once
at load**. So replacing only the palette dictionary leaves the already-built `Brush.*`
brushes on their old colours. The swap therefore:

1. Removes whatever currently provides the palette/token keys (located by sentinel keys —
   `Palette.Neutral.80` and `Brush.Surface.Window` — so it is robust to merge order, working
   whether the live entries are App.axaml's two separate `ResourceInclude`s on first call or a
   prior swap's wrapper afterwards).
2. Inserts one wrapper `ResourceDictionary` whose merged children are the chosen
   `Palette.<set>.axaml` **then a freshly reloaded `Tokens.axaml`**, as siblings, so Tokens'
   StaticResource refs resolve against the new palette (mirroring App.axaml's layout). Building
   the wrapper in code keeps the palette catalogue as the single add-point — no per-palette
   wrapper files.
3. Sets `Application.RequestedThemeVariant` (Light→`Light`; Dark/Colourblind/HighContrast→
   `Dark`) so the FluentTheme base controls follow.

`{DynamicResource Brush.*}` consumers (all chrome) retint automatically. **Converter-driven
canvas node colours** can't use `DynamicResource` (the brush key is computed from speaker
category + display type), so a `ThemeService.Current.Revision` `int` is bumped on each swap and
fed as a throwaway extra value into the node-colour MultiBindings; the changed value re-fires
the binding → the converter re-resolves the new brush. The speaker-dot `Fill` was converted to
a MultiBinding for the same reason (`SpeakerCategoryToBrushConverter` gained an
`IMultiValueConverter` overload that ignores the tick).

## 3. Components

- `AppSettings.Theme` (`DialogEditor.ViewModels`) — persisted `string`, default `"Dark"`.
- `IThemeApplier` + `ThemeOption` (`DialogEditor.ViewModels/Services`) — framework-agnostic
  seam (`Available`, `Apply(id)`), so `ThemePickerViewModel` is unit-testable with a stub.
- `ThemePickerViewModel` + `ThemeChoice` (`DialogEditor.ViewModels`) — persists to
  `AppSettings.Theme` and calls `IThemeApplier.Apply` on change (mirrors
  `SettingsViewModel.OnLocalizationFormatChanged`).
- `ThemeApplier` + `ThemeService` (`DialogEditor.Avalonia.Shared/Theming`) — the swap above
  plus the revision tick; the catalogue maps id → palette file + `ThemeVariant` + loc key.
- `ThemePickerView` (UserControl, `DialogEditor.Avalonia.Shared`) — labelled, tooltipped
  ComboBox; hosted by the editor's `SettingsWindow` and a top bar in PatchManager's window.
  Strings live in `SharedStrings.axaml`.
- Both `App.axaml.cs` call `new ThemeApplier().Apply(AppSettings.Theme)` before the first
  window is shown.

## 4. Cross-app behaviour

The choice is live in the running app and persisted; the other app picks it up at next launch
(no cross-process push). Both apps host their own picker.

## 5. Tests

- `AppSettingsThemeTests` — default + round-trip.
- `ThemePickerViewModelTests` — catalogue sourced from applier, init from settings, change
  persists + applies (stub applier).
- `ThemeApplierTests` (headless) — Light retints `Brush.Surface.Window` (proves Tokens
  reloaded, not just palette swapped), variant mapping, revision bump, Dark restores.
- `BrushConverterTests` — the new `SpeakerCategoryToBrushConverter` multi-value overload.

## 6. Incidental fix

The standalone PatchManager's `app.ico` was declared `<None>` (copied to output) rather than
`<AvaloniaResource>`, so its window `Icon="avares://…/app.ico"` threw `FileNotFoundException`
at startup — the standalone app could not launch at all. Corrected to embed `Assets\**` as an
`AvaloniaResource`, mirroring the editor. Predates this work (commit 242ba5e); surfaced because
Layer 2 added a PatchManager picker that required launching the app to verify.
