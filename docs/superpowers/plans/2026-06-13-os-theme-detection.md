# OS Theme Detection ("Auto") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an `"Auto"` theme option that resolves to the OS's reported high-contrast /
dark / light preference (high-contrast wins), make it the default `AppSettings.Theme` for
fresh installs, and surface it in the existing theme picker.

**Architecture:** `ThemeApplier` (Layer 2's runtime palette switcher) gains a pure mapping
function `DetectOsThemeId(PlatformColorValues? values)` and an `"Auto"` pseudo-entry in its
`Available` list. `Apply("Auto")` resolves to a concrete catalog id via that function before
doing its existing palette swap. `AppSettings.Theme`'s default changes from `"Dark"` to
`"Auto"`. No changes are needed to `App.axaml.cs`, `ThemePickerViewModel`, or
`ThemePickerView` — they already go through `IThemeApplier.Available` / `Apply(id)`.

**Tech Stack:** C# / .NET 8, Avalonia 11.3.14 (`Avalonia.Platform.PlatformColorValues`,
`PlatformThemeVariant`, `ColorContrastPreference`), xUnit + `Avalonia.Headless.XUnit`
(`[AvaloniaFact]`).

**Spec:** `docs/superpowers/specs/2026-06-13-os-theme-detection-design.md`

---

### Task 1: `DetectOsThemeId` pure mapping function

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Properties/AssemblyInfo.cs`
- Modify: `DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs`
- Test: `DialogEditor.Tests/Theming/ThemeApplierTests.cs`

- [ ] **Step 1: Make `DialogEditor.Avalonia.Shared` internals visible to the test project**

The new mapping function will be `internal` (it's a Layer-2 implementation detail, not part
of `IThemeApplier`). `DialogEditor.ViewModels` already does this for the same reason
(`DialogEditor.ViewModels/Properties/AssemblyInfo.cs`); mirror it for
`DialogEditor.Avalonia.Shared`.

Create `DialogEditor.Avalonia.Shared/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DialogEditor.Tests")]
```

- [ ] **Step 2: Write the failing tests**

Add to `DialogEditor.Tests/Theming/ThemeApplierTests.cs`. Add `using Avalonia.Platform;` to
the file's using block (alongside the existing `using Avalonia;`, `using
Avalonia.Headless.XUnit;`, etc.), then add these tests anywhere in the `ThemeApplierTests`
class:

```csharp
    [Theory]
    [InlineData(PlatformThemeVariant.Dark,  ColorContrastPreference.NoPreference, "Dark")]
    [InlineData(PlatformThemeVariant.Light, ColorContrastPreference.NoPreference, "Light")]
    [InlineData(PlatformThemeVariant.Dark,  ColorContrastPreference.High,         "HighContrast")]
    [InlineData(PlatformThemeVariant.Light, ColorContrastPreference.High,         "HighContrast")]
    public void DetectOsThemeId_MapsPlatformPreferences(
        PlatformThemeVariant variant, ColorContrastPreference contrast, string expectedId)
    {
        var values = new PlatformColorValues { ThemeVariant = variant, ContrastPreference = contrast };
        Assert.Equal(expectedId, ThemeApplier.DetectOsThemeId(values));
    }

    [Fact]
    public void DetectOsThemeId_NullValues_FallsBackToDark()
    {
        Assert.Equal("Dark", ThemeApplier.DetectOsThemeId(null));
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ThemeApplierTests"`

Expected: build error — `ThemeApplier.DetectOsThemeId` does not exist.

- [ ] **Step 4: Implement `DetectOsThemeId`**

In `DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs`, add `using Avalonia.Platform;` to
the using block at the top of the file, then add this method inside the `ThemeApplier`
class (e.g. just above `Apply`):

```csharp
    /// <summary>
    /// Maps the OS-reported colour preferences to a catalog id. High-contrast wins outright
    /// regardless of the reported light/dark variant — the <c>HighContrast</c> palette is
    /// itself authored against <see cref="ThemeVariant.Dark"/> and isn't variant-aware.
    /// <c>null</c> (no platform settings available) falls back to <c>"Dark"</c>, matching
    /// the historical hardcoded default.
    /// </summary>
    internal static string DetectOsThemeId(PlatformColorValues? values)
    {
        if (values is null) return "Dark";
        if (values.ContrastPreference == ColorContrastPreference.High) return "HighContrast";
        return values.ThemeVariant == PlatformThemeVariant.Light ? "Light" : "Dark";
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ThemeApplierTests"`

Expected: PASS (all `ThemeApplierTests`, including the 5 new cases).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Properties/AssemblyInfo.cs DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs DialogEditor.Tests/Theming/ThemeApplierTests.cs
git commit -m "feat(theming): add DetectOsThemeId OS-preference mapping"
```

---

### Task 2: `"Auto"` entry in `Available` + localised display name

**Files:**
- Modify: `DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs`
- Modify: `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml`
- Test: `DialogEditor.Tests/Theming/ThemeApplierTests.cs`

- [ ] **Step 1: Write the failing tests**

In `DialogEditor.Tests/Theming/ThemeApplierTests.cs`, replace the existing
`Available_ListsThePalettesInOrder` test:

```csharp
    [Fact]
    public void Available_ListsThePalettesInOrder()
    {
        var ids = new ThemeApplier().Available.Select(o => o.Id);
        Assert.Equal(["Dark", "Light", "Colourblind", "HighContrast"], ids);
    }
```

with:

```csharp
    [Fact]
    public void Available_ListsThePalettesInOrder()
    {
        var ids = new ThemeApplier().Available.Select(o => o.Id);
        Assert.Equal(["Auto", "Dark", "Light", "Colourblind", "HighContrast"], ids);
    }
```

Also add (this uses `AvaloniaStringProvider`, which is in
`DialogEditor.Avalonia.Shared.Services` — add `using DialogEditor.Avalonia.Shared.Services;`
to the test file's using block):

```csharp
    [AvaloniaFact]
    public void Theme_Name_Auto_ResourceResolves()
    {
        Assert.Equal("System Default", new AvaloniaStringProvider().Get("Theme_Name_Auto"));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ThemeApplierTests"`

Expected: FAIL —
- `Available_ListsThePalettesInOrder`: actual is `["Dark", "Light", "Colourblind", "HighContrast"]`, missing `"Auto"`.
- `Theme_Name_Auto_ResourceResolves`: actual is `"[Theme_Name_Auto]"` (missing-resource fallback in `AvaloniaStringProvider.Get`).

- [ ] **Step 3: Add the `Theme_Name_Auto` string resource**

In `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml`, the existing
`Theme_Name_*` block reads:

```xml
    <sys:String x:Key="Theme_Name_Dark">Dark</sys:String>
    <sys:String x:Key="Theme_Name_Light">Light</sys:String>
    <sys:String x:Key="Theme_Name_Colourblind">Colourblind</sys:String>
    <sys:String x:Key="Theme_Name_HighContrast">High Contrast</sys:String>
```

Add `Theme_Name_Auto` before it, so the block becomes:

```xml
    <sys:String x:Key="Theme_Name_Auto">System Default</sys:String>
    <sys:String x:Key="Theme_Name_Dark">Dark</sys:String>
    <sys:String x:Key="Theme_Name_Light">Light</sys:String>
    <sys:String x:Key="Theme_Name_Colourblind">Colourblind</sys:String>
    <sys:String x:Key="Theme_Name_HighContrast">High Contrast</sys:String>
```

- [ ] **Step 4: Add `"Auto"` to `Available`**

In `DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs`, replace:

```csharp
    public IReadOnlyList<ThemeOption> Available { get; } =
        Catalog.Select(e => new ThemeOption(e.Id, e.DisplayNameKey)).ToList();
```

with:

```csharp
    // "Auto" is a meta-choice resolved at apply-time by DetectOsThemeId — it has no palette
    // file of its own, so it isn't a Catalog entry, but it's the first (default) choice.
    public IReadOnlyList<ThemeOption> Available { get; } =
        new[] { new ThemeOption("Auto", "Theme_Name_Auto") }
            .Concat(Catalog.Select(e => new ThemeOption(e.Id, e.DisplayNameKey)))
            .ToList();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ThemeApplierTests"`

Expected: PASS (all `ThemeApplierTests`).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml DialogEditor.Tests/Theming/ThemeApplierTests.cs
git commit -m "feat(theming): list Auto as the first theme-picker option"
```

---

### Task 3: `Apply("Auto")` resolves via `DetectOsThemeId`

**Files:**
- Modify: `DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs`
- Test: `DialogEditor.Tests/Theming/ThemeApplierTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `DialogEditor.Tests/Theming/ThemeApplierTests.cs`:

```csharp
    [AvaloniaFact]
    public void Apply_Auto_ResolvesToDetectedPalette()
    {
        var app = Application.Current!;
        var expectedId      = ThemeApplier.DetectOsThemeId(app.PlatformSettings?.GetColorValues());
        var expectedVariant = expectedId == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        try
        {
            new ThemeApplier().Apply("Auto");
            Assert.Equal(expectedVariant, app.RequestedThemeVariant);
        }
        finally { new ThemeApplier().Apply("Dark"); }
    }

    [AvaloniaFact]
    public void Apply_Auto_BumpsRevision()
    {
        try
        {
            var before = ThemeService.Current.Revision;
            new ThemeApplier().Apply("Auto");
            Assert.Equal(before + 1, ThemeService.Current.Revision);
        }
        finally { new ThemeApplier().Apply("Dark"); }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ThemeApplierTests"`

Expected: before Step 3, `Apply("Auto")` falls through to `Catalog.FirstOrDefault(e => e.Id
== "Auto") ?? Catalog[0]`, i.e. it always resolves to `Catalog[0]` (`"Dark"` →
`ThemeVariant.Dark`), regardless of what `DetectOsThemeId` would say.
`Apply_Auto_BumpsRevision` will PASS already (`Apply` bumps the revision no matter which
entry it resolves to). `Apply_Auto_ResolvesToDetectedPalette` will FAIL *unless* the
headless test platform's `GetColorValues()` happens to report `Dark`/`NoPreference` (i.e.
`expectedVariant == ThemeVariant.Dark`) — in which case it coincidentally passes. Either
way, proceed to Step 3: the resolution wiring is required by the spec regardless of what
the headless platform currently reports.

- [ ] **Step 3: Implement the resolution step in `Apply`**

In `DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs`, the current `Apply` is:

```csharp
    public void Apply(string id)
    {
        var entry = Catalog.FirstOrDefault(e => e.Id == id) ?? Catalog[0];
        var app   = Application.Current
            ?? throw new InvalidOperationException("No Application is running to apply a theme to.");

        var dicts = app.Resources.MergedDictionaries;
```

Replace those four lines with:

```csharp
    public void Apply(string id)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("No Application is running to apply a theme to.");

        var resolvedId = id == "Auto"
            ? DetectOsThemeId(app.PlatformSettings?.GetColorValues())
            : id;
        var entry = Catalog.FirstOrDefault(e => e.Id == resolvedId) ?? Catalog[0];

        var dicts = app.Resources.MergedDictionaries;
```

(The rest of `Apply` — the dictionary-swap loop, `wrapper`, `app.RequestedThemeVariant =
entry.Variant`, `ThemeService.Current.Bump()` — is unchanged.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ThemeApplierTests"`

Expected: PASS (all `ThemeApplierTests`, including `Apply_Auto_ResolvesToDetectedPalette`
and `Apply_Auto_BumpsRevision`).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs DialogEditor.Tests/Theming/ThemeApplierTests.cs
git commit -m "feat(theming): Apply(\"Auto\") resolves to the OS-detected palette"
```

---

### Task 4: `AppSettings.Theme` defaults to `"Auto"`

**Files:**
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs:42-44`
- Test: `DialogEditor.Tests/Services/AppSettingsTests.cs:110-114`

- [ ] **Step 1: Write the failing test**

In `DialogEditor.Tests/Services/AppSettingsTests.cs`, replace:

```csharp
    [Fact]
    public void Theme_DefaultsToDark()
    {
        Assert.Equal("Dark", AppSettings.Theme);
    }
```

with:

```csharp
    [Fact]
    public void Theme_DefaultsToAuto()
    {
        Assert.Equal("Auto", AppSettings.Theme);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AppSettingsThemeTests"`

Expected: FAIL — `Theme_DefaultsToAuto`: `Assert.Equal("Auto", "Dark")`.

- [ ] **Step 3: Change the default**

In `DialogEditor.ViewModels/Services/AppSettings.cs`, replace:

```csharp
        // Layer 2 (runtime theming): the selected palette id (see ThemeApplier catalog).
        // Default "Dark" matches the historical hardcoded RequestedThemeVariant="Dark".
        public string Theme                          { get; set; } = "Dark";
```

with:

```csharp
        // Layer 2 (runtime theming): the selected palette id (see ThemeApplier catalog), or
        // "Auto" to follow the OS dark/light/high-contrast preference (resolved by
        // ThemeApplier.DetectOsThemeId). "Auto" is the default for fresh installs; existing
        // installs keep whatever they already had persisted (e.g. the historical "Dark").
        public string Theme                          { get; set; } = "Auto";
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AppSettingsThemeTests"`

Expected: PASS (`Theme_DefaultsToAuto` and `Theme_RoundTrips`).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/AppSettings.cs DialogEditor.Tests/Services/AppSettingsTests.cs
git commit -m "feat(theming): default AppSettings.Theme to Auto for fresh installs"
```

---

### Task 5: Full-suite regression check

**Files:** none (verification only)

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`

Expected: PASS, no new failures. In particular, confirm
`DialogEditor.Tests/ViewModels/ThemePickerViewModelTests.cs` is unaffected —
`SelectedTheme_WhenAppSettingsUnknown_FallsBackToFirst` uses a `StubThemeApplier` whose
`Available` is hardcoded to `["Dark", "Light"]` (no `"Auto"`), so its "falls back to first =
Dark" assertion is untouched by `ThemeApplier.Available` now starting with `"Auto"`.

- [ ] **Step 2: Manual sanity check (optional but recommended)**

Run the editor (`dotnet run --project DialogEditor.Avalonia`) on a machine with no
`settings.json` yet (or `AppSettings.SettingsPathOverride` pointed at a fresh temp file via
a throwaway debug run), open Settings, and confirm:
- The theme picker's first entry reads "System Default".
- It's selected by default.
- Switching to "Dark"/"Light"/etc. and back to "System Default" retints immediately to
  match your current OS dark/light setting (and to High Contrast if your OS high-contrast
  mode is on).

- [ ] **Step 3: No commit needed**

This task is verification-only; if Step 1 surfaces a failure, fix it under the relevant
earlier task's commit (re-stage and amend only if that commit hasn't been pushed, otherwise
add a small follow-up commit) rather than leaving it for a separate cleanup pass.
