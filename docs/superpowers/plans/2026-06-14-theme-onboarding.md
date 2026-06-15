# First-Run Theme Onboarding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On a genuinely fresh install, show a one-shot modal "Choose a Theme" window — hosting the existing `ThemePickerView` plus a live-retinting preview panel — before `MainWindow` opens, in both `DialogEditor.Avalonia` and `DialogEditor.PatchManager`.

**Architecture:** A new persisted `AppSettings.ThemeOnboardingSeen` flag (default `true`, but `false` on a no-file/load-failure `Load()`) gates a new shared `ThemeOnboardingWindow` (in `DialogEditor.Avalonia.Shared`). The window reuses `ThemePickerView`/`ThemePickerViewModel`/`IThemeApplier` unmodified; its preview panel is static `{DynamicResource Brush.*}`-bound markup that retints live as the picker's selection changes `Application.Current`'s merged resources. Both apps' `App.axaml.cs` gain an identical `if (!ThemeOnboardingSeen)` branch that shows the onboarding window first and swaps in `MainWindow` on close.

**Tech Stack:** C# / .NET 8, Avalonia UI (headless testing via `Avalonia.Headless.XUnit`), xUnit.

**Reference spec:** `docs/superpowers/specs/2026-06-14-theme-onboarding-design.md`

---

### Task 1: `AppSettings.ThemeOnboardingSeen`

**Files:**
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs`
- Test: `DialogEditor.Tests/Services/AppSettingsTests.cs`

- [ ] **Step 1: Write the failing tests**

Append this new class to `DialogEditor.Tests/Services/AppSettingsTests.cs` (after `AppSettingsFontScaleTests`):

```csharp
public class AppSettingsThemeOnboardingTests : IDisposable
{
    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null && File.Exists(path)) File.Delete(path);
    }

    [Fact]
    public void ThemeOnboardingSeen_DefaultsToFalse_WhenNoSettingsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = path;

        Assert.False(AppSettings.ThemeOnboardingSeen);
    }

    [Fact]
    public void ThemeOnboardingSeen_DefaultsToTrue_WhenExistingSettingsFileLacksKey()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{}");
        AppSettings.SettingsPathOverride = path;

        Assert.True(AppSettings.ThemeOnboardingSeen);
    }

    [Fact]
    public void ThemeOnboardingSeen_RoundTrips()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();

        AppSettings.ThemeOnboardingSeen = true;
        Assert.True(AppSettings.ThemeOnboardingSeen);

        AppSettings.ThemeOnboardingSeen = false;
        Assert.False(AppSettings.ThemeOnboardingSeen);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AppSettingsThemeOnboardingTests"`

Expected: build error — `AppSettings.ThemeOnboardingSeen` does not exist.

- [ ] **Step 3: Add the flag to `SettingsData` and adjust `Load()`**

In `DialogEditor.ViewModels/Services/AppSettings.cs`, add the new property to `SettingsData` right after `FontScale`:

```csharp
        // The font-scale multiplier applied to every FontSize.* token at next startup
        // (Gaps item 6 part B). 1.0 = no scaling, the historical/default size. Changing
        // this only persists the value; FontScaleApplier applies it once at the next
        // launch, so already-open windows are unaffected until restart.
        public double FontScale                      { get; set; } = 1.0;
        // Whether the first-run theme-onboarding dialog (Gaps item 15) has been shown.
        // Defaults to true so that EXISTING installs upgrading to this version (whose
        // settings.json predates this field) silently treat onboarding as already-seen —
        // only a genuinely fresh install (no settings.json yet, or a load failure) gets
        // false, via Load().
        public bool ThemeOnboardingSeen               { get; set; } = true;
```

Then change `Load()` so the "no file yet" and "load failed" paths set the flag to `false`:

```csharp
    private static SettingsData Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new() { ThemeOnboardingSeen = false };
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<SettingsData>(json) ?? new();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to load settings from {SettingsPath}: {ex.Message}");
            return new() { ThemeOnboardingSeen = false };
        }
    }
```

- [ ] **Step 4: Add the accessor**

Add after the `FontScale` accessor:

```csharp
    public static bool ThemeOnboardingSeen
    {
        get => Load().ThemeOnboardingSeen;
        set { var s = Load(); s.ThemeOnboardingSeen = value; Save(s); }
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AppSettingsThemeOnboardingTests"`

Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/Services/AppSettings.cs DialogEditor.Tests/Services/AppSettingsTests.cs
git commit -m "feat(settings): add ThemeOnboardingSeen flag for first-run theme onboarding"
```

---

### Task 2: Onboarding strings in `SharedStrings.axaml`

**Files:**
- Modify: `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml`

No test for this task — `NoStrayHexTests`/structural tests in Task 4 will catch any missing key once the window references them.

- [ ] **Step 1: Add the new string entries**

In `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml`, append this block right before the closing `</ResourceDictionary>` tag (after the existing `Theme_Name_HighContrast` entry):

```xml

    <!-- ─── First-run theme onboarding (Gaps item 15) — shared by editor and standalone PatchManager ── -->
    <sys:String x:Key="ThemeOnboarding_Title">Choose a Theme</sys:String>
    <sys:String x:Key="ThemeOnboarding_Intro">Pick a colour theme below — you can change this anytime later in Settings.</sys:String>
    <sys:String x:Key="ThemeOnboarding_PreviewLabel">Preview</sys:String>
    <sys:String x:Key="ThemeOnboarding_Continue">Continue</sys:String>
    <sys:String x:Key="ThemeOnboarding_Card_Npc">NPC</sys:String>
    <sys:String x:Key="ThemeOnboarding_Card_Player">Player</sys:String>
    <sys:String x:Key="ThemeOnboarding_Card_Narrator">Narrator</sys:String>
    <sys:String x:Key="ThemeOnboarding_Sample_Npc">"The road north is closed," the guard says.</sys:String>
    <sys:String x:Key="ThemeOnboarding_Sample_Player">[Persuade] Let us through — it's urgent.</sys:String>
    <sys:String x:Key="ThemeOnboarding_Sample_Narrator">The guard eyes you warily, hand on his sword.</sys:String>
    <sys:String x:Key="ThemeOnboarding_Badge_Warning">Warning</sys:String>
    <sys:String x:Key="ThemeOnboarding_Badge_Error">Error</sys:String>
    <sys:String x:Key="ThemeOnboarding_Button_Primary">Primary</sys:String>
    <sys:String x:Key="ThemeOnboarding_Button_Destructive">Destructive</sys:String>
    <sys:String x:Key="ThemeOnboarding_Button_Plain">Plain</sys:String>
```

- [ ] **Step 2: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml
git commit -m "feat(loc): add first-run theme onboarding strings"
```

---

### Task 3: Shared `app.ico` asset

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Assets/app.ico` (copy of `DialogEditor.Avalonia/Assets/app.ico`)
- Modify: `DialogEditor.Avalonia.Shared/DialogEditor.Avalonia.Shared.csproj`

`ThemeOnboardingWindow.axaml` (Task 4) is the first standalone `Window` compiled into `DialogEditor.Avalonia.Shared`, so it cannot reference `avares://DialogEditor.Avalonia/Assets/app.ico` (assembly-scoped). Copy the icon into Shared and mark it as an `AvaloniaResource`.

- [ ] **Step 1: Copy the icon file**

```bash
mkdir -p DialogEditor.Avalonia.Shared/Assets
cp DialogEditor.Avalonia/Assets/app.ico DialogEditor.Avalonia.Shared/Assets/app.ico
```

- [ ] **Step 2: Add the `AvaloniaResource` entry**

Open `DialogEditor.Avalonia.Shared/DialogEditor.Avalonia.Shared.csproj` and add, alongside the existing `<AvaloniaResource Include="Resources\SharedStrings.axaml"/>` item:

```xml
    <AvaloniaResource Include="Assets\**"/>
```

(Place it as a sibling `<AvaloniaResource>` item within the same `<ItemGroup>`.)

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Assets/app.ico DialogEditor.Avalonia.Shared/DialogEditor.Avalonia.Shared.csproj
git commit -m "feat(shared): embed app icon for the shared theme-onboarding window"
```

---

### Task 4: `ThemeOnboardingWindow`

**Files:**
- Create: `DialogEditor.Avalonia.Shared/ThemeOnboardingWindow.axaml`
- Create: `DialogEditor.Avalonia.Shared/ThemeOnboardingWindow.axaml.cs`
- Test: `DialogEditor.Tests/Controls/ThemeOnboardingWindowTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DialogEditor.Tests/Controls/ThemeOnboardingWindowTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared;
using DialogEditor.ViewModels.ViewModels;

namespace DialogEditor.Tests.Controls;

/// <summary>
/// Gaps.md a11y item 15: first-run theme-onboarding window. Construction smoke test
/// mirroring the other headless dialog-construction tests (LanguageCodeDialog,
/// UnsavedChangesDialog, ...). See
/// docs/superpowers/specs/2026-06-14-theme-onboarding-design.md.
/// </summary>
public class ThemeOnboardingWindowTests
{
    [AvaloniaFact]
    public void Constructs_WithoutThrowing()
    {
        var window = new ThemeOnboardingWindow();
        window.Show();
    }

    [AvaloniaFact]
    public void ThemePicker_ComboBox_HasAllFiveThemes()
    {
        var window = new ThemeOnboardingWindow();
        window.Show();

        var combo = window.FindControl<ThemePickerView>("ThemePicker")!
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .Single();

        var vm = (ThemePickerViewModel)window.FindControl<ThemePickerView>("ThemePicker")!.DataContext!;
        Assert.Equal(5, vm.AvailableThemes.Count);
        Assert.Equal(5, combo.ItemCount);
    }

    [AvaloniaFact]
    public void ContinueButton_Exists_AndIsNamed()
    {
        var window = new ThemeOnboardingWindow();
        window.Show();

        var button = window.FindControl<Button>("ContinueButton");
        Assert.NotNull(button);
        Assert.False(string.IsNullOrWhiteSpace(button!.Content as string));
    }

    [AvaloniaFact]
    public void ContinueButton_Click_ClosesWindow()
    {
        var window = new ThemeOnboardingWindow();
        window.Show();

        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.FindControl<Button>("ContinueButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(closed);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ThemeOnboardingWindowTests"`

Expected: build error — `DialogEditor.Avalonia.Shared.ThemeOnboardingWindow` does not exist.

- [ ] **Step 3: Add `GetVisualDescendants` using**

The test above uses `GetVisualDescendants()`, an extension on `Visual` from `Avalonia.VisualTree`. Add this `using` to the top of the test file if not already covered by an implicit/global using:

```csharp
using Avalonia.VisualTree;
```

(Check `DialogEditor.Tests/DialogEditor.Tests.csproj` for `<ImplicitUsings>enable</ImplicitUsings>` — if global usings already cover `Avalonia.VisualTree` via a `GlobalUsings.cs`, this explicit `using` is harmless; if the namespace doesn't exist in the referenced Avalonia version, replace the second test's combo lookup with the simpler form below instead:

```csharp
    [AvaloniaFact]
    public void ThemePicker_ComboBox_HasAllFiveThemes()
    {
        var window = new ThemeOnboardingWindow();
        window.Show();

        var picker = window.FindControl<ThemePickerView>("ThemePicker")!;
        var vm = (ThemePickerViewModel)picker.DataContext!;
        Assert.Equal(5, vm.AvailableThemes.Count);
    }
```

— this drops the `ComboBox.ItemCount` cross-check but still pins the "all 5 entries" contract via the ViewModel, which is what actually drives the ComboBox's `ItemsSource`.)

- [ ] **Step 4: Create `ThemeOnboardingWindow.axaml`**

Create `DialogEditor.Avalonia.Shared/ThemeOnboardingWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Shared.ThemeOnboardingWindow"
        Title="{DynamicResource ThemeOnboarding_Title}"
        Icon="avares://DialogEditor.Avalonia.Shared/Assets/app.ico"
        Width="480" MinWidth="480" SizeToContent="Height"
        CanResize="True"
        WindowStartupLocation="CenterScreen"
        Background="{DynamicResource Brush.Surface.Window}"
        x:CompileBindings="False">

    <!-- First-run theme-onboarding dialog (Gaps item 15). Hosts the existing
         ThemePickerView unmodified; selecting an entry calls IThemeApplier.Apply, which
         swaps the merged palette + Tokens.axaml and flips RequestedThemeVariant. Every
         {DynamicResource} below — including this preview panel — re-resolves
         automatically, giving a live "self-retint" preview with no bespoke rendering.
         See docs/superpowers/specs/2026-06-14-theme-onboarding-design.md. -->
    <Grid RowDefinitions="Auto,Auto,Auto" Margin="16">

        <StackPanel Grid.Row="0" Spacing="12">

            <TextBlock Text="{DynamicResource ThemeOnboarding_Intro}"
                       Foreground="{DynamicResource Brush.Text.Primary}"
                       FontSize="{StaticResource FontSize.Body}"
                       TextWrapping="Wrap"/>

            <shared:ThemePickerView x:Name="ThemePicker"/>

            <TextBlock Text="{DynamicResource ThemeOnboarding_PreviewLabel}"
                       Foreground="{DynamicResource Brush.Text.Muted.Light}"
                       FontSize="{StaticResource FontSize.Label}" FontWeight="SemiBold"/>

            <Border BorderBrush="{DynamicResource Brush.Border.Default}" BorderThickness="1" CornerRadius="3"
                    Padding="8" Background="{DynamicResource Brush.Surface.Inset}">
                <StackPanel Spacing="10">

                    <!-- Node cards: NPC (circle), Player (square), Narrator (triangle) —
                         Layer 2.5 shape glyphs mirrored from NodeTypeShapeConverter so the
                         preview reads correctly even in Colourblind/High-Contrast. -->
                    <StackPanel Orientation="Horizontal" Spacing="6">
                        <Border Width="140" CornerRadius="4" BorderBrush="{DynamicResource Brush.Border.Default}" BorderThickness="1" ClipToBounds="True">
                            <StackPanel>
                                <Border Background="{DynamicResource Brush.Node.Npc.Header}" Padding="6,4">
                                    <StackPanel Orientation="Horizontal" Spacing="4">
                                        <Path Data="M4.5,0 A4.5,4.5 0 1 1 4.5,9 A4.5,4.5 0 1 1 4.5,0 Z"
                                              Fill="{DynamicResource Brush.Text.OnAccent}"
                                              Width="9" Height="9" Stretch="Uniform" VerticalAlignment="Center"/>
                                        <TextBlock Text="{DynamicResource ThemeOnboarding_Card_Npc}"
                                                   Foreground="{DynamicResource Brush.Text.OnAccent}"
                                                   FontSize="{StaticResource FontSize.Label}" FontWeight="SemiBold"/>
                                    </StackPanel>
                                </Border>
                                <Border Background="{DynamicResource Brush.Node.Npc.Body}" Padding="6">
                                    <TextBlock Text="{DynamicResource ThemeOnboarding_Sample_Npc}"
                                               Foreground="{DynamicResource Brush.Text.OnLight}"
                                               FontSize="{StaticResource FontSize.Small}" TextWrapping="Wrap"/>
                                </Border>
                            </StackPanel>
                        </Border>
                        <Border Width="140" CornerRadius="4" BorderBrush="{DynamicResource Brush.Border.Default}" BorderThickness="1" ClipToBounds="True">
                            <StackPanel>
                                <Border Background="{DynamicResource Brush.Node.Player.Header}" Padding="6,4">
                                    <StackPanel Orientation="Horizontal" Spacing="4">
                                        <Path Data="M0,0 L9,0 L9,9 L0,9 Z"
                                              Fill="{DynamicResource Brush.Text.OnAccent}"
                                              Width="9" Height="9" Stretch="Uniform" VerticalAlignment="Center"/>
                                        <TextBlock Text="{DynamicResource ThemeOnboarding_Card_Player}"
                                                   Foreground="{DynamicResource Brush.Text.OnAccent}"
                                                   FontSize="{StaticResource FontSize.Label}" FontWeight="SemiBold"/>
                                    </StackPanel>
                                </Border>
                                <Border Background="{DynamicResource Brush.Node.Player.Body}" Padding="6">
                                    <TextBlock Text="{DynamicResource ThemeOnboarding_Sample_Player}"
                                               Foreground="{DynamicResource Brush.Text.OnLight}"
                                               FontSize="{StaticResource FontSize.Small}" TextWrapping="Wrap"/>
                                </Border>
                            </StackPanel>
                        </Border>
                        <Border Width="140" CornerRadius="4" BorderBrush="{DynamicResource Brush.Border.Default}" BorderThickness="1" ClipToBounds="True">
                            <StackPanel>
                                <Border Background="{DynamicResource Brush.Node.Narrator.Header}" Padding="6,4">
                                    <StackPanel Orientation="Horizontal" Spacing="4">
                                        <Path Data="M4.5,0 L9,9 L0,9 Z"
                                              Fill="{DynamicResource Brush.Text.OnAccent}"
                                              Width="9" Height="9" Stretch="Uniform" VerticalAlignment="Center"/>
                                        <TextBlock Text="{DynamicResource ThemeOnboarding_Card_Narrator}"
                                                   Foreground="{DynamicResource Brush.Text.OnAccent}"
                                                   FontSize="{StaticResource FontSize.Label}" FontWeight="SemiBold"/>
                                    </StackPanel>
                                </Border>
                                <Border Background="{DynamicResource Brush.Node.Narrator.Body}" Padding="6">
                                    <TextBlock Text="{DynamicResource ThemeOnboarding_Sample_Narrator}"
                                               Foreground="{DynamicResource Brush.Text.OnLight}"
                                               FontSize="{StaticResource FontSize.Small}" TextWrapping="Wrap"/>
                                </Border>
                            </StackPanel>
                        </Border>
                    </StackPanel>

                    <!-- Severity badges, each with their Layer 2.5 glyph (no
                         Brush.Severity.Success token exists, so there's no "OK" badge). -->
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Border Background="{DynamicResource Brush.Severity.Warning}" CornerRadius="10" Padding="8,3">
                            <StackPanel Orientation="Horizontal" Spacing="4">
                                <TextBlock Text="⚠" Foreground="{DynamicResource Brush.Text.OnAccent}" FontSize="{StaticResource FontSize.Label}"/>
                                <TextBlock Text="{DynamicResource ThemeOnboarding_Badge_Warning}" Foreground="{DynamicResource Brush.Text.OnAccent}" FontSize="{StaticResource FontSize.Label}"/>
                            </StackPanel>
                        </Border>
                        <Border Background="{DynamicResource Brush.Severity.Error}" CornerRadius="10" Padding="8,3">
                            <StackPanel Orientation="Horizontal" Spacing="4">
                                <TextBlock Text="⛔" Foreground="{DynamicResource Brush.Text.OnAccent}" FontSize="{StaticResource FontSize.Label}"/>
                                <TextBlock Text="{DynamicResource ThemeOnboarding_Badge_Error}" Foreground="{DynamicResource Brush.Text.OnAccent}" FontSize="{StaticResource FontSize.Label}"/>
                            </StackPanel>
                        </Border>
                    </StackPanel>

                    <!-- Button-style samples. Decorative only (IsEnabled="False"): they
                         demonstrate the retinted button brushes but trigger nothing, so
                         they carry no tooltip/AutomationProperties. -->
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Button Content="{DynamicResource ThemeOnboarding_Button_Primary}"
                                Background="{DynamicResource Brush.Button.Primary.Background}"
                                Foreground="{DynamicResource Brush.Text.OnAccent}"
                                BorderThickness="0" Padding="12,5" FontSize="{StaticResource FontSize.Label}"
                                IsEnabled="False"/>
                        <Button Content="{DynamicResource ThemeOnboarding_Button_Destructive}"
                                Background="{DynamicResource Brush.Button.Destructive.Background}"
                                Foreground="{DynamicResource Brush.Text.OnAccent}"
                                BorderThickness="0" Padding="12,5" FontSize="{StaticResource FontSize.Label}"
                                IsEnabled="False"/>
                        <Button Content="{DynamicResource ThemeOnboarding_Button_Plain}"
                                Background="{DynamicResource Brush.Toolbar.Button.Background}"
                                Foreground="{DynamicResource Brush.Toolbar.Button.Foreground}"
                                BorderThickness="0" Padding="12,5" FontSize="{StaticResource FontSize.Label}"
                                IsEnabled="False"/>
                    </StackPanel>

                </StackPanel>
            </Border>

        </StackPanel>

        <!-- Continue is self-explanatory in context (CLAUDE.md tooltip exception, like
             OK/Cancel on a confirmation dialog) — no ToolTip.Tip needed. -->
        <Button Grid.Row="1"
                x:Name="ContinueButton"
                Content="{DynamicResource ThemeOnboarding_Continue}"
                HorizontalAlignment="Right"
                Background="{DynamicResource Brush.Button.Primary.Background}"
                Foreground="{DynamicResource Brush.Text.OnAccent}"
                BorderThickness="0" Padding="16,5" Margin="0,12,0,0"/>

        <shared:FocusHintBar Grid.Row="2" x:Name="HintBar"/>

    </Grid>
</Window>
```

- [ ] **Step 5: Create `ThemeOnboardingWindow.axaml.cs`**

Create `DialogEditor.Avalonia.Shared/ThemeOnboardingWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.ViewModels.ViewModels;

namespace DialogEditor.Avalonia.Shared;

public partial class ThemeOnboardingWindow : Window
{
    public ThemeOnboardingWindow()
    {
        InitializeComponent();
        ThemePicker.DataContext = new ThemePickerViewModel(new ThemeApplier());
        HintBar.AttachTo(this);
        ContinueButton.Click += OnContinueClick;
    }

    private void OnContinueClick(object? sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ThemeOnboardingWindowTests"`

Expected: PASS (4 tests).

- [ ] **Step 7: Run the solution-wide structural accessibility tests**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Accessibility|FullyQualifiedName~NoStrayHex|FullyQualifiedName~NoStrayFontSize|FullyQualifiedName~NoNamedColourForeground|FullyQualifiedName~FakeWatermark"`

Expected: PASS. (`FocusHintBarPresenceTests` and `ResizableDialogTests` will still pass at this point because the new window isn't referenced by either yet — Task 5 adds those.)

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.Avalonia.Shared/ThemeOnboardingWindow.axaml DialogEditor.Avalonia.Shared/ThemeOnboardingWindow.axaml.cs DialogEditor.Tests/Controls/ThemeOnboardingWindowTests.cs
git commit -m "feat(onboarding): add ThemeOnboardingWindow with live-retint preview"
```

---

### Task 5: Wire `ThemeOnboardingWindow` into the structural test scanners

**Files:**
- Modify: `DialogEditor.Tests/Theming/ResizableDialogTests.cs`
- Modify: `DialogEditor.Tests/Accessibility/FocusHintBarPresenceTests.cs`

- [ ] **Step 1: Write the failing `ResizableDialogTests` case**

In `DialogEditor.Tests/Theming/ResizableDialogTests.cs`, add the using and the new test line.

Add to the usings at the top:

```csharp
using DialogEditor.Avalonia.Shared;
```

Add a new line among the `[AvaloniaFact]` lines (alphabetical-ish, after `SettingsWindow_IsResizable`):

```csharp
    [AvaloniaFact] public void ThemeOnboardingWindow_IsResizable() => AssertResizable(new ThemeOnboardingWindow(), 480);
```

- [ ] **Step 2: Run it to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ThemeOnboardingWindow_IsResizable"`

Expected: PASS — `ThemeOnboardingWindow.axaml` already sets `MinWidth="480"`, `CanResize="True"`, and `SizeToContent="Height"` from Task 4. This test pins that contract against future accidental regressions, the same way the other entries in this file do for their windows.

- [ ] **Step 3: Write the failing `FocusHintBarPresenceTests` case**

In `DialogEditor.Tests/Accessibility/FocusHintBarPresenceTests.cs`, the existing `WindowsInScope` (item 13) and `WindowsInScopeItem16` arrays each describe a fixed, already-rolled-out set of windows from earlier Gaps items. `ThemeOnboardingWindow` is a new item-15 window, so add a third array + theory rather than repurposing those doc comments.

Add a new private array after `WindowsInScopeItem16`:

```csharp
    /// <summary>
    /// Gaps.md a11y item 15: the first-run theme-onboarding window's ThemePickerView
    /// ComboBox has a substantive AutomationProperties.HelpText (Settings_ThemeTooltip)
    /// beyond its visible "Theme" label, so it gets a FocusHintBar too (see
    /// docs/superpowers/specs/2026-06-14-theme-onboarding-design.md §3.2).
    /// </summary>
    private static readonly string[] WindowsInScopeItem15 =
    {
        "ThemeOnboardingWindow.axaml",
    };
```

Add a corresponding `MemberData` provider and theory after the item-16 ones:

```csharp
    public static IEnumerable<object[]> WindowFilesItem15() => WindowsInScopeItem15.Select(f => new object[] { f });
```

```csharp
    [Theory]
    [MemberData(nameof(WindowFilesItem15))]
    public void Item15WindowHasFocusHintBar(string fileName) => AssertHasFocusHintBar(fileName);
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Item15WindowHasFocusHintBar"`

Expected: PASS — `ThemeOnboardingWindow.axaml` already has `<shared:FocusHintBar x:Name="HintBar"/>` from Task 4.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test DialogEditor.Tests`

Expected: PASS, all tests green.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Tests/Theming/ResizableDialogTests.cs DialogEditor.Tests/Accessibility/FocusHintBarPresenceTests.cs
git commit -m "test(onboarding): pin ThemeOnboardingWindow's resizability and FocusHintBar"
```

---

### Task 6: Wire onboarding into both apps' startup

**Files:**
- Modify: `DialogEditor.Avalonia/App.axaml.cs`
- Modify: `DialogEditor.PatchManager/App.axaml.cs`

Per the design spec §3.3/§4, this glue is not unit-tested — consistent with the existing `ThemeApplier`/`FontScaleApplier` startup calls in the same files.

- [ ] **Step 1: Update `DialogEditor.Avalonia/App.axaml.cs`**

Add a `using` for the shared window's namespace (already covered by `using DialogEditor.Avalonia.Shared.Theming;` for `ThemeApplier`/`FontScaleApplier` — `ThemeOnboardingWindow` is in `DialogEditor.Avalonia.Shared`, so add):

```csharp
using DialogEditor.Avalonia.Shared;
```

Replace the final line of `OnFrameworkInitializationCompleted`:

```csharp
            new FontScaleApplier().Apply(AppSettings.FontScale);
            desktop.MainWindow = new MainWindow();
```

with:

```csharp
            new FontScaleApplier().Apply(AppSettings.FontScale);

            if (!AppSettings.ThemeOnboardingSeen)
            {
                var onboarding = new ThemeOnboardingWindow();
                desktop.MainWindow = onboarding;
                onboarding.Closed += (_, _) =>
                {
                    AppSettings.ThemeOnboardingSeen = true;
                    var main = new MainWindow();
                    desktop.MainWindow = main;
                    main.Show();
                };
                onboarding.Show();
            }
            else
            {
                desktop.MainWindow = new MainWindow();
            }
```

- [ ] **Step 2: Update `DialogEditor.PatchManager/App.axaml.cs`**

Add a `using`:

```csharp
using DialogEditor.Avalonia.Shared;
```

Replace the body of `OnFrameworkInitializationCompleted` from `new ThemeApplier().Apply(...)` onward:

```csharp
            new ThemeApplier().Apply(AppSettings.Theme);
            var window = new MainWindow();
            desktop.MainWindow = window;

            // If a .patchlist was passed as a command-line argument, open it immediately
            var args = desktop.Args ?? [];
            var patchlist = args.FirstOrDefault(a =>
                a.EndsWith(".patchlist", StringComparison.OrdinalIgnoreCase)
                && File.Exists(a));
            if (patchlist is not null)
                window.LoadPatchList(patchlist);
```

with:

```csharp
            new ThemeApplier().Apply(AppSettings.Theme);

            var args = desktop.Args ?? [];
            var patchlist = args.FirstOrDefault(a =>
                a.EndsWith(".patchlist", StringComparison.OrdinalIgnoreCase)
                && File.Exists(a));

            void OpenMainWindow()
            {
                var window = new MainWindow();
                desktop.MainWindow = window;
                if (patchlist is not null)
                    window.LoadPatchList(patchlist);
                window.Show();
            }

            if (!AppSettings.ThemeOnboardingSeen)
            {
                var onboarding = new ThemeOnboardingWindow();
                desktop.MainWindow = onboarding;
                onboarding.Closed += (_, _) =>
                {
                    AppSettings.ThemeOnboardingSeen = true;
                    OpenMainWindow();
                };
                onboarding.Show();
            }
            else
            {
                var window = new MainWindow();
                desktop.MainWindow = window;
                if (patchlist is not null)
                    window.LoadPatchList(patchlist);
            }
```

Note: the `else` branch deliberately does **not** call `OpenMainWindow()`/`window.Show()` — `desktop.MainWindow = window` is enough on the normal startup path (Avalonia shows the configured `MainWindow` itself), exactly as the pre-existing code did. Only the onboarding-handoff path needs an explicit `Show()`, because by that point `OnFrameworkInitializationCompleted` has already returned and Avalonia's automatic "show MainWindow" has already run against the (now-closing) onboarding window.

- [ ] **Step 3: Build both apps to confirm no compile errors**

Run: `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj && dotnet build DialogEditor.PatchManager/DialogEditor.PatchManager.csproj`

Expected: both builds succeed.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test DialogEditor.Tests`

Expected: PASS, all tests green (App.axaml.cs isn't covered by tests, but this confirms nothing else broke).

- [ ] **Step 5: Manual smoke test (optional but recommended)**

Delete or rename your local `%LOCALAPPDATA%\PillarsDialogEditor\settings.json`, then run `dotnet run --project DialogEditor.Avalonia`. Confirm:
- The "Choose a Theme" window appears before the main editor window.
- Changing the theme dropdown retints the preview panel (cards, badges, buttons) live.
- Clicking Continue closes the onboarding window and opens the main editor window.
- Re-launching the app does **not** show the onboarding window again.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/App.axaml.cs DialogEditor.PatchManager/App.axaml.cs
git commit -m "feat(onboarding): show first-run theme picker before MainWindow"
```

---

### Task 7: Mark Gaps.md item 15 implemented

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Update the entry**

In `Gaps.md`, change item 15's heading line from:

```markdown
15. **No first-run theme-picker onboarding.** Raised during item 11(a)'s brainstorm: the
```

to:

```markdown
15. **✅ IMPLEMENTED (2026-06-14).** Raised during item 11(a)'s brainstorm: the
```

Leave the rest of the paragraph as historical context (matches the style of items 14/16's "✅ IMPLEMENTED" entries, which keep their original descriptive text below the marker).

- [ ] **Step 2: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark item 15 (first-run theme onboarding) implemented"
```

---

## Self-review notes

- **Spec coverage:** §3.1 (AppSettings) → Task 1; §3.2 (window/strings/preview/FocusHintBar) → Tasks 2 & 4; §3.4 (icon) → Task 3; §3.5 (localisation) → Task 2; §3.3 (App.axaml.cs wiring, both apps) → Task 6; §4 (tests: AppSettingsTests, ThemeOnboardingWindow construction test, ResizableDialogTests/FocusHintBarPresenceTests, no App.axaml.cs test) → Tasks 1, 4, 5, 6. §5 (out of scope) requires no tasks.
- **Placeholder scan:** no TBD/TODO; every code block is complete and copy-pasteable.
- **Type/name consistency:** `ThemeOnboardingSeen` (AppSettings), `ThemeOnboardingWindow` (Shared), `ThemePicker`/`ContinueButton`/`HintBar` (x:Name in XAML matches code-behind field access via `InitializeComponent()`), `ThemePickerViewModel`/`ThemeApplier`/`FocusHintBar.AttachTo` all match their existing definitions verified during research.
