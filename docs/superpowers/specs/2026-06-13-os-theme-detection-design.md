# OS Theme Detection ("Auto") — design

**Status:** Approved 2026-06-13, not yet implemented.
**Predecessor:** Layer 2 (`2026-06-11-layer2-runtime-theme-design.md`) — the runtime palette
switcher this builds on.
**Source:** Gaps.md item 11(a) — "Theme doesn't follow the OS." Item 11(b) (small hit
targets) is already implemented; the Dark-palette AA-contrast half of item 11 is split off
as a separate follow-up Gaps entry, as is the first-run theme-picker onboarding dialog idea
raised during brainstorming.

## 1. Goal

Add a `"Auto"` theme option that resolves to the OS's reported colour-scheme preference
(light/dark) and high-contrast preference, with **high-contrast taking priority over
light/dark**. Make `"Auto"` the default for fresh installs, so a new user's first launch
already roughly matches their OS setup — without requiring any new onboarding UI.

Non-goals:
- Live retinting when the OS preference changes while the app is running (resolution
  happens at apply-time — i.e. on launch, or immediately when the user picks "Auto" from
  the picker — not via an OS change-notification subscription).
- Detecting an OS "colour-blind mode" — Windows exposes no such preference to apps, so
  `Colourblind` remains a manual-only choice.
- A first-run onboarding dialog presenting all palettes with previews (separate Gaps entry).
- Bringing the Dark palette to AA contrast (separate Gaps entry — item 11's other half).

## 2. Resolution rule

Given Avalonia's `PlatformColorValues` (from `IPlatformSettings.GetColorValues()`):

```
ContrastPreference == High        → "HighContrast"
ThemeVariant        == Light       → "Light"
otherwise (Dark, or no platform
  settings available)              → "Dark"
```

High-contrast wins outright, regardless of the reported light/dark variant — this matches
the existing `HighContrast` palette, which is itself authored against `ThemeVariant.Dark`
(per `ThemeApplier`'s catalog) and is not light/dark-variant-aware.

If `Application.Current.PlatformSettings` or `GetColorValues()` returns `null` (e.g. some
headless/test hosts), fall back to `"Dark"` — matching today's hardcoded historical default.

## 3. Components

### `ThemeApplier` (`DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs`)

- `Catalog` (the four real palettes: Dark/Light/Colourblind/HighContrast) is **unchanged**.
- A new internal pure function maps OS state to one of the four catalog ids:

  ```csharp
  internal static string DetectOsThemeId(PlatformColorValues? values)
  {
      if (values is null) return "Dark";
      if (values.ContrastPreference == ColorContrastPreference.High) return "HighContrast";
      return values.ThemeVariant == PlatformThemeVariant.Light ? "Light" : "Dark";
  }
  ```

  Being a pure function over a directly-constructible `PlatformColorValues`, this is
  unit-tested without any headless-platform dependency.

- `Apply(string id)` gains a resolution step at the top:

  ```csharp
  var resolvedId = id == "Auto"
      ? DetectOsThemeId(Application.Current?.PlatformSettings?.GetColorValues())
      : id;
  var entry = Catalog.FirstOrDefault(e => e.Id == resolvedId) ?? Catalog[0];
  // ...unchanged from here
  ```

- `Available` gains a new `"Auto"` entry **prepended** (so it's the first/default choice in
  the picker), with display key `Theme_Name_Auto`. It is *not* added to `Catalog` — it has
  no palette file of its own and is never the `entry` actually applied.

### `AppSettings` (`DialogEditor.ViewModels/Services/AppSettings.cs`)

- `SettingsData.Theme` default changes from `"Dark"` to `"Auto"`.
- Existing users with a persisted `settings.json` are unaffected — their saved value
  (`"Dark"`, `"Light"`, etc.) is read as before. Only fresh installs (no settings file yet)
  get `"Auto"`.

### `App.axaml.cs` (both `DialogEditor.Avalonia` and `DialogEditor.PatchManager`)

No changes. Both already call `new ThemeApplier().Apply(AppSettings.Theme)` before the
first window is shown; `Apply` now transparently resolves `"Auto"`.

### `ThemePickerViewModel` / `ThemePickerView`

No logic changes. `Available` (sourced from the applier) already drives the ComboBox.
Selecting "Auto":
1. Persists `AppSettings.Theme = "Auto"`.
2. Calls `Apply("Auto")`, which re-resolves against the *current* OS reading and retints
   immediately — giving instant visual feedback on selection, while still being
   "startup-only" in the sense that no OS change-notification subscription exists.

### Localisation (`SharedStrings.axaml`)

New string `Theme_Name_Auto`, e.g. "System Default" — sits alongside the existing
`Theme_Name_Dark` / `Theme_Name_Light` / `Theme_Name_Colourblind` / `Theme_Name_HighContrast`
keys.

## 4. Tests

- `ThemeApplierTests`:
  - New `DetectOsThemeId` cases: high-contrast wins over light variant; high-contrast wins
    over dark variant; `Light` + `NoPreference` → `"Light"`; `Dark`/`NoPreference` →
    `"Dark"`; `null` → `"Dark"`.
  - `Available_ListsThePalettesInOrder` updated to expect `["Auto", "Dark", "Light",
    "Colourblind", "HighContrast"]`.
  - New `Apply_Auto_...` tests covering that `Apply("Auto")` ends up applying the palette
    `DetectOsThemeId` would pick for the current (headless) platform settings, and that the
    resulting `RequestedThemeVariant`/resources match that palette's entry — restoring
    `"Dark"` in a `finally` per the existing convention.

- `AppSettingsTests`:
  - `Theme_DefaultsToDark` → `Theme_DefaultsToAuto`, asserting `AppSettings.Theme ==
    "Auto"` on a fresh settings file.

- `ThemePickerViewModelTests`: review `SelectedTheme_WhenAppSettingsUnknown_FallsBackToFirst`
  — its stub catalogue (`["Dark", "Light"]`) doesn't include `"Auto"`, so it should be
  unaffected, but confirm after the `ThemeApplier` changes that nothing implicitly assumed
  `"Dark"` was first.

## 5. Follow-ups split out of Gaps item 11

- **Dark palette AA contrast** — `PaletteContrastTests` grandfathers Dark out of its AA
  checks; a few pairs (e.g. `Severity.Error` / `Surface.Panel` ≈ 2.8:1) sit below 4.5:1.
  Separate Gaps entry; iterative palette-value tuning + golden snapshot regeneration.
- **First-run theme-picker onboarding dialog** — show Light/Dark/Auto (and possibly
  Colourblind/HighContrast) with live previews on first launch. New Gaps entry; its own
  brainstorm (scope: which palettes shown, preview rendering, first-run flag, blocking vs.
  non-blocking).
