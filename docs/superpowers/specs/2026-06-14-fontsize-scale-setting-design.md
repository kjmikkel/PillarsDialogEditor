# FontSize Scale Setting + Resizable Dialogs — Design Spec

**Gaps.md item 6 (part B):** "there is still no UI-scale-factor setting, and fixed-size
windows (`SettingsWindow` is `CanResize="False" Height="220"`) will clip under OS text
scaling. Opportunity: add a scale factor in Settings that multiplies the `FontSize.*`
tokens, and make fixed-height windows resizable or auto-sizing."

This spec covers the second half of Gaps.md item 6, building on the `FontSize.*` token
foundation (part A, already implemented — see
`docs/superpowers/specs/2026-06-14-fontsize-token-foundation-design.md`).

## Goals

- A user-facing **font scale** setting (100/125/150/175/200%) in `SettingsWindow`, applied
  to all `FontSize.*` tokens at next app startup.
- A **live preview** in `SettingsWindow` showing sample text at the selected scale, so the
  user can judge proportions before restarting — independent of the global token
  dictionary, which remains static until restart.
- The 12 `CanResize="False"` dialogs become resizable with sensible minimum sizes and
  auto-growing height, so they don't clip when the font scale (or, incidentally, content
  length) makes their content taller than the original fixed height.

## Architecture Overview

### Font scale application (restart-required, startup-only)

`FontSize.*` tokens are bound via `StaticResource` throughout the app (349 occurrences
across 30 files, per the part-A spec). `StaticResource` resolves at the moment a control's
XAML is instantiated, against whatever value is currently in the resource dictionary.

This means: if the `FontSize.*` entries in the live `Tokens.axaml` dictionary are mutated
**before any window is constructed**, every window opened during that session — including
dialogs opened later — resolves the mutated (scaled) value. No `StaticResource` →
`DynamicResource` rewrite is needed.

Changing the setting mid-session only persists the new value to `settings.json`. It is
**not** re-applied to the live resource dictionary, so already-open windows (and any
opened before a restart) keep the scale that was active at launch. `SettingsWindow` shows
a "restart to apply" notice when the selected value differs from the value that was active
at launch.

### Live preview (no restart needed)

The preview area in `SettingsWindow` uses ordinary data-bound `FontSize` values computed
from the *selected* (not yet applied) scale, via `INotifyPropertyChanged` on
`SettingsViewModel`. This is completely independent of the `FontSize.*` resource tokens —
it updates instantly as the dropdown changes, while the rest of the app (including
`SettingsWindow`'s own chrome) stays on the launch-time scale until restart.

### Resizable dialogs

All 12 `CanResize="False"` windows get `CanResize="True"` and a `MinWidth` equal to their
current fixed `Width` (so they never render narrower than today). The 4 that lack
`SizeToContent="Height"` get it added, matching the other 8 that already have it — so
height grows with scaled/longer content, and width is a manual resize away if needed.

## Components

### 1. `FontSizeTokens` — shared base-value table (new)

**File:** `DialogEditor.Avalonia.Shared/Theming/FontSizeTokens.cs`

```csharp
namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Canonical unscaled FontSize.* token values (Tokens.axaml). Single source of truth
/// shared by FontScaleApplier (computes scaled values) and FontSizeTokenTests (pins the
/// unscaled values) so the two cannot drift apart.
/// </summary>
public static class FontSizeTokens
{
    public static readonly IReadOnlyDictionary<string, double> BaseValues = new Dictionary<string, double>
    {
        ["FontSize.Caption"]  = 9,
        ["FontSize.Small"]    = 10,
        ["FontSize.Label"]    = 11,
        ["FontSize.Body"]     = 12,
        ["FontSize.Medium"]   = 13,
        ["FontSize.Subtitle"] = 14,
        ["FontSize.Title"]    = 18,
        ["FontSize.Display"]  = 32,
    };
}
```

`FontSizeTokenTests` is updated to source its expected values from
`FontSizeTokens.BaseValues` instead of hardcoded `[InlineData]` literals (or to assert the
two stay equal), per the design's "single source of truth" goal.

### 2. `FontScaleApplier` (new)

**File:** `DialogEditor.Avalonia.Shared/Theming/FontScaleApplier.cs`

Mirrors `ThemeApplier`'s pattern of locating the live `Tokens.axaml` dictionary by sentinel
key and mutating it in place:

```csharp
namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Applies a font-size scale factor to the live FontSize.* tokens, once at startup
/// (after ThemeApplier.Apply, which reloads Tokens.axaml and would otherwise reset any
/// earlier scaling). Every FontSize.* StaticResource binding resolves against the
/// mutated dictionary for windows constructed afterwards — see design spec
/// docs/superpowers/specs/2026-06-14-fontsize-scale-setting-design.md.
/// </summary>
public sealed class FontScaleApplier
{
    private const string FontSizeSentinel = "FontSize.Body";

    public void Apply(double scale)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("No Application is running to apply a font scale to.");

        var dict = FindDictionaryContaining(app.Resources, FontSizeSentinel)
            ?? throw new InvalidOperationException($"No resource dictionary defines '{FontSizeSentinel}'.");

        foreach (var (key, baseValue) in FontSizeTokens.BaseValues)
            dict[key] = baseValue * scale;
    }

    private static IResourceDictionary? FindDictionaryContaining(IResourceDictionary root, string key)
    {
        if (root.ContainsKey(key)) return root;
        if (root is ResourceDictionary rd)
            foreach (var merged in rd.MergedDictionaries)
                if (merged is IResourceDictionary child &&
                    FindDictionaryContaining(child, key) is { } found)
                    return found;
        return null;
    }
}
```

(Exact recursion signature to be finalized against Avalonia's `IResourceDictionary`/
`ResourceDictionary` API during implementation — the principle is a recursive search
through `MergedDictionaries` for the dictionary defining `FontSize.Body`, then direct
indexer writes for all 8 keys.)

### 3. `AppSettings.FontScale` (modify)

**File:** `DialogEditor.ViewModels/Services/AppSettings.cs`

Add to `SettingsData`:

```csharp
public double FontScale { get; set; } = 1.0;
```

Add property, following the `Theme` pattern exactly:

```csharp
public static double FontScale
{
    get => Load().FontScale;
    set { var s = Load(); s.FontScale = value; Save(s); }
}
```

### 4. `App.axaml.cs` wiring (modify)

After `new ThemeApplier().Apply(AppSettings.Theme)` and before `new MainWindow()`:

```csharp
new FontScaleApplier().Apply(AppSettings.FontScale);
```

Ordering matters: `ThemeApplier.Apply` replaces the `Tokens.axaml` dictionary instance
with a freshly-loaded (unscaled) copy, so `FontScaleApplier` must run after it.

### 5. `FontScaleToPercentConverter` (new)

**File:** `DialogEditor.Avalonia/Converters/FontScaleToPercentConverter.cs`

`IValueConverter` formatting a `double` scale (e.g. `1.25`) as a percentage string
(`"125%"`) for the `ComboBoxItem` display, avoiding any hardcoded literal label strings.

### 6. `SettingsViewModel` additions (modify)

**File:** `DialogEditor.ViewModels/ViewModels/SettingsViewModel.cs`

```csharp
[ObservableProperty] private double _selectedFontScale;

public IReadOnlyList<double> FontScaleOptions { get; } = [1.0, 1.25, 1.5, 1.75, 2.0];

private readonly double _appliedFontScale; // captured at construction = AppSettings.FontScale

public double PreviewBodyFontSize     => SelectedFontScale * 12;
public double PreviewSubtitleFontSize => SelectedFontScale * 14;
public double PreviewTitleFontSize    => SelectedFontScale * 18;

public bool ShowRestartNotice => SelectedFontScale != _appliedFontScale;

partial void OnSelectedFontScaleChanged(double value)
{
    AppSettings.FontScale = value;
    OnPropertyChanged(nameof(PreviewBodyFontSize));
    OnPropertyChanged(nameof(PreviewSubtitleFontSize));
    OnPropertyChanged(nameof(PreviewTitleFontSize));
    OnPropertyChanged(nameof(ShowRestartNotice));
}
```

Constructor initializes `_selectedFontScale = _appliedFontScale = AppSettings.FontScale`.

### 7. `SettingsWindow.axaml` additions (modify)

- New row (after the localization-format `DockPanel`): `ComboBox` bound to
  `FontScaleOptions`/`SelectedFontScale`, items displayed via
  `FontScaleToPercentConverter`.
- New preview area: a bordered panel containing 3 `TextBlock`s bound to
  `PreviewBodyFontSize`/`PreviewSubtitleFontSize`/`PreviewTitleFontSize`, with new
  localized sample-text resource keys (e.g. `Settings_FontScalePreviewBody`,
  `..PreviewSubtitle`, `..PreviewTitle`) in `Strings.axaml`/`.resx`.
- New `TextBlock` bound to `ShowRestartNotice` (`IsVisible` binding), showing a new
  localized `Settings_FontScaleRestartNotice` string.
- Window-level changes (see Resizable Dialogs below): add `SizeToContent="Height"`,
  replace `CanResize="False"` with `CanResize="True"`, add `MinWidth="500"`, and change
  `Grid RowDefinitions="*,Auto,Auto"` to `"Auto,Auto,Auto"` (now that the window sizes to
  content, the content row no longer needs to be `*`).

## Resizable Dialogs (12 windows)

All 12 get `CanResize="False"` → `CanResize="True"` and `MinWidth` = their current `Width`
value. The 4 lacking `SizeToContent="Height"` get it added (and their fixed `Height`
attribute removed).

**Group A — already `SizeToContent="Height"`, add `CanResize="True"` + `MinWidth`:**

| Window | Current `Width` → `MinWidth` |
|---|---|
| `AboutWindow.axaml` | 420 |
| `BranchNameDialog.axaml` | 400 |
| `CommitConsentDialog.axaml` | 460 |
| `ConflictResolutionDialog.axaml` | 520 |
| `ConversationNameDialog.axaml` | 460 |
| `ForceDeleteDialog.axaml` | 420 |
| `ImportWarningsDialog.axaml` | 440 |
| `UnsavedChangesDialog.axaml` | 420 |

**Group B — add `SizeToContent="Height"` (remove fixed `Height`), `CanResize="True"` +
`MinWidth`:**

| Window | Current `Width`/`Height` → `MinWidth` (Height removed) |
|---|---|
| `ExportConversationsWindow.axaml` | Width 480, Height 520 → MinWidth 480 |
| `FindReplaceWindow.axaml` | Width 420, Height 200 → MinWidth 420 |
| `LanguageCodeDialog.axaml` | Width 340, Height 160 → MinWidth 340 |
| `SettingsWindow.axaml` | Width 500, Height 220 → MinWidth 500 |

`ImportWarningsDialog.axaml` keeps its existing `MaxHeight="520"` (caps growth from a long
warning list); `CommitConsentDialog.axaml` keeps its internal `ScrollViewer` `MaxHeight="160"`
for the same reason — neither changes.

## Testing Strategy

1. **RED first**, per CLAUDE.md TDD:
   - `AppSettingsTests`: `FontScale` get/set round-trip, default `1.0`.
   - `FontScaleApplierTests` (`[AvaloniaFact]`): after `Apply(1.25)`, `FontSize.Body`
     resolves to `15.0`, `FontSize.Title` to `22.5`, etc., for all 8 keys in
     `FontSizeTokens.BaseValues`.
   - `FontSizeTokenTests`: update to source expected values from
     `FontSizeTokens.BaseValues` (still pins the 8 unscaled values).
   - `SettingsViewModel` tests: `SelectedFontScale` → `AppSettings.FontScale` persistence;
     `FontScaleOptions == [1.0, 1.25, 1.5, 1.75, 2.0]`; `Preview*FontSize` values and
     change notifications; `ShowRestartNotice` false initially, true after a change.
   - `FontScaleToPercentConverter` test: `1.25 → "125%"` etc.
   - New `ResizableDialogTests` (`[AvaloniaTheory]`, one row per window): asserts
     `CanResize == true`, `SizeToContent.HasFlag(SizeToContent.Height)`, and `MinWidth`
     equals the value from the tables above, for all 12 windows.

2. **GREEN**: implement each component until its tests pass. `App.axaml.cs` wiring is
   covered transitively (no direct unit test — `FontScaleApplierTests` covers the applier
   itself; `App.axaml.cs` ordering is a one-line addition next to the existing
   `ThemeApplier` call).

3. **Final**: `dotnet build` (0 warnings/errors) and full `DialogEditor.Tests` suite green
   (currently 1374 tests; expect ~1374 + new tests, no regressions).

## Out of Scope

- Live/no-restart application of the font scale to already-open windows. The dropdown only
  persists `AppSettings.FontScale`; `FontScaleApplier` runs once, at next startup.
- Rewriting any `FontSize.*` `StaticResource` bindings to `DynamicResource`.
- Reading or reacting to the OS-level display/text-scaling setting — `FontScale` is a
  purely app-internal preference.
- `SizeToContent="WidthAndHeight"` or any per-window layout rework beyond
  `CanResize`/`MinWidth`/`SizeToContent="Height"`.
- Any visual/layout redesign of the 12 dialogs beyond those three attributes.
- `ThemePickerView`'s own sizing.
