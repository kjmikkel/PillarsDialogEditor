# UI Localisation Switching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire live UI language switching (English-only for now) — `LanguageApplier` overlay mechanism, `LocaleService` revision tick, `AppSettings.UiLanguage`, `LanguagePickerViewModel/View`, `{DynamicResource}` string conversion, `PatchManagerSettingsWindow`, and `CoreLocale` facade.

**Architecture:** `LanguageApplier` is stateless (like `ThemeApplier`) — a `_LanguageOverlayMarker` sentinel key in each overlay file lets it find and remove the previous overlay before injecting the new one. `Apply("en")` is a no-op (English is the base dictionary). `LocaleService` is an exact copy of `ThemeService` providing an independent Revision tick for locale changes. All `{StaticResource <key_with_underscore>}` in view files are converted to `{DynamicResource}` in bulk; `FontSize.*` keys stay `{StaticResource}` (restart-required by design).

**Tech stack:** Avalonia, CommunityToolkit.Mvvm, xUnit, `[AvaloniaFact]` headless tests.

---

## File Map

**Create:**
- `DialogEditor.Avalonia.Shared/Theming/LocaleService.cs`
- `DialogEditor.ViewModels/Services/ILanguageApplier.cs`
- `DialogEditor.Avalonia.Shared/Theming/LanguageApplier.cs`
- `DialogEditor.Core/Resources/CoreLocale.cs`
- `DialogEditor.ViewModels/ViewModels/LanguagePickerViewModel.cs`
- `DialogEditor.Avalonia.Shared/LanguagePickerView.axaml` + `LanguagePickerView.axaml.cs`
- `DialogEditor.PatchManager/PatchManagerSettingsWindow.axaml` + `PatchManagerSettingsWindow.axaml.cs`
- `DialogEditor.Tests/Theming/LanguageApplierTests.cs`
- `DialogEditor.Tests/ViewModels/LanguagePickerViewModelTests.cs`
- `DialogEditor.Tests/Theming/NoStaticStringResourceTests.cs`

**Modify:**
- `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml` — language picker strings
- `DialogEditor.PatchManager/Resources/Strings.axaml` — PatchManager settings strings
- `DialogEditor.ViewModels/Services/AppSettings.cs` — `UiLanguage` property
- `DialogEditor.Avalonia/App.axaml.cs` — startup wiring
- `DialogEditor.PatchManager/App.axaml.cs` — startup wiring
- `DialogEditor.Avalonia/Views/SettingsWindow.axaml` — Language row
- `DialogEditor.Avalonia/Views/SettingsWindow.axaml.cs` — wire LanguagePicker DataContext
- `DialogEditor.PatchManager/MainWindow.axaml` — replace theme bar with Settings button
- `DialogEditor.PatchManager/MainWindow.axaml.cs` — update code-behind
- All 36 view `.axaml` files — bulk `{StaticResource *_*}` → `{DynamicResource *_*}`
- `DialogEditor.ViewModels/ViewModels/AboutViewModel.cs` — `LocaleService` subscription
- `DialogEditor.ViewModels/ViewModels/ChangelogViewModel.cs` — extend to `ObservableObject`, add subscription
- `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs` — `LocaleService` subscription
- `Gaps.md` — add "Font scale live switching" deferred gap; mark localisation items 1–3 complete

**Known limitations (accepted, not fixed here):**
- `ConversationItemViewModel.DisplayName` won't update live (plain class, not `ObservableObject`)
- `ConditionRowViewModel.OperatorOptions` won't update live (static property)
- `ThemePickerViewModel.AvailableThemes` display names stay in the language from the session start (only relevant once a non-English translation ships — deferred)

---

## Task 1: `LocaleService`

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Theming/LocaleService.cs`

`LocaleService` is an exact structural copy of `ThemeService.cs` (same file, adjacent in `Theming/`).
No separate unit test needed — `LanguageApplierTests` verifies the Revision bump in Task 5.

- [ ] **Step 1: Create `LocaleService.cs`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Process-wide "the language just changed" ticker. <see cref="Revision"/> is bumped on
/// every overlay swap. ViewModels with computed string getters subscribe to
/// <see cref="PropertyChanged"/> and call <c>OnPropertyChanged(string.Empty)</c> to
/// re-evaluate their localized properties live.
/// Singleton because XAML binds <c>{x:Static}</c> to it and there is exactly one running app.
/// </summary>
public sealed partial class LocaleService : ObservableObject
{
    public static LocaleService Current { get; } = new();

    private LocaleService() { }

    [ObservableProperty] private int _revision;

    internal void Bump() => Revision++;
}
```

- [ ] **Step 2: Commit**

```
git add DialogEditor.Avalonia.Shared/Theming/LocaleService.cs
git commit -m "feat(localisation): add LocaleService revision ticker"
```

---

## Task 2: `ILanguageApplier` + `LanguageOption`

**Files:**
- Create: `DialogEditor.ViewModels/Services/ILanguageApplier.cs`

- [ ] **Step 1: Create `ILanguageApplier.cs`**

```csharp
namespace DialogEditor.ViewModels.Services;

/// <summary>One selectable language: its persisted <paramref name="Id"/> (BCP-47 code,
/// e.g. "en") and the localisation key for its display name.</summary>
public sealed record LanguageOption(string Id, string DisplayNameKey);

/// <summary>
/// Framework-agnostic seam for runtime language switching. The Avalonia implementation
/// injects an overlay ResourceDictionary (last-merged wins, English base covers untranslated
/// keys); tests inject a stub. Mirrors <see cref="IThemeApplier"/>.
/// </summary>
public interface ILanguageApplier
{
    /// <summary>The languages the user may choose between, in display order (default first).</summary>
    IReadOnlyList<LanguageOption> Available { get; }

    /// <summary>Apply the language with the given BCP-47 <paramref name="id"/> to the running app.</summary>
    void Apply(string id);
}
```

- [ ] **Step 2: Commit**

```
git add DialogEditor.ViewModels/Services/ILanguageApplier.cs
git commit -m "feat(localisation): add ILanguageApplier interface and LanguageOption record"
```

---

## Task 3: `AppSettings.UiLanguage`

**Files:**
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs`
- Test: `DialogEditor.Tests/ViewModels/AppSettingsTests.cs` (if it exists; else add inline)

- [ ] **Step 1: Write the failing test**

Add to whatever AppSettings test file exists (search for `AppSettings` in `DialogEditor.Tests/`), or create `DialogEditor.Tests/ViewModels/AppSettingsUiLanguageTests.cs`:

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class AppSettingsUiLanguageTests : IDisposable
{
    private readonly string _path;
    public AppSettingsUiLanguageTests()
    {
        _path = Path.GetTempFileName();
        AppSettings.SettingsPathOverride = _path;
    }
    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_path); } catch { /* best-effort */ }
    }

    [Fact]
    public void UiLanguage_DefaultsToEn()
    {
        // Fresh settings file — no UiLanguage key — should default to "en"
        File.WriteAllText(_path, "{}");
        Assert.Equal("en", AppSettings.UiLanguage);
    }

    [Fact]
    public void UiLanguage_RoundTrips()
    {
        AppSettings.UiLanguage = "de";
        Assert.Equal("de", AppSettings.UiLanguage);
    }
}
```

- [ ] **Step 2: Run to confirm FAIL**

```
dotnet test DialogEditor.Tests --filter "AppSettingsUiLanguageTests"
```

Expected: compile error (no `UiLanguage` property yet).

- [ ] **Step 3: Add `UiLanguage` to `AppSettings.cs`**

In `SettingsData` (around line 57, after `ThemeOnboardingSeen`), add:

```csharp
        // UI language code (BCP-47, e.g. "en", "de"). Defaults to "en" (English).
        // TODO: add "Auto" (OS locale detection) once a non-English translation ships —
        //       would resolve via CultureInfo.CurrentUICulture and fall back to "en".
        public string UiLanguage { get; set; } = "en";
```

After the existing `ThemeOnboardingSeen` public property, add:

```csharp
    public static string UiLanguage
    {
        get => Load().UiLanguage;
        set { var s = Load(); s.UiLanguage = value; Save(s); }
    }
```

- [ ] **Step 4: Run to confirm PASS**

```
dotnet test DialogEditor.Tests --filter "AppSettingsUiLanguageTests"
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/Services/AppSettings.cs
git add DialogEditor.Tests/ViewModels/AppSettingsUiLanguageTests.cs   # or existing file
git commit -m "feat(localisation): add AppSettings.UiLanguage persisted setting"
```

---

## Task 4: `CoreLocale` public facade

**Files:**
- Create: `DialogEditor.Core/Resources/CoreLocale.cs`
- Test: `DialogEditor.Tests/CoreLocaleTests.cs` (new)

`CoreStrings` is `internal static` — `CoreLocale` is the public seam.

- [ ] **Step 1: Write the failing test**

```csharp
using DialogEditor.Core.Resources;

namespace DialogEditor.Tests;

public class CoreLocaleTests
{
    [Fact]
    public void SetCulture_English_SetsNull()
    {
        CoreLocale.SetCulture("en");
        // Culture null means ResourceManager uses the invariant/English .resx
        // We verify indirectly: the call must not throw.
        // (CoreStrings.Culture is internal; this test just verifies the public API.)
    }

    [Fact]
    public void SetCulture_Null_SetsNull()
    {
        CoreLocale.SetCulture(null);
        // Same — should not throw
    }

    [Fact]
    public void SetCulture_InvalidCode_DoesNotThrow()
    {
        // Invalid culture codes (e.g. "zz") should be caught internally and not crash the app.
        var ex = Record.Exception(() => CoreLocale.SetCulture("zz-INVALID-99"));
        Assert.Null(ex);
    }
}
```

- [ ] **Step 2: Run to confirm FAIL**

```
dotnet test DialogEditor.Tests --filter "CoreLocaleTests"
```

Expected: compile error (no `CoreLocale` type yet).

- [ ] **Step 3: Create `CoreLocale.cs`**

```csharp
using System.Globalization;

namespace DialogEditor.Core.Resources;

/// <summary>
/// Public seam for setting the culture used by <see cref="CoreStrings"/> (the four
/// Core-layer strings: Script_Prefix_Enter/Exit/Update, Condition_Not).
/// Called at startup and on live language change.
/// </summary>
public static class CoreLocale
{
    public static void SetCulture(string? langCode)
    {
        if (langCode is null or "en")
        {
            CoreStrings.Culture = null;
            return;
        }
        try
        {
            CoreStrings.Culture = new CultureInfo(langCode);
        }
        catch (CultureNotFoundException ex)
        {
            AppLog.Warn($"CoreLocale: unknown culture code '{langCode}': {ex.Message}. Falling back to English.");
            CoreStrings.Culture = null;
        }
    }
}
```

- [ ] **Step 4: Run to confirm PASS**

```
dotnet test DialogEditor.Tests --filter "CoreLocaleTests"
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Resources/CoreLocale.cs
git add DialogEditor.Tests/CoreLocaleTests.cs
git commit -m "feat(localisation): add CoreLocale public facade over CoreStrings.Culture"
```

---

## Task 5: `LanguageApplier`

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Theming/LanguageApplier.cs`
- Create: `DialogEditor.Tests/Theming/LanguageApplierTests.cs`

The applier is stateless (like `ThemeApplier`). It uses a sentinel key `_LanguageOverlayMarker`
to find and remove any existing overlay before injecting the new one. `Apply("en")` is a no-op —
English is the base, no overlay needed.

The `uriTemplates` format strings use `{0}` for the language code, e.g.
`"avares://DialogEditor.Avalonia/Resources/Strings.{0}.axaml"` → for `"de"` →
`"avares://DialogEditor.Avalonia/Resources/Strings.de.axaml"`.

- [ ] **Step 1: Write failing tests**

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

public class LanguageApplierTests
{
    [Fact]
    public void Available_ContainsEnglish()
    {
        var ids = new LanguageApplier().Available.Select(o => o.Id);
        Assert.Equal(["en"], ids);
    }

    [AvaloniaFact]
    public void Apply_English_BumpsRevision()
    {
        var before = LocaleService.Current.Revision;
        new LanguageApplier().Apply("en");
        Assert.Equal(before + 1, LocaleService.Current.Revision);
    }

    [AvaloniaFact]
    public void Apply_English_DoesNotAddToDictionaries()
    {
        var app    = Application.Current!;
        var before = app.Resources.MergedDictionaries.Count;
        new LanguageApplier().Apply("en");
        Assert.Equal(before, app.Resources.MergedDictionaries.Count);
    }

    [AvaloniaFact]
    public void Apply_UnknownCode_FallsBackToEnglish_AndBumpsRevision()
    {
        var app    = Application.Current!;
        var before = app.Resources.MergedDictionaries.Count;
        var rev    = LocaleService.Current.Revision;

        new LanguageApplier().Apply("zz-UNKNOWN");

        // No overlay injected (unknown code treated as "en")
        Assert.Equal(before, app.Resources.MergedDictionaries.Count);
        // Still bumped
        Assert.Equal(rev + 1, LocaleService.Current.Revision);
    }

    [AvaloniaFact]
    public void Apply_English_DisplayNameKeyResolvesViaStringProvider()
    {
        var applier  = new LanguageApplier();
        var english  = applier.Available.Single(o => o.Id == "en");
        var provider = new AvaloniaStringProvider();
        // The key must resolve (we registered the strings resource dictionary in the test app)
        Assert.NotEqual($"[{english.DisplayNameKey}]", provider.Get(english.DisplayNameKey));
    }
}
```

- [ ] **Step 2: Run to confirm FAIL**

```
dotnet test DialogEditor.Tests --filter "LanguageApplierTests"
```

Expected: compile errors (no `LanguageApplier` or `LocaleService` in tests yet).

- [ ] **Step 3: Create `LanguageApplier.cs`**

```csharp
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Runtime language overlay injection (UI Localisation items 1–3).
/// Stateless: uses a <c>_LanguageOverlayMarker</c> sentinel key to find and remove any
/// previously injected overlay before adding the new one. <c>Apply("en")</c> is a no-op —
/// English IS the base <c>Strings.axaml</c>; no overlay file exists for it.
///
/// Each overlay file must contain a <c>_LanguageOverlayMarker</c> string key so this
/// applier can locate and remove it:
/// <code>&lt;sys:String x:Key="_LanguageOverlayMarker"&gt;de&lt;/sys:String&gt;</code>
/// </summary>
public sealed class LanguageApplier : ILanguageApplier
{
    private const string OverlaySentinel = "_LanguageOverlayMarker";

    // TODO: add "Auto" (OS locale detection) + additional entries once a translation ships.
    private static readonly LanguageEntry[] Catalog =
    [
        new("en", "Settings_Language_English"),
    ];

    private readonly string[] _uriTemplates;

    /// <summary>
    /// Constructs a <see cref="LanguageApplier"/> for the calling app.
    /// </summary>
    /// <param name="uriTemplates">
    /// Format strings for the per-language overlay URIs.
    /// <c>{0}</c> is replaced with the language code.
    /// Example: <c>"avares://DialogEditor.Avalonia/Resources/Strings.{0}.axaml"</c>
    /// </param>
    public LanguageApplier(params string[] uriTemplates) => _uriTemplates = uriTemplates;

    public IReadOnlyList<LanguageOption> Available { get; } =
        Catalog.Select(e => new LanguageOption(e.Id, e.DisplayNameKey)).ToList();

    public void Apply(string id)
    {
        var entry = Catalog.FirstOrDefault(e => e.Id == id);
        if (entry is null)
        {
            AppLog.Warn($"LanguageApplier: unknown language id '{id}'. Falling back to English.");
            id = "en";
        }

        var app   = Application.Current
            ?? throw new InvalidOperationException("No Application is running.");
        var dicts = app.Resources.MergedDictionaries;

        // Remove any previously injected overlay (identified by the sentinel key).
        for (var i = dicts.Count - 1; i >= 0; i--)
            if (dicts[i].TryGetResource(OverlaySentinel, null, out _))
                dicts.RemoveAt(i);

        // English is the base — no overlay needed.
        if (id != "en")
        {
            var wrapper = new ResourceDictionary();
            foreach (var template in _uriTemplates)
                wrapper.MergedDictionaries.Add(
                    new ResourceInclude((Uri?)null)
                    {
                        Source = new Uri(string.Format(template, id)),
                    });
            dicts.Add(wrapper);
        }

        LocaleService.Current.Bump();
    }

    private sealed record LanguageEntry(string Id, string DisplayNameKey);
}
```

- [ ] **Step 4: Add `Settings_Language_English` string** (needed for the last AvaloniaFact test to pass)

In `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml`, add after the `Theme_Name_*` block (around line 57):

```xml
    <!-- ─── Language picker ──────────────────────────────────────────────── -->
    <sys:String x:Key="Settings_Language">Language</sys:String>
    <sys:String x:Key="Settings_LanguageTooltip">Choose the application language. The change is applied immediately and remembered for next time.</sys:String>
    <sys:String x:Key="Settings_Language_English">English</sys:String>
```

- [ ] **Step 5: Run tests to confirm PASS**

```
dotnet test DialogEditor.Tests --filter "LanguageApplierTests"
```

- [ ] **Step 6: Commit**

```
git add DialogEditor.Avalonia.Shared/Theming/LanguageApplier.cs
git add DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml
git add DialogEditor.ViewModels/Services/ILanguageApplier.cs
git add DialogEditor.Tests/Theming/LanguageApplierTests.cs
git commit -m "feat(localisation): add LanguageApplier with English-only catalog and LocaleService bump"
```

---

## Task 6: `LanguagePickerViewModel`

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/LanguagePickerViewModel.cs`
- Create: `DialogEditor.Tests/ViewModels/LanguagePickerViewModelTests.cs`

Mirrors `ThemePickerViewModel` exactly: selection persists to `AppSettings.UiLanguage` and
calls `ILanguageApplier.Apply` live. Also calls `CoreLocale.SetCulture` on change.

- [ ] **Step 1: Write failing tests**

```csharp
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class LanguagePickerViewModelTests : IDisposable
{
    public LanguagePickerViewModelTests()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        Loc.Configure(new StubStringProvider());
    }

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    private sealed class StubLanguageApplier : ILanguageApplier
    {
        public IReadOnlyList<LanguageOption> Available { get; } =
        [
            new("en", "Settings_Language_English"),
            new("de", "Settings_Language_German"),
        ];
        public List<string> Applied { get; } = [];
        public void Apply(string id) => Applied.Add(id);
    }

    [Fact]
    public void AvailableLanguages_AreSourcedFromApplier()
    {
        var vm = new LanguagePickerViewModel(new StubLanguageApplier());
        Assert.Equal(["en", "de"], vm.AvailableLanguages.Select(c => c.Id));
    }

    [Fact]
    public void SelectedLanguage_InitialisedFromAppSettings()
    {
        AppSettings.UiLanguage = "de";
        var vm = new LanguagePickerViewModel(new StubLanguageApplier());
        Assert.Equal("de", vm.SelectedLanguage.Id);
    }

    [Fact]
    public void SelectedLanguage_WhenAppSettingsUnknown_FallsBackToFirst()
    {
        AppSettings.UiLanguage = "xx-UNKNOWN";
        var vm = new LanguagePickerViewModel(new StubLanguageApplier());
        Assert.Equal("en", vm.SelectedLanguage.Id);
    }

    [Fact]
    public void ChangingSelectedLanguage_PersistsAndApplies()
    {
        var applier = new StubLanguageApplier();
        var vm      = new LanguagePickerViewModel(applier);

        vm.SelectedLanguage = vm.AvailableLanguages.Single(c => c.Id == "de");

        Assert.Equal("de", AppSettings.UiLanguage);
        Assert.Equal(["de"], applier.Applied);
    }
}
```

- [ ] **Step 2: Run to confirm FAIL**

```
dotnet test DialogEditor.Tests --filter "LanguagePickerViewModelTests"
```

Expected: compile error (no `LanguagePickerViewModel` yet).

- [ ] **Step 3: Create `LanguagePickerViewModel.cs`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Resources;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// <summary>One row in the language ComboBox.</summary>
public sealed record LanguageChoice(string Id, string DisplayName);

/// <summary>
/// Drives the shared language picker. On selection change it persists to
/// <see cref="AppSettings.UiLanguage"/>, applies the overlay live via the injected
/// <see cref="ILanguageApplier"/>, and updates <see cref="CoreLocale"/> so the four
/// Core-layer strings also retranslate.
/// </summary>
public partial class LanguagePickerViewModel : ObservableObject
{
    private readonly ILanguageApplier _applier;

    public IReadOnlyList<LanguageChoice> AvailableLanguages { get; }

    [ObservableProperty] private LanguageChoice _selectedLanguage;

    public LanguagePickerViewModel(ILanguageApplier applier)
    {
        _applier = applier;
        AvailableLanguages = applier.Available
            .Select(o => new LanguageChoice(o.Id, Loc.Get(o.DisplayNameKey)))
            .ToList();

        var savedId = AppSettings.UiLanguage;
        _selectedLanguage = AvailableLanguages.FirstOrDefault(c => c.Id == savedId)
                            ?? AvailableLanguages[0];
    }

    partial void OnSelectedLanguageChanged(LanguageChoice value)
    {
        if (value is null) return;
        AppSettings.UiLanguage = value.Id;
        _applier.Apply(value.Id);
        CoreLocale.SetCulture(value.Id);
    }
}
```

- [ ] **Step 4: Run to confirm PASS**

```
dotnet test DialogEditor.Tests --filter "LanguagePickerViewModelTests"
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/ViewModels/LanguagePickerViewModel.cs
git add DialogEditor.Tests/ViewModels/LanguagePickerViewModelTests.cs
git commit -m "feat(localisation): add LanguagePickerViewModel mirroring ThemePickerViewModel"
```

---

## Task 7: `LanguagePickerView`

**Files:**
- Create: `DialogEditor.Avalonia.Shared/LanguagePickerView.axaml`
- Create: `DialogEditor.Avalonia.Shared/LanguagePickerView.axaml.cs`

Mirrors `ThemePickerView.axaml` exactly (same structure, different VM binding).

- [ ] **Step 1: Create `LanguagePickerView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
             x:Class="DialogEditor.Avalonia.Shared.LanguagePickerView"
             x:CompileBindings="False">

    <DockPanel ToolTip.Tip="{DynamicResource Settings_LanguageTooltip}">
        <TextBlock Text="{DynamicResource Settings_Language}"
                   Foreground="{DynamicResource Brush.Text.Muted.Light}"
                   VerticalAlignment="Center"
                   Width="140"
                   FontSize="{StaticResource FontSize.Label}"/>
        <ComboBox ItemsSource="{Binding AvailableLanguages}"
                  SelectedItem="{Binding SelectedLanguage, Mode=TwoWay}"
                  MinWidth="160"
                  ToolTip.Tip="{DynamicResource Settings_LanguageTooltip}"
                  AutomationProperties.Name="{DynamicResource Settings_Language}"
                  AutomationProperties.HelpText="{DynamicResource Settings_LanguageTooltip}">
            <ComboBox.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding DisplayName}"/>
                </DataTemplate>
            </ComboBox.ItemTemplate>
        </ComboBox>
    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Create `LanguagePickerView.axaml.cs`**

```csharp
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Shared;

public partial class LanguagePickerView : UserControl
{
    public LanguagePickerView() => InitializeComponent();
}
```

- [ ] **Step 3: Build to confirm no compile errors**

```
dotnet build DialogEditor.Avalonia.Shared
```

- [ ] **Step 4: Commit**

```
git add DialogEditor.Avalonia.Shared/LanguagePickerView.axaml
git add DialogEditor.Avalonia.Shared/LanguagePickerView.axaml.cs
git commit -m "feat(localisation): add LanguagePickerView mirroring ThemePickerView"
```

---

## Task 8: Editor `SettingsWindow` — add Language row

**Files:**
- Modify: `DialogEditor.Avalonia/Views/SettingsWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/SettingsWindow.axaml.cs`

- [ ] **Step 1: Add `LanguagePickerView` to `SettingsWindow.axaml`**

In `SettingsWindow.axaml`, find the comment `<!-- Theme picker ... -->` at line 113.
Insert the Language picker row **immediately before** it:

```xml
            <!-- Language picker (own DataContext set in code-behind; shared with PatchManager) -->
            <shared:LanguagePickerView x:Name="LanguagePicker"/>

            <!-- Theme picker (own DataContext set in code-behind; shared with PatchManager) -->
            <shared:ThemePickerView x:Name="ThemePicker"/>
```

- [ ] **Step 2: Wire DataContext in `SettingsWindow.axaml.cs`**

In `SettingsWindow()`, add after the existing `ThemePicker.DataContext` line:

```csharp
using DialogEditor.Avalonia.Shared.Theming;
// ...

public SettingsWindow()
{
    InitializeComponent();
    ThemePicker.DataContext    = new ThemePickerViewModel(new ThemeApplier());
    LanguagePicker.DataContext = new LanguagePickerViewModel(new LanguageApplier(
        "avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.{0}.axaml",
        "avares://DialogEditor.Avalonia/Resources/Strings.{0}.axaml"));
    HintBar.AttachTo(this);
}
```

- [ ] **Step 3: Build to confirm no compile errors**

```
dotnet build DialogEditor.Avalonia
```

- [ ] **Step 4: Commit**

```
git add DialogEditor.Avalonia/Views/SettingsWindow.axaml
git add DialogEditor.Avalonia/Views/SettingsWindow.axaml.cs
git commit -m "feat(localisation): add LanguagePickerView to editor SettingsWindow"
```

---

## Task 9: `PatchManagerSettingsWindow`

**Files:**
- Create: `DialogEditor.PatchManager/PatchManagerSettingsWindow.axaml`
- Create: `DialogEditor.PatchManager/PatchManagerSettingsWindow.axaml.cs`
- Modify: `DialogEditor.PatchManager/MainWindow.axaml`
- Modify: `DialogEditor.PatchManager/MainWindow.axaml.cs`
- Modify: `DialogEditor.PatchManager/Resources/Strings.axaml`

Also add automation name string for the Settings button.

- [ ] **Step 1: Add PatchManager settings strings to `Strings.axaml`**

In `DialogEditor.PatchManager/Resources/Strings.axaml`, add at the end before `</ResourceDictionary>`:

```xml
    <sys:String x:Key="PatchManager_SettingsTitle">Patch Manager — Settings</sys:String>
    <sys:String x:Key="PatchManager_SettingsButton">⚙ Settings…</sys:String>
    <sys:String x:Key="PatchManager_SettingsTooltip">Open settings to change the colour theme and application language</sys:String>
    <sys:String x:Key="AutomationName_PatchManagerSettings">Settings</sys:String>
```

- [ ] **Step 2: Create `PatchManagerSettingsWindow.axaml`**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.PatchManager.PatchManagerSettingsWindow"
        Title="{DynamicResource PatchManager_SettingsTitle}"
        Icon="avares://DialogEditor.PatchManager/Assets/app.ico"
        Width="400" MinWidth="400" SizeToContent="Height"
        CanResize="True"
        Background="{DynamicResource Brush.Surface.Window}"
        WindowStartupLocation="CenterOwner"
        x:CompileBindings="False">

    <Window.Styles>
        <Style Selector="TextBlock.sectionHeader">
            <Setter Property="Foreground"  Value="{DynamicResource Brush.Text.Secondary}"/>
            <Setter Property="FontSize"    Value="{StaticResource FontSize.Small}"/>
            <Setter Property="FontWeight"  Value="SemiBold"/>
            <Setter Property="Margin"      Value="0,0,0,4"/>
        </Style>
    </Window.Styles>

    <StackPanel Margin="16" Spacing="16">

        <!-- Appearance -->
        <StackPanel Spacing="8">
            <TextBlock Classes="sectionHeader" Text="{DynamicResource Settings_AppearanceSection}"/>
            <shared:ThemePickerView x:Name="ThemePicker"/>
        </StackPanel>

        <!-- Language -->
        <StackPanel Spacing="8">
            <TextBlock Classes="sectionHeader" Text="{DynamicResource Settings_LanguageSection}"/>
            <shared:LanguagePickerView x:Name="LanguagePicker"/>
        </StackPanel>

        <!-- Close -->
        <Button Content="{DynamicResource Settings_Close}"
                HorizontalAlignment="Right"
                Background="{DynamicResource Brush.Surface.Header}"
                Foreground="{DynamicResource Brush.Text.Secondary}"
                BorderThickness="0"
                Padding="16,5"
                Click="Close_Click"/>

        <shared:FocusHintBar x:Name="HintBar"/>

    </StackPanel>
</Window>
```

- [ ] **Step 3: Create `PatchManagerSettingsWindow.axaml.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.ViewModels;

namespace DialogEditor.PatchManager;

public partial class PatchManagerSettingsWindow : Window
{
    public PatchManagerSettingsWindow()
    {
        InitializeComponent();
        ThemePicker.DataContext    = new ThemePickerViewModel(new ThemeApplier());
        LanguagePicker.DataContext = new LanguagePickerViewModel(new LanguageApplier(
            "avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.{0}.axaml",
            "avares://DialogEditor.PatchManager/Resources/Strings.{0}.axaml"));
        HintBar.AttachTo(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 4: Update `MainWindow.axaml` — replace theme bar with Settings button**

Replace the existing `<Border DockPanel.Dock="Top" ...>` block (lines 15–19) with:

```xml
        <!-- Standalone-only settings bar. Lives here (not in the shared PatchManagerView)
             so it appears only in the standalone PatchManager. -->
        <Border DockPanel.Dock="Top"
                Background="{DynamicResource Brush.Surface.Header}"
                Padding="8,6">
            <Button Content="{DynamicResource PatchManager_SettingsButton}"
                    Click="Settings_Click"
                    Background="Transparent"
                    BorderThickness="0"
                    Padding="6,2"
                    ToolTip.Tip="{DynamicResource PatchManager_SettingsTooltip}"
                    AutomationProperties.Name="{DynamicResource AutomationName_PatchManagerSettings}"
                    AutomationProperties.HelpText="{DynamicResource PatchManager_SettingsTooltip}"/>
        </Border>
```

Remove `x:Name="ThemePicker"` from the old markup (it no longer exists).

- [ ] **Step 5: Update `MainWindow.axaml.cs`**

Replace the constructor and remove `ThemePicker.DataContext`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.ViewModels;

namespace DialogEditor.PatchManager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new PatchManagerViewModel(
            new AvaloniaFolderPicker(this),
            new AvaloniaFilePicker(this));
        DataContext = vm;
    }

    public void LoadPatchList(string path) =>
        ((PatchManagerViewModel)DataContext!).LoadFromFile(path);

    private void Settings_Click(object? sender, RoutedEventArgs e) =>
        new PatchManagerSettingsWindow { Owner = this }.ShowDialog(this);
}
```

- [ ] **Step 6: Add missing section-header strings to `SharedStrings.axaml`**

These keys (`Settings_AppearanceSection`, `Settings_LanguageSection`, `Settings_Close`) are used by the new window. Check whether `Settings_Close` already exists in `Strings.axaml` (it does — line ~119 of SettingsWindow); add the two section headers to `SharedStrings.axaml`:

```xml
    <sys:String x:Key="Settings_AppearanceSection">Appearance</sys:String>
    <sys:String x:Key="Settings_LanguageSection">Language</sys:String>
    <sys:String x:Key="Settings_Close">Close</sys:String>
```

Note: if `Settings_Close` is already in the editor's `Strings.axaml`, move it to `SharedStrings.axaml` so the PatchManager settings window can resolve it too. Check first:
```
grep -n "Settings_Close" DialogEditor.Avalonia/Resources/Strings.axaml
```
If found, remove it from `Strings.axaml` and add to `SharedStrings.axaml` instead (they are merged in the same app; no duplicates allowed).

- [ ] **Step 7: Build PatchManager to confirm no compile errors**

```
dotnet build DialogEditor.PatchManager
```

- [ ] **Step 8: Commit**

```
git add DialogEditor.PatchManager/PatchManagerSettingsWindow.axaml
git add DialogEditor.PatchManager/PatchManagerSettingsWindow.axaml.cs
git add DialogEditor.PatchManager/MainWindow.axaml
git add DialogEditor.PatchManager/MainWindow.axaml.cs
git add DialogEditor.PatchManager/Resources/Strings.axaml
git add DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml
git commit -m "feat(localisation): add PatchManagerSettingsWindow; replace top-bar theme picker with Settings button"
```

---

## Task 10: Startup wiring (both `App.axaml.cs`)

**Files:**
- Modify: `DialogEditor.Avalonia/App.axaml.cs`
- Modify: `DialogEditor.PatchManager/App.axaml.cs`

- [ ] **Step 1: Update `DialogEditor.Avalonia/App.axaml.cs`**

Add two lines after `ThemeApplier.Apply`, before `FontScaleApplier.Apply`:

```csharp
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Core.Resources;
// ...

public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        // ... existing logging setup ...
        Loc.Configure(new AvaloniaStringProvider());
        new ThemeApplier().Apply(AppSettings.Theme);
        // Apply the persisted language before the first window is shown.
        new LanguageApplier(
            "avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.{0}.axaml",
            "avares://DialogEditor.Avalonia/Resources/Strings.{0}.axaml"
        ).Apply(AppSettings.UiLanguage);
        CoreLocale.SetCulture(AppSettings.UiLanguage);
        new FontScaleApplier().Apply(AppSettings.FontScale);
        // ... rest of method unchanged ...
    }
}
```

- [ ] **Step 2: Update `DialogEditor.PatchManager/App.axaml.cs`**

Same addition after `ThemeApplier.Apply`:

```csharp
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Core.Resources;
// ...

public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        new ThemeApplier().Apply(AppSettings.Theme);
        new LanguageApplier(
            "avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.{0}.axaml",
            "avares://DialogEditor.PatchManager/Resources/Strings.{0}.axaml"
        ).Apply(AppSettings.UiLanguage);
        CoreLocale.SetCulture(AppSettings.UiLanguage);
        // ... rest of method unchanged ...
    }
}
```

- [ ] **Step 3: Build both apps to confirm no errors**

```
dotnet build DialogEditor.Avalonia
dotnet build DialogEditor.PatchManager
```

- [ ] **Step 4: Run full test suite**

```
dotnet test DialogEditor.Tests
```

All tests should pass. If `LanguageApplierTests.Apply_English_DisplayNameKeyResolvesViaStringProvider` fails, verify the `SharedStrings.axaml` additions from Task 5 Step 4 are in place.

- [ ] **Step 5: Commit**

```
git add DialogEditor.Avalonia/App.axaml.cs
git add DialogEditor.PatchManager/App.axaml.cs
git commit -m "feat(localisation): wire LanguageApplier and CoreLocale into both app startup sequences"
```

---

## Task 11: `NoStaticStringResourceTests` + `{DynamicResource}` conversion

**Files:**
- Create: `DialogEditor.Tests/Theming/NoStaticStringResourceTests.cs`
- Modify: all 36 view `.axaml` files (bulk PowerShell)

The rule: `{StaticResource <key>}` where `<key>` contains an underscore (all string keys use
`Namespace_Name` pattern) must become `{DynamicResource <key>}`. No string key contains a dot,
so `{StaticResource FontSize.Label}` and `{StaticResource Palette.*}` are safe.
Converter/style refs like `{StaticResource FontScaleToPercent}` have no underscore and are excluded.

- [ ] **Step 1: Write the failing enforcement test**

```csharp
using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Enforces that all string resources use <c>{DynamicResource}</c> (not <c>{StaticResource}</c>)
/// in view files so live language switching retranslates them without a restart.
///
/// Identification heuristic: string keys always use underscore separators (e.g.
/// <c>Status_OpenFolder</c>, <c>Settings_Theme</c>); converter and style keys never do
/// (e.g. <c>FontScaleToPercent</c>, <c>ToolbarPlainButton</c>). <c>{StaticResource FontSize.*}</c>
/// and <c>{StaticResource Palette.*}</c> use dots — excluded by the same rule.
///
/// Resource dictionary files themselves (<c>Strings.axaml</c>, <c>Tokens.axaml</c>, etc.)
/// are excluded — they may reference each other with <c>{StaticResource}</c> internally.
/// </summary>
public class NoStaticStringResourceTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static bool IsBuildArtifact(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    // Resource dict files are allowed to use StaticResource for internal cross-references.
    private static bool IsResourceDict(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("App.axaml",          StringComparison.OrdinalIgnoreCase)
            || name.Contains("Strings",           StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tokens",            StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Palette",         StringComparison.OrdinalIgnoreCase);
    }

    // Match {StaticResource Key_With_Underscore} — string keys only.
    private static readonly Regex StaticStringRef =
        new(@"\{StaticResource \w+_[\w_]+\}", RegexOptions.Compiled);

    [Fact]
    public void StringResourcesMustUseDynamicResource()
    {
        var root      = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file)) continue;
            if (IsResourceDict(file))  continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (StaticStringRef.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}:  {lines[i].Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "String resources must use {DynamicResource} so live language switching works. " +
            "FontSize.* may stay {StaticResource} (restart-required by design). Offenders:\n" +
            string.Join("\n", offenders));
    }
}
```

- [ ] **Step 2: Run to confirm FAIL (many offenders)**

```
dotnet test DialogEditor.Tests --filter "NoStaticStringResourceTests"
```

Expected: FAIL — hundreds of `{StaticResource <Key_*>}` offenders listed.

- [ ] **Step 3: Bulk convert `{StaticResource *_*}` → `{DynamicResource *_*}` in all view files**

Run from the solution root (where `DialogEditor.slnx` lives). This PowerShell command skips resource dict files and only touches non-FontSize/non-Palette underscore refs:

```powershell
Get-ChildItem -Path . -Filter "*.axaml" -Recurse |
  Where-Object {
    $_.FullName -notmatch '\\bin\\|\\obj\\' -and
    $_.Name -notmatch '^(App|.*Strings.*|Tokens.*|Palette.*)\.axaml$'
  } |
  ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $updated = [regex]::Replace(
      $content,
      '\{StaticResource (\w+_[\w_]+)\}',
      '{DynamicResource $1}'
    )
    if ($content -ne $updated) {
      Set-Content -Path $_.FullName -Value $updated -NoNewline
      Write-Host "Updated: $($_.Name)"
    }
  }
```

- [ ] **Step 4: Run test to confirm PASS**

```
dotnet test DialogEditor.Tests --filter "NoStaticStringResourceTests"
```

- [ ] **Step 5: Run full test suite to check for regressions**

```
dotnet test DialogEditor.Tests
```

All existing tests must still pass. If any fail, the regex may have converted a non-string key — inspect the diff.

- [ ] **Step 6: Commit**

```
git add DialogEditor.Tests/Theming/NoStaticStringResourceTests.cs
git add -u   # stage all the .axaml modifications
git commit -m "feat(localisation): convert string StaticResource refs to DynamicResource; add NoStaticStringResourceTests enforcer"
```

---

## Task 12: ViewModel `LocaleService` subscriptions

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/AboutViewModel.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/ChangelogViewModel.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`

These three VMs have **computed string getters** — properties whose `get` bodies call
`Loc.Get` directly. When the language changes, `LocaleService.Revision` bumps, the subscription
fires `OnPropertyChanged(string.Empty)`, and Avalonia re-reads the properties.

`ChangelogViewModel` is currently a plain class; it needs to extend `ObservableObject` to gain
`OnPropertyChanged`.

- [ ] **Step 1: Subscribe in `AboutViewModel.cs`**

`AboutViewModel` already extends `ObservableObject`. In its constructor, add:

```csharp
// Re-evaluate all Loc.Get-based computed properties when the language changes live.
LocaleService.Current.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(LocaleService.Revision))
        OnPropertyChanged(string.Empty);
};
```

Add the using at the top:
```csharp
using DialogEditor.Avalonia.Shared.Theming;
```

- [ ] **Step 2: Extend `ChangelogViewModel` to `ObservableObject` and subscribe**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Patch.Changelog;

namespace DialogEditor.ViewModels;

public sealed partial class ChangelogViewModel : ObservableObject
{
    public IReadOnlyList<ChangelogRelease> Releases { get; }

    public ChangelogViewModel(IReadOnlyList<ChangelogRelease> releases)
    {
        Releases = releases;
        LocaleService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocaleService.Revision))
                OnPropertyChanged(string.Empty);
        };
    }

    public bool IsEmpty     => Releases.Count == 0;
    public bool HasReleases => Releases.Count > 0;
    public string EmptyMessage => Loc.Get("Changelog_Empty");
}
```

- [ ] **Step 3: Subscribe in `ConversationViewModel.cs`**

`ConversationViewModel` already extends `ObservableObject`. Find the constructor (grep for `public ConversationViewModel(`) and add:

```csharp
using DialogEditor.Avalonia.Shared.Theming;
// ...

// In constructor body:
LocaleService.Current.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(LocaleService.Revision))
        OnPropertyChanged(string.Empty);
};
```

- [ ] **Step 4: Run full test suite to confirm no regressions**

```
dotnet test DialogEditor.Tests
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/ViewModels/AboutViewModel.cs
git add DialogEditor.ViewModels/ViewModels/ChangelogViewModel.cs
git add DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs
git commit -m "feat(localisation): subscribe affected ViewModels to LocaleService.Revision for live string updates"
```

---

## Task 13: Gaps.md update

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Mark UI Localisation Readiness items 1–3 as implemented**

In `Gaps.md`, find the **UI Localisation Readiness** section. Add a new block at the top of
the "Work needed" list:

```markdown
**Items 1–3 IMPLEMENTED (2026-06-18):** `LanguageApplier` (overlay-merge mechanism, English
no-op, `_LanguageOverlayMarker` sentinel for stateless live switching), `LocaleService`
revision tick, `AppSettings.UiLanguage` ("en" default; TODO Auto once a translation ships),
`CoreLocale.SetCulture` facade, `LanguagePickerViewModel`/`LanguagePickerView` (live, no
restart), editor `SettingsWindow` Language row, `PatchManagerSettingsWindow` (Appearance +
Language, replaces the top-bar theme picker). All `{StaticResource <string key>}` in views
converted to `{DynamicResource}`; `NoStaticStringResourceTests` enforces the invariant.
`AboutViewModel`, `ChangelogViewModel`, `ConversationViewModel` subscribe to
`LocaleService.Revision` for live getter refresh. Remaining items (4–6) are unchanged.
```

- [ ] **Step 2: Add "Font scale live switching" deferred gap**

At the end of `Gaps.md`, after the existing content, add a new subsection:

```markdown
### Font Scale Live Switching

**Deferred from UI Localisation Readiness items 1–3.** `FontScaleApplier` currently runs
once at startup (restart-required). Making font scaling live requires:

- Converting `{StaticResource FontSize.*}` → `{DynamicResource FontSize.*}` in all views
  (~349 occurrences across 30 `.axaml` files).
- Making `FontScaleApplier.Apply` callable at runtime, not just at startup.
- A `FontScaleService.Revision` tick (mirroring `ThemeService`/`LocaleService`) so
  text-measurement-dependent layouts reflow when the scale changes.
- Removing the "takes effect after restart" notice from the font-scale picker in
  `SettingsWindow`.

The `NoStaticStringResourceTests` enforcer (added in items 1–3) excludes `FontSize.*`
from its scan, so this gap has a clean path: convert, add the service tick, remove the
restart notice.
```

- [ ] **Step 3: Commit**

```
git add Gaps.md
git commit -m "docs(gaps): mark UI localisation items 1-3 implemented; add font scale live switching deferred gap"
```

---

## Self-Review Checklist

- [x] **`LocaleService`** — Task 1 ✓
- [x] **`ILanguageApplier` + `LanguageOption`** — Task 2 ✓
- [x] **`AppSettings.UiLanguage`** — Task 3 ✓
- [x] **`CoreLocale.SetCulture`** — Task 4 ✓
- [x] **`LanguageApplier` (catalog, Apply no-op, Apply unknown fallback, LocaleService.Bump)** — Task 5 ✓
- [x] **New strings in `SharedStrings.axaml`** — Task 5 Step 4 + Task 9 Step 6 ✓
- [x] **`LanguagePickerViewModel`** — Task 6 ✓
- [x] **`LanguagePickerView`** — Task 7 ✓
- [x] **Editor `SettingsWindow` Language row** — Task 8 ✓
- [x] **`PatchManagerSettingsWindow` + replace top-bar theme picker** — Task 9 ✓
- [x] **Startup wiring (both `App.axaml.cs`)** — Task 10 ✓
- [x] **`{DynamicResource}` conversion + `NoStaticStringResourceTests`** — Task 11 ✓
- [x] **ViewModel `LocaleService` subscriptions** — Task 12 ✓
- [x] **Gaps.md update + font scale deferred gap** — Task 13 ✓
- [x] **`LanguageApplierTests`** — Task 5 ✓
- [x] **`LanguagePickerViewModelTests`** — Task 6 ✓
- [x] **`AppSettingsUiLanguageTests`** — Task 3 ✓
- [x] **`CoreLocaleTests`** — Task 4 ✓

**Type consistency check:**
- `LanguageChoice(string Id, string DisplayName)` — defined Task 6, used Task 7 (PickerView binds `DisplayName`) ✓
- `LanguageOption(string Id, string DisplayNameKey)` — defined Task 2, used Tasks 5 and 6 ✓
- `LocaleService.Current.Revision` — defined Task 1, tested Task 5, subscribed Task 12 ✓
- `LanguageApplier(params string[] uriTemplates)` — defined Task 5, called Tasks 8, 9, 10 ✓
- `AppSettings.UiLanguage` — defined Task 3, read Task 6, set Task 6 `OnSelectedLanguageChanged` ✓
