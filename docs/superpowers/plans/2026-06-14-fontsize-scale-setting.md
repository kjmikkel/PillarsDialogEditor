# FontSize Scale Setting + Resizable Dialogs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a restart-required font-scale setting (100/125/150/175/200%) with a live preview in `SettingsWindow`, applied to all `FontSize.*` tokens at startup, and make the 12 previously fixed-size dialogs resizable so they don't clip at larger scales.

**Architecture:** A new `FontScaleApplier` (mirroring the existing `ThemeApplier`) mutates the live `Tokens.axaml` resource dictionary's `FontSize.*` entries in place at app startup, before any window is constructed — so every `StaticResource` binding resolves the scaled value for the rest of the session. `AppSettings.FontScale` persists the choice; `SettingsViewModel` exposes a live, independent preview computed from the *selected* (not-yet-applied) scale. The 12 `CanResize="False"` dialogs get `CanResize="True"`, `MinWidth` = their old fixed `Width`, and `SizeToContent="Height"` (4 of them lack it today).

**Tech Stack:** Avalonia 11 (headless XUnit tests), CommunityToolkit.Mvvm, C#/.NET.

**Spec:** `docs/superpowers/specs/2026-06-14-fontsize-scale-setting-design.md`

---

### Task 1: Shared `FontSizeTokens` base-value table

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Theming/FontSizeTokens.cs`
- Modify: `DialogEditor.Tests/Theming/FontSizeTokenTests.cs`

- [ ] **Step 1: Create the shared base-value table**

```csharp
namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Canonical unscaled FontSize.* token values (Tokens.axaml). Single source of truth
/// shared by FontScaleApplier (computes scaled values) and FontSizeTokenTests (pins the
/// unscaled values) so the two cannot drift apart. See design spec
/// docs/superpowers/specs/2026-06-14-fontsize-scale-setting-design.md.
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

- [ ] **Step 2: Update `FontSizeTokenTests.cs` to source its expected values from `FontSizeTokens.BaseValues`**

`[InlineData]` values must be compile-time constants, so the per-key `[AvaloniaTheory]` is
replaced with a single `[AvaloniaFact]` that iterates the shared table:

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

public class FontSizeTokenTests
{
    private static double Size(string key)
    {
        Assert.True(
            Application.Current!.TryGetResource(key, Application.Current!.ActualThemeVariant, out var v),
            $"FontSize key '{key}' is not defined");
        return Assert.IsType<double>(v);
    }

    [AvaloniaFact]
    public void AllTokens_ResolveToBaseValues()
    {
        foreach (var (key, expected) in FontSizeTokens.BaseValues)
            Assert.Equal(expected, Size(key));
    }

    [AvaloniaFact]
    public void Micro_WasRetired_NoLongerDefined()
        => Assert.False(Application.Current!.TryGetResource("FontSize.Micro", Application.Current!.ActualThemeVariant, out _));
}
```

- [ ] **Step 3: Run the test to confirm it still passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~FontSizeTokenTests`
Expected: PASS (2 tests) — this is a refactor of an already-green test, not a new behaviour.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Theming/FontSizeTokens.cs DialogEditor.Tests/Theming/FontSizeTokenTests.cs
git commit -m "refactor(theming): extract FontSize base-value table shared by tests and scaling"
```

---

### Task 2: `FontScaleApplier`

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Theming/FontScaleApplier.cs`
- Test: `DialogEditor.Tests/Theming/FontScaleApplierTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Exercises FontScaleApplier against the shared headless Application. Because that
/// Application is an assembly-wide singleton (FontSizeTokenTests asserts base values),
/// every test restores scale 1.0 in a finally block.
/// </summary>
public class FontScaleApplierTests
{
    private static double Size(string key)
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource(key, app.ActualThemeVariant, out var v),
            $"FontSize key '{key}' did not resolve");
        return Assert.IsType<double>(v);
    }

    [AvaloniaFact]
    public void Apply_ScalesAllFontSizeTokens()
    {
        try
        {
            new FontScaleApplier().Apply(1.25);
            foreach (var (key, baseValue) in FontSizeTokens.BaseValues)
                Assert.Equal(baseValue * 1.25, Size(key));
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void Apply_OneScale_RestoresBaseValues()
    {
        new FontScaleApplier().Apply(1.5);
        new FontScaleApplier().Apply(1.0);
        foreach (var (key, baseValue) in FontSizeTokens.BaseValues)
            Assert.Equal(baseValue, Size(key));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~FontScaleApplierTests`
Expected: FAIL — `FontScaleApplier` does not exist (compile error).

- [ ] **Step 3: Implement `FontScaleApplier`**

```csharp
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

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

        var dict = FindDictionary(app.Resources, FontSizeSentinel)
            ?? throw new InvalidOperationException($"No resource dictionary defines '{FontSizeSentinel}'.");

        foreach (var (key, baseValue) in FontSizeTokens.BaseValues)
            dict[key] = baseValue * scale;
    }

    private static IResourceDictionary? FindDictionary(IResourceProvider provider, string key)
    {
        var dict = provider as IResourceDictionary
            ?? (provider as ResourceInclude)?.Loaded as IResourceDictionary;

        if (dict is null) return null;
        if (dict.ContainsKey(key)) return dict;

        foreach (var merged in dict.MergedDictionaries)
            if (FindDictionary(merged, key) is { } found)
                return found;

        return null;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~FontScaleApplierTests`
Expected: PASS (2 tests)

- [ ] **Step 5: Run the FontSize token tests to confirm no cross-test pollution**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~FontSizeTokenTests`
Expected: PASS (2 tests) — confirms the `finally { Apply(1.0) }` cleanup restores base values for other tests.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Theming/FontScaleApplier.cs DialogEditor.Tests/Theming/FontScaleApplierTests.cs
git commit -m "feat(theming): add FontScaleApplier to scale FontSize.* tokens at startup"
```

---

### Task 3: `AppSettings.FontScale`

**Files:**
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs`
- Test: `DialogEditor.Tests/Services/AppSettingsTests.cs`

- [ ] **Step 1: Write the failing test**

Add a new test class to `DialogEditor.Tests/Services/AppSettingsTests.cs`, following the
existing `AppSettingsThemeTests` pattern exactly:

```csharp
public class AppSettingsFontScaleTests : IDisposable
{
    public AppSettingsFontScaleTests()
        => AppSettings.SettingsPathOverride = Path.GetTempFileName();

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    [Fact]
    public void FontScale_DefaultsTo1()
    {
        Assert.Equal(1.0, AppSettings.FontScale);
    }

    [Fact]
    public void FontScale_RoundTrips()
    {
        AppSettings.FontScale = 1.5;
        Assert.Equal(1.5, AppSettings.FontScale);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~AppSettingsFontScaleTests`
Expected: FAIL — `AppSettings.FontScale` does not exist (compile error).

- [ ] **Step 3: Add `FontScale` to `AppSettings`**

In `DialogEditor.ViewModels/Services/AppSettings.cs`, add to `SettingsData` (after the
`Theme` property):

```csharp
        // The font-scale multiplier applied to every FontSize.* token at next startup
        // (Gaps item 6 part B). 1.0 = no scaling, the historical/default size. Changing
        // this only persists the value; FontScaleApplier applies it once at the next
        // launch, so already-open windows are unaffected until restart.
        public double FontScale                      { get; set; } = 1.0;
```

Add the static property (after `Theme`):

```csharp
    public static double FontScale
    {
        get => Load().FontScale;
        set { var s = Load(); s.FontScale = value; Save(s); }
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~AppSettingsFontScaleTests`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/AppSettings.cs DialogEditor.Tests/Services/AppSettingsTests.cs
git commit -m "feat(settings): add AppSettings.FontScale (default 1.0)"
```

---

### Task 4: Wire `FontScaleApplier` into app startup

**Files:**
- Modify: `DialogEditor.Avalonia/App.axaml.cs`

- [ ] **Step 1: Add the `FontScaleApplier` call after `ThemeApplier`**

In `DialogEditor.Avalonia/App.axaml.cs`, change:

```csharp
            Loc.Configure(new AvaloniaStringProvider());
            // Apply the persisted theme before the first window is shown (overrides the
            // design-time Dark default baked into App.axaml).
            new ThemeApplier().Apply(AppSettings.Theme);
            desktop.MainWindow = new MainWindow();
```

to:

```csharp
            Loc.Configure(new AvaloniaStringProvider());
            // Apply the persisted theme before the first window is shown (overrides the
            // design-time Dark default baked into App.axaml).
            new ThemeApplier().Apply(AppSettings.Theme);
            // Scale FontSize.* tokens before any window is constructed, so every
            // StaticResource FontSize binding (including dialogs opened later this
            // session) resolves the scaled value. Must run after ThemeApplier, which
            // reloads Tokens.axaml and would otherwise reset this. Restart-required:
            // changing the setting mid-session does not re-run this (Gaps item 6 part B).
            new FontScaleApplier().Apply(AppSettings.FontScale);
            desktop.MainWindow = new MainWindow();
```

`FontScaleApplier` is in `DialogEditor.Avalonia.Shared.Theming`, which is already imported
via the existing `using DialogEditor.Avalonia.Shared.Theming;` (used by `ThemeApplier`).

- [ ] **Step 2: Build and run the full test suite to confirm no regressions**

Run: `dotnet build DialogEditor.slnx`
Expected: 0 warnings, 0 errors

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: All tests pass (this startup path doesn't run under headless tests — `OnFrameworkInitializationCompleted`'s `IClassicDesktopStyleApplicationLifetime` branch is desktop-only — so no new test is needed here; `FontScaleApplierTests` already covers the applier itself).

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/App.axaml.cs
git commit -m "feat(settings): apply FontScale at startup, after theme application"
```

---

### Task 5: `FontScaleToPercentConverter`

**Files:**
- Create: `DialogEditor.Avalonia/Converters/FontScaleToPercentConverter.cs`
- Test: Create `DialogEditor.Tests/Converters/FontScaleToPercentConverterTests.cs`
- Modify: `DialogEditor.Avalonia/App.axaml`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Globalization;
using DialogEditor.Avalonia.Converters;

namespace DialogEditor.Tests.Converters;

public class FontScaleToPercentConverterTests
{
    [Theory]
    [InlineData(1.0,  "100%")]
    [InlineData(1.25, "125%")]
    [InlineData(1.5,  "150%")]
    [InlineData(1.75, "175%")]
    [InlineData(2.0,  "200%")]
    public void Convert_FormatsAsPercent(double scale, string expected)
    {
        var result = new FontScaleToPercentConverter().Convert(scale, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_NonDouble_ReturnsNull()
    {
        var result = new FontScaleToPercentConverter().Convert("not a double", typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~FontScaleToPercentConverterTests`
Expected: FAIL — `FontScaleToPercentConverter` does not exist (compile error).

- [ ] **Step 3: Implement the converter**

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

/// Formats a FontScale multiplier (1.0, 1.25, ...) as a percentage string ("100%",
/// "125%") for the Settings font-scale picker, avoiding a hardcoded label per option.
public sealed class FontScaleToPercentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double scale ? $"{scale * 100:0}%" : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~FontScaleToPercentConverterTests`
Expected: PASS (6 tests)

- [ ] **Step 5: Register the converter in `App.axaml`**

In `DialogEditor.Avalonia/App.axaml`, after the line:

```xml
            <converters:IsNotNullConverter                       x:Key="IsNotNull"/>
```

add:

```xml
            <converters:FontScaleToPercentConverter              x:Key="FontScaleToPercent"/>
```

- [ ] **Step 6: Build to confirm the XAML resource resolves**

Run: `dotnet build DialogEditor.slnx`
Expected: 0 warnings, 0 errors

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Converters/FontScaleToPercentConverter.cs DialogEditor.Tests/Converters/FontScaleToPercentConverterTests.cs DialogEditor.Avalonia/App.axaml
git commit -m "feat(settings): add FontScaleToPercentConverter for the font-scale picker"
```

---

### Task 6: `SettingsViewModel` font-scale properties

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/SettingsViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/LowPriorityViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to the existing `SettingsViewModelTests` class in
`DialogEditor.Tests/ViewModels/LowPriorityViewModelTests.cs`:

```csharp
    [Fact]
    public void FontScaleOptions_ContainsExpectedPresets()
    {
        var vm = new SettingsViewModel("/game", new StubFolderPicker());
        Assert.Equal([1.0, 1.25, 1.5, 1.75, 2.0], vm.FontScaleOptions);
    }

    [Fact]
    public void SelectedFontScale_DefaultsToAppSettingsValue()
    {
        AppSettings.FontScale = 1.5;
        var vm = new SettingsViewModel("/game", new StubFolderPicker());
        Assert.Equal(1.5, vm.SelectedFontScale);
    }

    [Fact]
    public void SelectedFontScale_RoundTripsViaAppSettings()
    {
        var vm = new SettingsViewModel("/game", new StubFolderPicker());
        vm.SelectedFontScale = 1.75;
        Assert.Equal(1.75, AppSettings.FontScale);
    }

    [Fact]
    public void PreviewFontSizes_ScaleWithSelectedFontScale()
    {
        var vm = new SettingsViewModel("/game", new StubFolderPicker());
        vm.SelectedFontScale = 1.5;
        Assert.Equal(18, vm.PreviewBodyFontSize);
        Assert.Equal(21, vm.PreviewSubtitleFontSize);
        Assert.Equal(27, vm.PreviewTitleFontSize);
    }

    [Fact]
    public void ShowRestartNotice_FalseInitially_TrueAfterChange()
    {
        AppSettings.FontScale = 1.0;
        var vm = new SettingsViewModel("/game", new StubFolderPicker());
        Assert.False(vm.ShowRestartNotice);
        vm.SelectedFontScale = 1.25;
        Assert.True(vm.ShowRestartNotice);
    }

    [Fact]
    public void SelectedFontScale_Change_RaisesPreviewAndRestartNoticeNotifications()
    {
        var vm = new SettingsViewModel("/game", new StubFolderPicker());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SelectedFontScale = 1.25;

        Assert.Contains(nameof(SettingsViewModel.PreviewBodyFontSize), raised);
        Assert.Contains(nameof(SettingsViewModel.PreviewSubtitleFontSize), raised);
        Assert.Contains(nameof(SettingsViewModel.PreviewTitleFontSize), raised);
        Assert.Contains(nameof(SettingsViewModel.ShowRestartNotice), raised);
    }
```

(This file already has `using DialogEditor.ViewModels.Services;` for `AppSettings` and
`using System.Collections.Generic;`/`System.ComponentModel` from existing tests in the
same file — add any missing `using` directives the compiler reports.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~SettingsViewModelTests`
Expected: FAIL — `FontScaleOptions`, `SelectedFontScale`, `PreviewBodyFontSize`,
`PreviewSubtitleFontSize`, `PreviewTitleFontSize`, `ShowRestartNotice` do not exist
(compile errors).

- [ ] **Step 3: Implement the properties on `SettingsViewModel`**

In `DialogEditor.ViewModels/ViewModels/SettingsViewModel.cs`, change:

```csharp
public partial class SettingsViewModel : ObservableObject
{
    private readonly string        _gameDirectory;
    private readonly IFolderPicker _picker;

    [ObservableProperty] private string _backupDirectory;
    [ObservableProperty] private string _localizationFormat;

    public SettingsViewModel(string gameDirectory, IFolderPicker picker)
    {
        _gameDirectory      = gameDirectory;
        _picker             = picker;
        _backupDirectory    = AppSettings.GetBackupPath(gameDirectory) ?? string.Empty;
        _localizationFormat = AppSettings.DefaultLocalizationFormat;
    }

    partial void OnLocalizationFormatChanged(string value)
        => AppSettings.DefaultLocalizationFormat = value;
```

to:

```csharp
public partial class SettingsViewModel : ObservableObject
{
    private readonly string        _gameDirectory;
    private readonly IFolderPicker _picker;

    // The scale that FontScaleApplier actually applied at this session's startup —
    // captured once so ShowRestartNotice can detect when the user picks a different
    // value (Gaps item 6 part B).
    private readonly double _appliedFontScale;

    [ObservableProperty] private string _backupDirectory;
    [ObservableProperty] private string _localizationFormat;
    [ObservableProperty] private double _selectedFontScale;

    /// Preset font-scale multipliers offered in Settings.
    public IReadOnlyList<double> FontScaleOptions { get; } = [1.0, 1.25, 1.5, 1.75, 2.0];

    // Live preview sizes, independent of the FontSize.* resource tokens (which stay
    // static until restart) — recomputed from SelectedFontScale as the user picks.
    public double PreviewBodyFontSize     => SelectedFontScale * 12;
    public double PreviewSubtitleFontSize => SelectedFontScale * 14;
    public double PreviewTitleFontSize    => SelectedFontScale * 18;

    /// True once the selected scale differs from the one applied at launch.
    public bool ShowRestartNotice => SelectedFontScale != _appliedFontScale;

    public SettingsViewModel(string gameDirectory, IFolderPicker picker)
    {
        _gameDirectory      = gameDirectory;
        _picker             = picker;
        _backupDirectory    = AppSettings.GetBackupPath(gameDirectory) ?? string.Empty;
        _localizationFormat = AppSettings.DefaultLocalizationFormat;
        _appliedFontScale   = AppSettings.FontScale;
        _selectedFontScale  = _appliedFontScale;
    }

    partial void OnLocalizationFormatChanged(string value)
        => AppSettings.DefaultLocalizationFormat = value;

    partial void OnSelectedFontScaleChanged(double value)
    {
        AppSettings.FontScale = value;
        OnPropertyChanged(nameof(PreviewBodyFontSize));
        OnPropertyChanged(nameof(PreviewSubtitleFontSize));
        OnPropertyChanged(nameof(PreviewTitleFontSize));
        OnPropertyChanged(nameof(ShowRestartNotice));
    }
```

Add `using System.Collections.Generic;` to the top of the file if not already present.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~SettingsViewModelTests`
Expected: PASS (all `SettingsViewModelTests`, including the 6 new ones)

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/SettingsViewModel.cs DialogEditor.Tests/ViewModels/LowPriorityViewModelTests.cs
git commit -m "feat(settings): add font-scale selection and live preview to SettingsViewModel"
```

---

### Task 7: New `Strings.axaml` resource keys

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

- [ ] **Step 1: Add the new localized strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, after the line:

```xml
    <sys:String x:Key="Settings_LocalizationFormat">Default localization format</sys:String>
```

add:

```xml
    <sys:String x:Key="Settings_FontScale">Font scale</sys:String>
    <sys:String x:Key="Settings_FontScaleTooltip">Scales all text in the app. Takes effect after restarting the application.</sys:String>
    <sys:String x:Key="Settings_FontScalePreviewBody">Body text sample</sys:String>
    <sys:String x:Key="Settings_FontScalePreviewSubtitle">Subtitle text sample</sys:String>
    <sys:String x:Key="Settings_FontScalePreviewTitle">Title text sample</sys:String>
    <sys:String x:Key="Settings_FontScaleRestartNotice">Restart the application for the new font scale to take effect.</sys:String>
```

- [ ] **Step 2: Build to confirm the XAML is well-formed**

Run: `dotnet build DialogEditor.slnx`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat(settings): add font-scale strings to the resource dictionary"
```

---

### Task 8: `SettingsWindow` UI — font-scale picker, live preview, restart notice, resize

**Files:**
- Modify: `DialogEditor.Avalonia/Views/SettingsWindow.axaml`

- [ ] **Step 1: Change the window-level resize attributes**

In `DialogEditor.Avalonia/Views/SettingsWindow.axaml`, change:

```xml
        Width="500" Height="220"
        CanResize="False"
```

to:

```xml
        Width="500" MinWidth="500" SizeToContent="Height"
        CanResize="True"
```

- [ ] **Step 2: Change the outer `Grid`'s row definitions**

The content row no longer needs `*` now that the window sizes to content. Change:

```xml
    <Grid RowDefinitions="*,Auto,Auto" Margin="16">
```

to:

```xml
    <Grid RowDefinitions="Auto,Auto,Auto" Margin="16">
```

- [ ] **Step 3: Add the font-scale picker, live preview, and restart notice**

Insert the following block after the "Default localization format" `DockPanel` and
before the `<shared:ThemePickerView x:Name="ThemePicker"/>` line (both currently inside
`<StackPanel Grid.Row="0" Spacing="12">`):

```xml
            <!-- Font scale (Gaps item 6 part B) -->
            <DockPanel ToolTip.Tip="{StaticResource Settings_FontScaleTooltip}">
                <TextBlock Classes="label" Text="{StaticResource Settings_FontScale}"/>
                <ComboBox ItemsSource="{Binding FontScaleOptions}"
                          SelectedItem="{Binding SelectedFontScale, Mode=TwoWay}"
                          MinWidth="120"
                          ToolTip.Tip="{StaticResource Settings_FontScaleTooltip}"
                          AutomationProperties.HelpText="{StaticResource Settings_FontScaleTooltip}"
                          AutomationProperties.Name="{StaticResource Settings_FontScale}">
                    <ComboBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding Converter={StaticResource FontScaleToPercent}}"/>
                        </DataTemplate>
                    </ComboBox.ItemTemplate>
                </ComboBox>
            </DockPanel>

            <!-- Font scale live preview: independent of the static FontSize.* tokens,
                 so it reflects the *selected* (not-yet-applied) scale immediately. -->
            <Border BorderBrush="{DynamicResource Brush.Border.Default}" BorderThickness="1" CornerRadius="3"
                    Padding="8" Background="{DynamicResource Brush.Surface.Inset}">
                <StackPanel Spacing="4">
                    <TextBlock Text="{StaticResource Settings_FontScalePreviewBody}"
                               FontSize="{Binding PreviewBodyFontSize}"
                               Foreground="{DynamicResource Brush.Text.Primary}"/>
                    <TextBlock Text="{StaticResource Settings_FontScalePreviewSubtitle}"
                               FontSize="{Binding PreviewSubtitleFontSize}"
                               Foreground="{DynamicResource Brush.Text.Primary}" FontWeight="SemiBold"/>
                    <TextBlock Text="{StaticResource Settings_FontScalePreviewTitle}"
                               FontSize="{Binding PreviewTitleFontSize}"
                               Foreground="{DynamicResource Brush.Text.Primary}" FontWeight="Bold"/>
                </StackPanel>
            </Border>

            <!-- Restart notice: only shown once the selection differs from the scale
                 actually applied at launch. -->
            <TextBlock Text="{StaticResource Settings_FontScaleRestartNotice}"
                       Foreground="{DynamicResource Brush.Text.Status.Changed}"
                       FontSize="{StaticResource FontSize.Small}"
                       IsVisible="{Binding ShowRestartNotice}"/>
```

- [ ] **Step 4: Build and run the full test suite**

Run: `dotnet build DialogEditor.slnx`
Expected: 0 warnings, 0 errors (catches any typo'd `{StaticResource ...}` or
`{Binding ...}` against `SettingsViewModel`)

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: All tests pass, no regressions.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/SettingsWindow.axaml
git commit -m "feat(settings): add font-scale picker, live preview, and resize to SettingsWindow"
```

---

### Task 9: Resizable dialogs — the other 11 windows + enforcement test

**Files:**
- Create: `DialogEditor.Tests/Theming/ResizableDialogTests.cs`
- Modify: `DialogEditor.Avalonia/Views/AboutWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/BranchNameDialog.axaml`
- Modify: `DialogEditor.Avalonia/Views/CommitConsentDialog.axaml`
- Modify: `DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml`
- Modify: `DialogEditor.Avalonia/Views/ConversationNameDialog.axaml`
- Modify: `DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/FindReplaceWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/ForceDeleteDialog.axaml`
- Modify: `DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml`
- Modify: `DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml`
- Modify: `DialogEditor.Avalonia/Views/UnsavedChangesDialog.axaml`

- [ ] **Step 1: Write the failing test**

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Pins the resize behaviour added for Gaps item 6 part B: every dialog that was
/// previously CanResize="False" with a fixed Width is now resizable, with MinWidth
/// equal to its old fixed Width, and grows vertically via SizeToContent="Height". See
/// docs/superpowers/specs/2026-06-14-fontsize-scale-setting-design.md.
/// </summary>
public class ResizableDialogTests
{
    private static void AssertResizable(Window window, double expectedMinWidth)
    {
        window.Show();
        Assert.True(window.CanResize);
        Assert.True(window.SizeToContent.HasFlag(SizeToContent.Height));
        Assert.Equal(expectedMinWidth, window.MinWidth);
    }

    [AvaloniaFact] public void AboutWindow_IsResizable() => AssertResizable(new AboutWindow(), 420);
    [AvaloniaFact] public void BranchNameDialog_IsResizable() => AssertResizable(new BranchNameDialog(), 400);
    [AvaloniaFact] public void CommitConsentDialog_IsResizable() => AssertResizable(new CommitConsentDialog(), 460);
    [AvaloniaFact] public void ConflictResolutionDialog_IsResizable() => AssertResizable(new ConflictResolutionDialog(), 520);
    [AvaloniaFact] public void ConversationNameDialog_IsResizable() => AssertResizable(new ConversationNameDialog(), 460);
    [AvaloniaFact] public void ExportConversationsWindow_IsResizable() => AssertResizable(new ExportConversationsWindow(), 480);
    [AvaloniaFact] public void FindReplaceWindow_IsResizable() => AssertResizable(new FindReplaceWindow(), 420);
    [AvaloniaFact] public void ForceDeleteDialog_IsResizable() => AssertResizable(new ForceDeleteDialog(), 420);
    [AvaloniaFact] public void ImportWarningsDialog_IsResizable() => AssertResizable(new ImportWarningsDialog(), 440);
    [AvaloniaFact] public void LanguageCodeDialog_IsResizable() => AssertResizable(new LanguageCodeDialog(), 340);
    [AvaloniaFact] public void SettingsWindow_IsResizable() => AssertResizable(new SettingsWindow(), 500);
    [AvaloniaFact] public void UnsavedChangesDialog_IsResizable() => AssertResizable(new UnsavedChangesDialog(), 420);
}
```

- [ ] **Step 2: Run the test to verify it fails for the 11 not-yet-changed windows**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~ResizableDialogTests`
Expected: 11 FAIL (`CanResize` is `false` or `MinWidth` is `0`), 1 PASS
(`SettingsWindow_IsResizable`, already fixed in Task 8).

- [ ] **Step 3: `AboutWindow.axaml`** — already has `SizeToContent="Height"`; add `MinWidth` and flip `CanResize`.

Change:
```xml
        Width="420" SizeToContent="Height" CanResize="False"
```
to:
```xml
        Width="420" MinWidth="420" SizeToContent="Height" CanResize="True"
```

- [ ] **Step 4: `BranchNameDialog.axaml`**

Change:
```xml
        Width="400" SizeToContent="Height"
        CanResize="False"
```
to:
```xml
        Width="400" MinWidth="400" SizeToContent="Height"
        CanResize="True"
```

- [ ] **Step 5: `CommitConsentDialog.axaml`**

Change:
```xml
        Width="460" SizeToContent="Height"
        CanResize="False"
```
to:
```xml
        Width="460" MinWidth="460" SizeToContent="Height"
        CanResize="True"
```

- [ ] **Step 6: `ConflictResolutionDialog.axaml`**

Change:
```xml
        Width="520" SizeToContent="Height"
        CanResize="False"
```
to:
```xml
        Width="520" MinWidth="520" SizeToContent="Height"
        CanResize="True"
```

- [ ] **Step 7: `ConversationNameDialog.axaml`**

Change:
```xml
        Width="460" SizeToContent="Height"
        CanResize="False"
```
to:
```xml
        Width="460" MinWidth="460" SizeToContent="Height"
        CanResize="True"
```

- [ ] **Step 8: `ExportConversationsWindow.axaml`** — currently has neither `SizeToContent` nor an existing `MinWidth`; both `Width` and `Height` are fixed.

Change:
```xml
        Width="480" Height="520"
        Background="{DynamicResource Brush.Surface.Window}"
        CanResize="False"
```
to:
```xml
        Width="480" MinWidth="480" SizeToContent="Height"
        Background="{DynamicResource Brush.Surface.Window}"
        CanResize="True"
```

- [ ] **Step 9: `FindReplaceWindow.axaml`**

Change:
```xml
        Width="420" Height="200"
        CanResize="False"
```
to:
```xml
        Width="420" MinWidth="420" SizeToContent="Height"
        CanResize="True"
```

- [ ] **Step 10: `ForceDeleteDialog.axaml`**

Change:
```xml
        Width="420" SizeToContent="Height"
        CanResize="False"
```
to:
```xml
        Width="420" MinWidth="420" SizeToContent="Height"
        CanResize="True"
```

- [ ] **Step 11: `ImportWarningsDialog.axaml`** — keep its existing `MaxHeight="520"` cap.

Change:
```xml
        Width="440" SizeToContent="Height" MaxHeight="520"
        CanResize="False"
```
to:
```xml
        Width="440" MinWidth="440" SizeToContent="Height" MaxHeight="520"
        CanResize="True"
```

- [ ] **Step 12: `LanguageCodeDialog.axaml`**

Change:
```xml
        Width="340" Height="160"
        CanResize="False"
```
to:
```xml
        Width="340" MinWidth="340" SizeToContent="Height"
        CanResize="True"
```

- [ ] **Step 13: `UnsavedChangesDialog.axaml`**

Change:
```xml
        Width="420" SizeToContent="Height"
        CanResize="False"
```
to:
```xml
        Width="420" MinWidth="420" SizeToContent="Height"
        CanResize="True"
```

- [ ] **Step 14: Run the test to verify all 12 pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter FullyQualifiedName~ResizableDialogTests`
Expected: PASS (12 tests)

- [ ] **Step 15: Build and run the full suite**

Run: `dotnet build DialogEditor.slnx`
Expected: 0 warnings, 0 errors

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: All tests pass (1374 + ~21 new tests from Tasks 2/3/5/6/9, 0 failures).

- [ ] **Step 16: Commit**

```bash
git add DialogEditor.Tests/Theming/ResizableDialogTests.cs \
        DialogEditor.Avalonia/Views/AboutWindow.axaml \
        DialogEditor.Avalonia/Views/BranchNameDialog.axaml \
        DialogEditor.Avalonia/Views/CommitConsentDialog.axaml \
        DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml \
        DialogEditor.Avalonia/Views/ConversationNameDialog.axaml \
        DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml \
        DialogEditor.Avalonia/Views/FindReplaceWindow.axaml \
        DialogEditor.Avalonia/Views/ForceDeleteDialog.axaml \
        DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml \
        DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml \
        DialogEditor.Avalonia/Views/UnsavedChangesDialog.axaml
git commit -m "feat(a11y): make the 12 fixed-size dialogs resizable (Gaps item 6 part B)"
```

---

### Task 10: Update `Gaps.md`

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Mark item 6 part B implemented**

Update Gaps.md item 6 to record that part B (the font-scale setting and resizable
dialogs) is now implemented, following the same convention used for part A's
"✅ Part A IMPLEMENTED (2026-06-14)" annotation — e.g. append
"✅ Part B IMPLEMENTED (<date>)" with a one-line summary referencing this plan and the
design spec.

- [ ] **Step 2: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark item 6 part B (font scale + resizable dialogs) implemented"
```

---

## Self-Review Notes

- **Spec coverage:** AppSettings.FontScale (Task 3), startup application ordering after
  ThemeApplier (Task 4), live preview independent of tokens (Task 6/8), restart notice
  (Task 6/8), preset dropdown (Task 5/8), all 12 resizable dialogs with the targeted
  `CanResize`/`MinWidth`/`SizeToContent="Height"` approach (Task 8/9), shared base-value
  table to prevent drift (Task 1) — all spec sections have a corresponding task.
- **Placeholder scan:** no TBD/TODO; the one open item flagged in the spec
  (`FontScaleApplier`'s exact Avalonia API) is fully resolved in Task 2 with concrete,
  compilable code using `IResourceDictionary`/`ResourceInclude`/`MergedDictionaries`
  (the same types `ThemeApplier` already uses).
- **Type consistency:** `FontSizeTokens.BaseValues` (Task 1) is used identically in Task 2
  (`FontScaleApplier`) and the refactored `FontSizeTokenTests`. `SettingsViewModel`
  property names (`SelectedFontScale`, `FontScaleOptions`, `PreviewBodyFontSize`,
  `PreviewSubtitleFontSize`, `PreviewTitleFontSize`, `ShowRestartNotice`) introduced in
  Task 6 match exactly what `SettingsWindow.axaml` binds to in Task 8.
