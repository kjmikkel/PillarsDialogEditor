# Window Sizing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every window follows the font scale and fits a 1366×768 working area. Open windows
resize live when the scale changes, and no button or label is clipped at 1.0× or 2.0×.

**Architecture:**
- A pure rule, `WindowSizing.Compute`/`Rescale`, turns a window's XAML design size, the font
  scale and the screen's working area into concrete sizes.
- A class handler on `Window.WindowOpenedEvent` applies the rule before Avalonia reads the
  window's size. A subscription to `FontScaleService.Scale` rescales windows that are already
  open.
- Two guards keep it honest:
  - one checks that every window type is sized by the mechanism;
  - one renders every window with real text measurement and fails on any clipped text.

**Tech Stack:** .NET 10, Avalonia 11.3.22 (Fluent theme, Skia), xUnit v2, Avalonia.Headless.XUnit.

**Spec:** `docs/superpowers/specs/2026-10-13-window-sizing-design.md`

## Global Constraints

- Tests: always `dotnet test DialogEditor.Tests --filter "Category!=Gui"`. A bare
  `dotnet test` launches the GUI tests.
- The tests run serially. `Application.Current`, `FontScaleService.Current` and the
  `WindowSizing` statics are process-wide, so every test that changes the scale or the
  working-area provider restores it in `finally`.
- No user-visible text inline in XAML or C#. New strings go in `Strings.axaml` /
  `SharedStrings.axaml`, plus each `Strings.{lang}.axaml` that already exists.
- Every interactive control keeps a `ToolTip`. Controls stay discoverable by UIA Name.
- Production `catch` blocks log through `AppLog`. There are none planned.
- Comments cite issues as "issue 177", not "#177". Put the reasoning in the files themselves.
- `CHANGELOG.md` is frozen. Don't touch it.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Clamp margin: **16 DIP from each screen edge**, so a 1366×768 working area limits windows
  to 1334×736.

## Plan corrections to the spec (rulings made while planning)

- **Hook:** the spec named `TemplateApplied`. Avalonia 11.3's `Window.ShowCore`, read at
  `release/11.3.0`, applies the template during `ExecuteInitialLayoutPass`. That is *after* it
  has read `Width`, `Height` and the min/max values into the initial client size. But
  `ShowCore` raises `WindowOpenedEvent` as its very first step, before `EnsureInitialized`,
  `ApplyStyling` and any size read. Avalonia documents that event as "for global tracking of
  opening windows". The plan uses `WindowOpenedEvent` (and `WindowClosedEvent`). This is the
  spec's ordering requirement, met by a better hook.
- **Screen:** the spec said "the owner's screen". `ShowCore` assigns `Owner` *after* raising
  `WindowOpenedEvent`, so the owner isn't known yet when the handler runs. The plan uses the
  screen of the desktop lifetime's `MainWindow`, which owns nearly every dialog, and falls
  back to the primary screen.
- **Margin:** the spec's "16 DIP on each axis" means 16 DIP from each edge. See Global
  Constraints.
- **Headless text measurement:** the default headless renderer measures every glyph at 10 px,
  whatever the font size (verified: "Hello" is 50 px wide at both 12 and 24). A clipping guard
  is meaningless with that renderer. Task 1 switches the test host to Skia rendering. A trial
  run of the whole suite under Skia passed: 3562 passed, 10 skipped.
- **Theme switching resets the font scale.** `ThemeApplier.Apply` reloads `Tokens.axaml`,
  which resets every font token to its 1.0 value while `AppSettings.FontScale` and the window
  sizes stay scaled. That would break this feature's invariant that window scale equals text
  scale. Task 3 makes `ThemeApplier` re-apply the current scale. No existing issue covered
  this.

## Review Focus

1. **A high-DPI or second monitor.** `Screen.WorkingArea` is in physical pixels. A 2732×1536
   screen at 200% must clamp like a 1366×768 one. This is pinned in Task 4
   (`WorkingAreaInDips_DividesByScaling`).
2. **A window shown, hidden and shown again.** Its design size must not compound: it must be
   2× the XAML size, not 4×. Pinned in Task 4 (`ReopenedWindow_DoesNotCompoundScale`).
3. **The scale changes after a window has closed.** Closed windows must not be resized and
   must not throw. Pinned in Task 4 (`ClosedWindow_IsNotRescaled`).
4. **The theme switches while scaled.** The text must stay at the scaled size. Pinned in
   Task 3 (`ThemeSwitch_KeepsFontScale`).
5. **You resize a window, then change the scale.** Your width scales by the same ratio and is
   still clamped to the screen. Pinned in Task 2 (`Rescale_KeepsUserResizeInProportion`,
   `Rescale_ClampsToScreen`).

---

### Task 1: Real text measurement in the headless test host

**Files:**
- Modify: `DialogEditor.Tests/AvaloniaTestApp.cs`
- Create: `DialogEditor.Tests/Theming/HeadlessTextMeasurementTests.cs`

**Interfaces:**
- Produces: a test host where `TextBlock` measurement scales with `FontSize`. Tasks 6 and 7
  depend on it.

- [ ] **Step 1: Write the failing test**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// The clipping guards (issue 177) need real text measurement. The default headless renderer
/// measures every glyph at a fixed width whatever the font size, so text never "grows" with the
/// font scale and nothing could ever be found clipped. This pins the Skia-backed host.
/// </summary>
public class HeadlessTextMeasurementTests
{
    [AvaloniaFact]
    public void TextWidth_GrowsWithFontSize()
    {
        var small = new TextBlock { Text = "Hello", FontSize = 12 };
        var large = new TextBlock { Text = "Hello", FontSize = 24 };
        small.Measure(Size.Infinity);
        large.Measure(Size.Infinity);

        Assert.InRange(large.DesiredSize.Width / small.DesiredSize.Width, 1.8, 2.2);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~HeadlessTextMeasurementTests"`
Expected: FAIL. The ratio is 1.0, because both widths are 50.

- [ ] **Step 3: Switch the host to Skia**

Replace the body of `DialogEditor.Tests/AvaloniaTestApp.cs` with:

```csharp
using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(DialogEditor.Tests.TestAppBuilder))]

namespace DialogEditor.Tests;

public class TestAppBuilder
{
    // Skia rendering instead of the headless drawing stub (issue 177): the stub measures every
    // glyph at a fixed width regardless of FontSize, which makes layout tests at a font scale
    // meaningless. Skia measures with the real fonts the app ships.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DialogEditor.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
```

- [ ] **Step 4: Run the test, then the whole suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~HeadlessTextMeasurementTests"`
Expected: PASS.

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: all pass. At planning time that was 3563 passed (3562 plus this test) and 10 skipped.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Tests/AvaloniaTestApp.cs DialogEditor.Tests/Theming/HeadlessTextMeasurementTests.cs
git commit -m "test: measure text with Skia in headless tests (#177)"
```

---

### Task 2: The pure sizing rule

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Theming/WindowSizingRule.cs`
- Test: `DialogEditor.Tests/Theming/WindowSizingRuleTests.cs`

**Interfaces:**
- Produces, in namespace `DialogEditor.Avalonia.Shared.Theming`:
  - `public readonly record struct WindowSize(double Width, double Height, double MinWidth, double MinHeight, double MaxWidth, double MaxHeight)`
    - `static WindowSize Of(Window w)`
    - `void ApplyTo(Window w)`
  - `public enum WindowSizingMode { Scale, ClampOnly }`
  - `public static class WindowSizingRule`
    - `const double ScreenMargin = 16`
    - `static WindowSize Compute(WindowSize design, double scale, Size? workingArea, WindowSizingMode mode)`
    - `static WindowSize Rescale(WindowSize current, WindowSize design, double oldScale, double newScale, Size? workingArea, WindowSizingMode mode)`
    - `static Size WorkingAreaInDips(PixelSize workingArea, double scaling)`

- [ ] **Step 1: Write the failing tests**

```csharp
using Avalonia;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

/// <summary>Issue 177: the pure rule that sizes windows from their design size, the font
/// scale and the screen. See docs/superpowers/specs/2026-10-13-window-sizing-design.md.</summary>
public class WindowSizingRuleTests
{
    private static readonly Size Small = new(1366, 768);   // limits: 1334 × 736
    private static WindowSize Design(double w = 420, double h = double.NaN, double minW = 420,
        double minH = 0, double maxW = double.PositiveInfinity, double maxH = double.PositiveInfinity)
        => new(w, h, minW, minH, maxW, maxH);

    [Fact]
    public void Compute_AtOne_WithoutScreen_IsIdentity()
        => Assert.Equal(Design(), WindowSizingRule.Compute(Design(), 1.0, null, WindowSizingMode.Scale));

    [Fact]
    public void Compute_AtTwo_DoublesFiniteDimensions()
    {
        var s = WindowSizingRule.Compute(Design(w: 420, h: 300, minW: 400, minH: 200, maxW: 500, maxH: 520),
                                         2.0, null, WindowSizingMode.Scale);
        Assert.Equal(new WindowSize(840, 600, 800, 400, 1000, 1040), s);
    }

    [Fact]
    public void Compute_KeepsNaNZeroAndInfinity()
    {
        var s = WindowSizingRule.Compute(Design(h: double.NaN, minH: 0), 2.0, null, WindowSizingMode.Scale);
        Assert.True(double.IsNaN(s.Height));
        Assert.Equal(0, s.MinHeight);
        Assert.True(double.IsPositiveInfinity(s.MaxWidth));
    }

    [Fact]
    public void Compute_ClampsEverythingToTheWorkingAreaLessMargins()
    {
        var s = WindowSizingRule.Compute(Design(w: 820, h: 620, minW: 700, minH: 400), 2.0, Small, WindowSizingMode.Scale);
        Assert.Equal(new WindowSize(1334, 736, 1334, 736, 1334, 736), s);
    }

    [Fact]
    public void Compute_ClampsMinimumsSoTheyCannotForceAnOversizeWindow()
    {
        var s = WindowSizingRule.Compute(Design(w: 500, minW: 500, minH: 420), 2.0, Small, WindowSizingMode.Scale);
        Assert.Equal(1000, s.Width);
        Assert.Equal(1000, s.MinWidth);
        Assert.Equal(736, s.MinHeight);
        Assert.Equal(736, s.MaxHeight);
    }

    [Fact]
    public void Compute_ClampOnly_DoesNotScaleButStillClamps()
    {
        var s = WindowSizingRule.Compute(Design(w: 1400, h: 850, minW: 0), 2.0, Small, WindowSizingMode.ClampOnly);
        Assert.Equal(new WindowSize(1334, 736, 0, 0, 1334, 736), s);
    }

    [Fact]
    public void Compute_WithoutScreen_ScalesButDoesNotClamp()
    {
        var s = WindowSizingRule.Compute(Design(w: 1400, minW: 0), 2.0, null, WindowSizingMode.Scale);
        Assert.Equal(2800, s.Width);
        Assert.True(double.IsPositiveInfinity(s.MaxWidth));
    }

    [Fact]
    public void Rescale_KeepsUserResizeInProportion()
    {
        var design  = Design(w: 420, minW: 420);
        var current = design with { Width = 600 };            // you widened it at 1.0
        var s = WindowSizingRule.Rescale(current, design, 1.0, 2.0, null, WindowSizingMode.Scale);
        Assert.Equal(1200, s.Width);
        Assert.Equal(840, s.MinWidth);
    }

    [Fact]
    public void Rescale_ClampsToScreen()
    {
        var design  = Design(w: 420, minW: 420);
        var current = design with { Width = 900 };
        var s = WindowSizingRule.Rescale(current, design, 1.0, 2.0, Small, WindowSizingMode.Scale);
        Assert.Equal(1334, s.Width);
    }

    [Fact]
    public void Rescale_BackDown_RestoresTheDesignMinimum()
    {
        var design  = Design(w: 420, minW: 420);
        var current = design with { Width = 840, MinWidth = 840 };
        var s = WindowSizingRule.Rescale(current, design, 2.0, 1.0, null, WindowSizingMode.Scale);
        Assert.Equal(420, s.Width);
        Assert.Equal(420, s.MinWidth);
    }

    [Fact]
    public void Rescale_KeepsAutoHeightAuto()
    {
        var design = Design(h: double.NaN);
        var s = WindowSizingRule.Rescale(design, design, 1.0, 2.0, Small, WindowSizingMode.Scale);
        Assert.True(double.IsNaN(s.Height));
    }

    [Fact]
    public void WorkingAreaInDips_DividesByScaling()
        => Assert.Equal(new Size(1366, 768), WindowSizingRule.WorkingAreaInDips(new PixelSize(2732, 1536), 2.0));
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~WindowSizingRuleTests"`
Expected: build error CS0246. `WindowSize`, `WindowSizingRule` and `WindowSizingMode` don't
exist.

- [ ] **Step 3: Implement**

```csharp
using Avalonia;
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>The six size values of a window. NaN means "auto" (SizeToContent), as in Avalonia.</summary>
public readonly record struct WindowSize(
    double Width, double Height,
    double MinWidth, double MinHeight,
    double MaxWidth, double MaxHeight)
{
    public static WindowSize Of(Window w) =>
        new(w.Width, w.Height, w.MinWidth, w.MinHeight, w.MaxWidth, w.MaxHeight);

    // Limits first, so Width/Height are never briefly outside them.
    public void ApplyTo(Window w)
    {
        w.MinWidth = MinWidth;   w.MinHeight = MinHeight;
        w.MaxWidth = MaxWidth;   w.MaxHeight = MaxHeight;
        w.Width    = Width;      w.Height    = Height;
    }
}

/// <summary>Scale: window geometry follows the font scale. ClampOnly: only kept on screen
/// (main windows, whose panes already resize).</summary>
public enum WindowSizingMode { Scale, ClampOnly }

/// <summary>
/// The pure sizing rule behind issue 177. The font scale changes text size but not layout
/// units, so a Width="420" dialog stays 420 while its text doubles and its buttons are cut
/// off. This multiplies a window's XAML design size by the scale, then clamps every dimension,
/// minimums included, to the screen's working area. Clamping only Width would not be enough:
/// Avalonia lets MinWidth win over MaxWidth, so a doubled MinWidth could still push a window
/// off a 1366×768 screen. See docs/superpowers/specs/2026-10-13-window-sizing-design.md.
/// </summary>
public static class WindowSizingRule
{
    /// <summary>DIPs kept free between a clamped window and each screen edge.</summary>
    public const double ScreenMargin = 16;

    public static WindowSize Compute(WindowSize design, double scale, Size? workingArea, WindowSizingMode mode)
    {
        var k = mode == WindowSizingMode.Scale ? scale : 1.0;
        // NaN, 0 and Infinity survive multiplication by a positive k, which is what we want.
        var scaled = new WindowSize(
            design.Width * k, design.Height * k,
            design.MinWidth * k, design.MinHeight * k,
            design.MaxWidth * k, design.MaxHeight * k);
        return Clamp(scaled, workingArea);
    }

    /// <summary>
    /// Live rescale of an open window. Width and Height scale by newScale/oldScale from their
    /// CURRENT values, so a window you resized keeps that size in proportion. The limits are
    /// recomputed from the design size, so they never drift.
    /// </summary>
    public static WindowSize Rescale(WindowSize current, WindowSize design, double oldScale, double newScale,
                                     Size? workingArea, WindowSizingMode mode)
    {
        var ratio  = mode == WindowSizingMode.Scale ? newScale / oldScale : 1.0;
        var limits = Compute(design, newScale, workingArea, mode);
        return limits with
        {
            Width  = Fit(current.Width  * ratio, limits.MinWidth,  limits.MaxWidth),
            Height = Fit(current.Height * ratio, limits.MinHeight, limits.MaxHeight),
        };
    }

    /// <summary>Screen.WorkingArea is in physical pixels; window sizes are in DIPs.</summary>
    public static Size WorkingAreaInDips(PixelSize workingArea, double scaling) =>
        workingArea.ToSize(scaling);

    private static WindowSize Clamp(WindowSize s, Size? workingArea)
    {
        if (workingArea is not { } area) return s;
        var maxW = Math.Max(0, area.Width  - 2 * ScreenMargin);
        var maxH = Math.Max(0, area.Height - 2 * ScreenMargin);
        return new WindowSize(
            Cap(s.Width, maxW), Cap(s.Height, maxH),
            Math.Min(s.MinWidth, maxW), Math.Min(s.MinHeight, maxH),
            Math.Min(s.MaxWidth, maxW), Math.Min(s.MaxHeight, maxH));
    }

    private static double Cap(double v, double limit) => double.IsNaN(v) ? v : Math.Min(v, limit);

    private static double Fit(double v, double min, double max) =>
        double.IsNaN(v) ? v : Math.Max(min, Math.Min(v, max));
}
```

- [ ] **Step 4: Run them and watch them pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~WindowSizingRuleTests"`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Theming/WindowSizingRule.cs DialogEditor.Tests/Theming/WindowSizingRuleTests.cs
git commit -m "feat: pure rule that sizes windows from the font scale and the screen (#177)"
```

---

### Task 3: `FontScaleService.Scale`, and theme switches keep the scale

**Files:**
- Modify: `DialogEditor.Avalonia.Shared/Theming/FontScaleService.cs`
- Modify: `DialogEditor.Avalonia.Shared/Theming/FontScaleApplier.cs` (`Apply`)
- Modify: `DialogEditor.Avalonia.Shared/Theming/ThemeApplier.cs` (end of `Apply`)
- Test: `DialogEditor.Tests/Theming/FontScaleApplierTests.cs`, `DialogEditor.Tests/Theming/ThemeApplierTests.cs`

**Interfaces:**
- Produces: `FontScaleService.Current.Scale`, a `double` with an internal setter, starting at
  1.0, that raises `PropertyChanged(nameof(Scale))` when it changes.

- [ ] **Step 1: Write the failing tests**

Add to `FontScaleApplierTests`:

```csharp
    [AvaloniaFact]
    public void Apply_PublishesTheScale()
    {
        try
        {
            new FontScaleApplier().Apply(1.5);
            Assert.Equal(1.5, FontScaleService.Current.Scale);
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }
```

Add to `ThemeApplierTests` (with `using DialogEditor.Avalonia.Shared.Theming;` if missing):

```csharp
    // Issue 177: ThemeApplier reloads Tokens.axaml, which used to reset every font token to
    // its 1.0 value while the windows (and AppSettings.FontScale) stayed scaled.
    [AvaloniaFact]
    public void ThemeSwitch_KeepsFontScale()
    {
        try
        {
            new FontScaleApplier().Apply(2.0);
            new ThemeApplier().Apply("Light");
            Assert.True(Application.Current!.TryGetResource("FontSize.Body", Application.Current.ActualThemeVariant, out var v));
            Assert.Equal(24.0, v);
        }
        finally
        {
            new FontScaleApplier().Apply(1.0);
            new ThemeApplier().Apply("Dark");
        }
    }
```

First check how `ThemeApplierTests` restores the theme. If it restores to something other
than `"Dark"`, use the same restore it uses.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Apply_PublishesTheScale|FullyQualifiedName~ThemeSwitch_KeepsFontScale"`
Expected:
- `Apply_PublishesTheScale` fails to compile, because `Scale` doesn't exist. Comment it out
  for one run if you want to see the second test fail on its own.
- `ThemeSwitch_KeepsFontScale` fails with Expected 24, Actual 12.

- [ ] **Step 3: Implement**

In `FontScaleService.cs`, add this inside the class, after the `_revision` field:

```csharp
    private double _scale = 1.0;

    /// <summary>The font scale currently applied (1.0–2.0). Set only by FontScaleApplier.Apply;
    /// WindowSizing subscribes to it to resize open windows live (issue 177).</summary>
    public double Scale
    {
        get => _scale;
        internal set => SetProperty(ref _scale, value);
    }
```

In `FontScaleApplier.Apply`, replace `FontScaleService.Current.Bump();` with:

```csharp
        FontScaleService.Current.Scale = scale;
        FontScaleService.Current.Bump();
```

In `ThemeApplier.Apply`, after `dicts.Insert(0, wrapper);`, add:

```csharp
        // The fresh Tokens.axaml carries unscaled FontSize.* values (issue 177): re-apply the
        // current font scale, or a theme switch silently shrinks the text back to 1.0 while the
        // windows sized for it stay scaled.
        new FontScaleApplier().Apply(FontScaleService.Current.Scale);
```

- [ ] **Step 4: Run them, then the whole suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~FontScaleApplierTests|FullyQualifiedName~ThemeApplierTests"`
Expected: PASS.

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Theming DialogEditor.Tests/Theming
git commit -m "fix: theme switches keep the font scale; publish the applied scale (#177)"
```

---

### Task 4: `WindowSizing`, the runtime behaviour, registered in both apps

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Theming/WindowSizing.cs`
- Modify: `DialogEditor.Avalonia/App.axaml.cs` (top of `OnFrameworkInitializationCompleted`)
- Modify: `DialogEditor.PatchManager/App.axaml.cs` (top of `OnFrameworkInitializationCompleted`)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (root element)
- Modify: `DialogEditor.PatchManager/MainWindow.axaml` (root element)
- Test: `DialogEditor.Tests/Theming/WindowSizingTests.cs`, `DialogEditor.Tests/Theming/EveryWindowIsSizedTests.cs`

**Interfaces:**
- Consumes: Task 2 (`WindowSize`, `WindowSizingMode`, `WindowSizingRule`) and Task 3
  (`FontScaleService.Current.Scale`).
- Produces, in `DialogEditor.Avalonia.Shared.Theming.WindowSizing`:
  - `static void Register()`, which is idempotent;
  - `static readonly AttachedProperty<WindowSizingMode> ModeProperty`, with
    `GetMode(Window)` / `SetMode(Window, WindowSizingMode)`;
  - `static Func<Window, Size?> WorkingAreaProvider { get; set; }`, the seam tests use for a
    1366×768 screen;
  - `static double? GetAppliedScale(Window)`, which is null until the window has been sized.

- [ ] **Step 1: Write the failing behaviour tests**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

/// <summary>Issue 177: windows are sized from their design size and the font scale when they
/// open, and resized live when the scale changes. Every test restores scale 1.0 and the
/// working-area provider.</summary>
public class WindowSizingTests
{
    private static void Run(Size? workingArea, Action body)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => workingArea;
        try { body(); }
        finally
        {
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }

    private static Window Dialog() =>
        new() { Width = 300, MinWidth = 200, SizeToContent = SizeToContent.Height, Content = new TextBlock { Text = "x" } };

    [AvaloniaFact]
    public void OpenedAtDoubleScale_IsSizedBeforeAvaloniaReadsItsSize() => Run(null, () =>
    {
        new FontScaleApplier().Apply(2.0);
        var w = Dialog();
        w.Show();
        Assert.Equal(400, w.MinWidth);
        Assert.Equal(600, w.ClientSize.Width);   // the initial client size came from the scaled Width
        Assert.Equal(2.0, WindowSizing.GetAppliedScale(w));
        w.Close();
    });

    [AvaloniaFact]
    public void ClampOnlyWindow_IsClampedNotScaled() => Run(new Size(1366, 768), () =>
    {
        new FontScaleApplier().Apply(2.0);
        var w = new Window { Width = 1400, Height = 850 };
        WindowSizing.SetMode(w, WindowSizingMode.ClampOnly);
        w.Show();
        Assert.Equal(1334, w.ClientSize.Width);
        Assert.Equal(736, w.ClientSize.Height);
        w.Close();
    });

    [AvaloniaFact]
    public void OpenWindow_ResizesLiveWhenTheScaleChanges() => Run(null, () =>
    {
        var w = Dialog();
        w.Show();
        new FontScaleApplier().Apply(2.0);
        Assert.Equal(400, w.MinWidth);
        Assert.Equal(600, w.Width);
        new FontScaleApplier().Apply(1.0);
        Assert.Equal(200, w.MinWidth);
        Assert.Equal(300, w.Width);
        w.Close();
    });

    [AvaloniaFact]
    public void ReopenedWindow_DoesNotCompoundScale() => Run(null, () =>
    {
        new FontScaleApplier().Apply(2.0);
        var w = Dialog();
        w.Show();
        w.Hide();
        w.Show();
        Assert.Equal(400, w.MinWidth);
        w.Close();
    });

    [AvaloniaFact]
    public void ClosedWindow_IsNotRescaled() => Run(null, () =>
    {
        var w = Dialog();
        w.Show();
        w.Close();
        new FontScaleApplier().Apply(2.0);   // must not throw or touch the closed window
        Assert.Equal(200, w.MinWidth);
    });

    [AvaloniaFact]
    public void EditorMainWindow_IsClampOnly() =>
        Assert.Equal(WindowSizingMode.ClampOnly, WindowSizing.GetMode(new DialogEditor.Avalonia.Views.MainWindow()));

    [AvaloniaFact]
    public void PatchManagerMainWindow_IsClampOnly() =>
        Assert.Equal(WindowSizingMode.ClampOnly, WindowSizing.GetMode(new DialogEditor.PatchManager.MainWindow()));
}
```

- [ ] **Step 2: Write the failing guard**

```csharp
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Guard for issue 177: every window type in both apps is sized by WindowSizing. A window that
/// bypassed it would stay at its XAML pixel size while its text follows the font scale. Opens
/// each window type with a parameterless constructor at 2.0×.
/// </summary>
public class EveryWindowIsSizedTests
{
    public EveryWindowIsSizedTests() => Loc.Configure(new StubStringProvider());

    private static readonly Assembly[] Apps =
    [
        typeof(DialogEditor.Avalonia.App).Assembly,
        typeof(DialogEditor.Avalonia.Shared.ThemeOnboardingWindow).Assembly,
        typeof(DialogEditor.PatchManager.App).Assembly,
    ];

    public static IEnumerable<object[]> WindowTypes() =>
        Apps.SelectMany(a => a.GetTypes())
            .Where(t => typeof(Window).IsAssignableFrom(t) && !t.IsAbstract
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName)
            .Select(t => new object[] { t.AssemblyQualifiedName! });

    [AvaloniaTheory]
    [MemberData(nameof(WindowTypes))]
    public void Window_IsSizedByWindowSizing(string typeName)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => null;
        var w = (Window)Activator.CreateInstance(Type.GetType(typeName)!)!;
        var designMinWidth = w.MinWidth;
        try
        {
            new FontScaleApplier().Apply(2.0);
            w.Show();
            Assert.Equal(2.0, WindowSizing.GetAppliedScale(w));
            var expected = WindowSizing.GetMode(w) == WindowSizingMode.Scale ? designMinWidth * 2 : designMinWidth;
            Assert.Equal(expected, w.MinWidth);
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }
}
```

- [ ] **Step 3: Run them and watch them fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~WindowSizingTests|FullyQualifiedName~EveryWindowIsSizedTests"`
Expected: build error CS0103/CS0117. `WindowSizing` doesn't exist.

- [ ] **Step 4: Implement `WindowSizing`**

```csharp
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Applies <see cref="WindowSizingRule"/> to every window (issue 177).
///
/// Hook: Window.WindowOpenedEvent. Avalonia's Window.ShowCore raises it as its very first step,
/// before it reads Width/Height/Min/Max into the initial client size and before startup
/// positioning. So a window is sized before it appears, and CenterOwner centres it at the new
/// size. (TemplateApplied, the spec's first idea, fires during the initial layout pass, after
/// the size has already been read.)
///
/// The XAML-authored size is captured the first time a window opens, as its design size.
/// Reopening re-derives from it, so the scale never compounds. Live scale changes rescale every
/// open window by new/old from its current size, which keeps your own resizing in proportion.
/// See docs/superpowers/specs/2026-10-13-window-sizing-design.md.
/// </summary>
public static class WindowSizing
{
    public static readonly AttachedProperty<WindowSizingMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<Window, WindowSizingMode>("Mode", typeof(WindowSizing));

    public static WindowSizingMode GetMode(Window w) => w.GetValue(ModeProperty);
    public static void SetMode(Window w, WindowSizingMode value) => w.SetValue(ModeProperty, value);

    /// <summary>The working area (DIPs) a window must fit, or null when no screen is known.
    /// Replaceable so tests can stand in a 1366×768 screen.</summary>
    public static Func<Window, Size?> WorkingAreaProvider { get; set; } = ScreenWorkingArea;

    private sealed class State
    {
        public required WindowSize Design;
        public double AppliedScale;
    }

    private static readonly ConditionalWeakTable<Window, State> States = new();
    private static readonly HashSet<Window> Open = new();
    private static bool _registered;

    public static double? GetAppliedScale(Window w) =>
        States.TryGetValue(w, out var s) ? s.AppliedScale : null;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        Window.WindowOpenedEvent.AddClassHandler<Window, RoutedEventArgs>((w, _) => OnOpening(w));
        Window.WindowClosedEvent.AddClassHandler<Window, RoutedEventArgs>((w, _) => Open.Remove(w));
        FontScaleService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontScaleService.Scale)) RescaleOpenWindows();
        };
    }

    private static void OnOpening(Window w)
    {
        var scale = FontScaleService.Current.Scale;
        if (!States.TryGetValue(w, out var state))
        {
            state = new State { Design = WindowSize.Of(w) };
            States.Add(w, state);
        }
        WindowSizingRule.Compute(state.Design, scale, WorkingAreaProvider(w), GetMode(w)).ApplyTo(w);
        state.AppliedScale = scale;
        Open.Add(w);
    }

    private static void RescaleOpenWindows()
    {
        var scale = FontScaleService.Current.Scale;
        foreach (var w in Open.ToList())
        {
            if (!States.TryGetValue(w, out var state) || state.AppliedScale == scale) continue;
            WindowSizingRule.Rescale(WindowSize.Of(w), state.Design, state.AppliedScale, scale,
                                     WorkingAreaProvider(w), GetMode(w)).ApplyTo(w);
            state.AppliedScale = scale;
        }
    }

    // The owner is not assigned yet when WindowOpenedEvent fires, so use the main window's
    // screen (it owns nearly every dialog), else the primary screen.
    private static Size? ScreenWorkingArea(Window w)
    {
        var screens = w.Screens;
        if (screens is null) return null;
        Screen? screen = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
            && !ReferenceEquals(main, w) && main.IsVisible)
            screen = screens.ScreenFromWindow(main);
        screen ??= screens.Primary;
        return screen is null ? null : WindowSizingRule.WorkingAreaInDips(screen.WorkingArea.Size, screen.Scaling);
    }
}
```

If `AddClassHandler<Window, RoutedEventArgs>` doesn't compile, look up the Avalonia 11.3
signature with context7 or the Avalonia source before guessing. The non-generic form is
`Window.WindowOpenedEvent.AddClassHandler(typeof(Window), (s, e) => OnOpening((Window)s!))`.

- [ ] **Step 5: Register it in both apps, and mark the main windows `ClampOnly`**

In `DialogEditor.Avalonia/App.axaml.cs`, make this the first statement of
`OnFrameworkInitializationCompleted`, before the `if (ApplicationLifetime is …)`:

```csharp
        // Every window follows the font scale and fits the screen (issue 177). Outside the
        // desktop-lifetime branch so headless tests get the same behaviour.
        WindowSizing.Register();
```

Make the same addition, with the same comment, at the top of
`DialogEditor.PatchManager/App.axaml.cs`'s `OnFrameworkInitializationCompleted`. Add
`using DialogEditor.Avalonia.Shared.Theming;` where it's missing. The editor's `App` already
uses `FontScaleApplier`, so check its usings first.

On the root `<Window>` of `DialogEditor.Avalonia/Views/MainWindow.axaml` and
`DialogEditor.PatchManager/MainWindow.axaml`:
- add the attribute
  `xmlns:theming="clr-namespace:DialogEditor.Avalonia.Shared.Theming;assembly=DialogEditor.Avalonia.Shared"`;
- add `theming:WindowSizing.Mode="ClampOnly"`;
- add, as the first child comment:
  `<!-- ClampOnly (issue 177): the panes already resize, so the main window is only kept on screen, never scaled. -->`

- [ ] **Step 6: Run them, then the whole suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~WindowSizingTests|FullyQualifiedName~EveryWindowIsSizedTests|FullyQualifiedName~ResizableDialogTests"`
Expected: PASS. `ResizableDialogTests` runs at 1.0 and keeps its values.

A window type whose parameterless constructor throws headless isn't a sizing failure. Exclude
it in `WindowTypes()` with a comment naming the exception, and ledger a ruling.

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Theming/WindowSizing.cs DialogEditor.Avalonia/App.axaml.cs DialogEditor.PatchManager/App.axaml.cs DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.PatchManager/MainWindow.axaml DialogEditor.Tests/Theming
git commit -m "feat: windows follow the font scale and fit the screen, live (#177)"
```

---

### Task 5: No fixed sizes on interactive controls

**Files:**
- Create: `DialogEditor.Tests/Theming/NoFixedInteractiveSizeTests.cs`
- Modify:
  - `DialogEditor.Avalonia/Views/AnnotationView.axaml`, the eight header buttons;
  - `DialogEditor.Avalonia/Views/NodeDetailView.axaml`, the `ComboBox Height="22"` and the
    `NumericUpDown Width="72"`;
  - `DialogEditor.Avalonia/Views/GameBrowserView.axaml` and
    `DialogEditor.Avalonia/Views/ConversationView.axaml`, the `TextBox Height="26"` and the
    `Button Width="24" Height="24"`;
  - `DialogEditor.Avalonia/Views/TagReferenceWindow.axaml`, the `TextBox Width="280"`.

- [ ] **Step 1: Write the failing guard**

```csharp
using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Guard for issue 177: interactive controls never get a fixed Width or Height. Their text
/// follows the font scale (issue 176), so a fixed box clips it at large scales. Use
/// MinWidth/MinHeight instead.
/// </summary>
public class NoFixedInteractiveSizeTests
{
    private static readonly HashSet<string> Interactive =
        ["Button", "ToggleButton", "ComboBox", "TextBox", "AutoCompleteBox", "CheckBox", "RadioButton", "NumericUpDown"];

    private static readonly string[] Projects =
        ["DialogEditor.Avalonia", "DialogEditor.Avalonia.Shared", "DialogEditor.PatchManager"];

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void InteractiveControls_HaveNoFixedWidthOrHeight()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();
        foreach (var project in Projects)
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, project), "*.axaml", SearchOption.AllDirectories))
        {
            var segments = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Contains("bin") || segments.Contains("obj")) continue;

            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var el in doc.Descendants().Where(e => Interactive.Contains(e.Name.LocalName)))
            foreach (var attr in new[] { "Width", "Height" })
                if (el.Attribute(attr) is { } a)
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{((IXmlLineInfo)el).LineNumber}: <{el.Name.LocalName}> {attr}=\"{a.Value}\"");
        }

        Assert.True(offenders.Count == 0,
            "Interactive controls must use MinWidth/MinHeight, not a fixed Width/Height, so their text can follow the font scale (issue 177):\n"
            + string.Join("\n", offenders));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NoFixedInteractiveSizeTests"`
Expected: FAIL, listing the 25 offenders the planning survey found:
- `AnnotationView.axaml`: 8 buttons × Width and Height (16);
- `NodeDetailView.axaml`: 2;
- `GameBrowserView.axaml`: 3;
- `ConversationView.axaml`: 3;
- `TagReferenceWindow.axaml`: 1.

Every one is fixed in Step 3.

- [ ] **Step 3: Replace each fixed size with a minimum**

On interactive controls only, rename each attribute in place:
- `Width="N"` becomes `MinWidth="N"`;
- `Height="N"` becomes `MinHeight="N"`.

Keep the values. In `AnnotationView.axaml`, for example, every
`<Button Width="24" Height="24"` becomes `<Button MinWidth="24" MinHeight="24"`.

Run this to see each line before editing:

```bash
grep -n '<\(Button\|ComboBox\|TextBox\|NumericUpDown\)[^>]*\(Width\|Height\)="' DialogEditor.Avalonia/Views/AnnotationView.axaml DialogEditor.Avalonia/Views/NodeDetailView.axaml DialogEditor.Avalonia/Views/GameBrowserView.axaml DialogEditor.Avalonia/Views/ConversationView.axaml DialogEditor.Avalonia/Views/TagReferenceWindow.axaml
```

Attributes on a continuation line of a multi-line element won't show in that grep. The
test's output names their line numbers.

At the first edited element in each file, add the comment
`<!-- Min, not fixed, sizes (issue 177): the text follows the font scale. -->`.

- [ ] **Step 4: Run it, then the whole suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NoFixedInteractiveSizeTests"`
Expected: PASS.

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Tests/Theming/NoFixedInteractiveSizeTests.cs DialogEditor.Avalonia/Views
git commit -m "fix: interactive controls use minimum sizes so their text can grow (#177)"
```

---

### Task 6: Tall windows scroll, and their action row stays on screen

**Files:**
- Modify: `DialogEditor.Avalonia/Views/SettingsWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/AboutWindow.axaml`
- Modify: `DialogEditor.Avalonia.Shared/ThemeOnboardingWindow.axaml`
- Test: `DialogEditor.Tests/Theming/TallWindowsFitTests.cs`

**Interfaces:**
- Consumes: Task 4 (`WindowSizing.Register`, `WorkingAreaProvider`) and Task 1 (real text
  measurement).
- Produces: `x:Name="CloseButton"` on Settings' and About's Close buttons. Theme Onboarding
  already has `ContinueButton`.

- [ ] **Step 1: Write the failing test**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>Issue 177: at 2.0× on a 1366×768 screen, Settings, About and Theme Onboarding are
/// clamped in height; their bodies scroll, so the action button stays inside the window.</summary>
public class TallWindowsFitTests
{
    public TallWindowsFitTests() => Loc.Configure(new StubStringProvider());

    private static void AssertButtonVisible(Window w, string buttonName)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => new Size(1366, 768);
        try
        {
            new FontScaleApplier().Apply(2.0);
            w.Show();
            var button = w.FindControl<Button>(buttonName)!;
            var bottom = button.TranslatePoint(new Point(0, button.Bounds.Height), w)!.Value.Y;
            Assert.True(w.ClientSize.Height <= 736, $"window is {w.ClientSize.Height} tall");
            Assert.True(bottom <= w.ClientSize.Height, $"{buttonName} ends at {bottom}, window is {w.ClientSize.Height} tall");
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }

    [AvaloniaFact] public void Settings_CloseStaysVisible() => AssertButtonVisible(new SettingsWindow(), "CloseButton");
    [AvaloniaFact] public void About_CloseStaysVisible() => AssertButtonVisible(new AboutWindow(), "CloseButton");
    [AvaloniaFact] public void ThemeOnboarding_ContinueStaysVisible() =>
        AssertButtonVisible(new DialogEditor.Avalonia.Shared.ThemeOnboardingWindow(), "ContinueButton");
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TallWindowsFitTests"`
Expected: Settings and About fail. Before Step 3 adds the names, they fail with a null
reference on `FindControl`. Add the two `x:Name="CloseButton"` attributes first, then re-run:
they fail with "CloseButton ends at …, window is 736 tall", or pass if the stub `About` has
too little content. Theme Onboarding may already fail or pass.

If a test passes before Step 3, its window fits at 2.0× even without scrolling. Keep the test
as a regression guard, still make that window's change in Step 3 (real content is longer than
the parameterless window's), and ledger it.

- [ ] **Step 3: Restructure the three windows**

**Settings** (`SettingsWindow.axaml`):
- change the root `<Grid RowDefinitions="Auto,Auto,Auto" Margin="16">` to
  `<Grid RowDefinitions="*,Auto,Auto" Margin="16">`;
- wrap the `Grid.Row="0"` `StackPanel` in a `ScrollViewer`, moving `Grid.Row="0"` onto it:

```xml
        <!-- The body scrolls and the Close row stays below it (issue 177): at large font
             scales WindowSizing clamps the window to the screen height, and the * row then
             gives the body only what is left. With SizeToContent="Height" the * row behaves
             like Auto until the clamp applies. -->
        <ScrollViewer Grid.Row="0">
            <StackPanel Spacing="12">
                … existing content unchanged …
            </StackPanel>
        </ScrollViewer>
```

- add `x:Name="CloseButton"` to the Close `<Button Grid.Row="1" …>`.

**Theme Onboarding** (`ThemeOnboardingWindow.axaml`): the same change. The root grid becomes
`RowDefinitions="*,Auto,Auto"`, and the `Grid.Row="0"` `StackPanel` goes inside
`<ScrollViewer Grid.Row="0">` with the same comment.

**About** (`AboutWindow.axaml`): the root is a single `StackPanel` with Close and the hint bar
inside it. Replace it with:

```xml
    <!-- The body scrolls and Close stays below it (issue 177); see SettingsWindow.axaml. -->
    <Grid RowDefinitions="*,Auto,Auto" Margin="18,16,18,16">
        <ScrollViewer Grid.Row="0">
            <StackPanel Spacing="8">
                … every child of the old StackPanel up to and including the Status TextBlock …
            </StackPanel>
        </ScrollViewer>

        <Button Grid.Row="1" x:Name="CloseButton" HorizontalAlignment="Right" Margin="0,8,0,0"
                Content="{DynamicResource About_Close}"
                ToolTip.Tip="{DynamicResource About_Close}"
                AutomationProperties.HelpText="{DynamicResource About_Close}"
                Click="Close_Click"/>

        <!-- Focus hint bar (Gaps item 16) -->
        <shared:FocusHintBar Grid.Row="2" x:Name="HintBar"/>
    </Grid>
```

Check each code-behind (`*.axaml.cs`) for code that walks the old tree. For example,
`FindControl` by name still works, but a cast of `Content` to `StackPanel` would break.

- [ ] **Step 4: Run it, then the whole suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TallWindowsFitTests|FullyQualifiedName~ResizableDialogTests"`
Expected: PASS.

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/SettingsWindow.axaml DialogEditor.Avalonia/Views/AboutWindow.axaml DialogEditor.Avalonia.Shared/ThemeOnboardingWindow.axaml DialogEditor.Tests/Theming/TallWindowsFitTests.cs
git commit -m "fix: tall windows scroll so their action row stays on screen (#177)"
```

---

### Task 7: No clipped text in any window, at 1.0× and 2.0× on 1366×768

**Files:**
- Create: `DialogEditor.Tests/Theming/NoClippedTextTests.cs`
- Modify: whichever views the guard names. The planning survey expects these:
  - `LegendWindow.axaml`: descriptions run past the window at 1.0×;
  - `ConditionEditorWindow.axaml`: the And/Or combo's 70 px column shows "An";
  - `BatchReplaceWindow.axaml`: the "Replace" label's 70 px column;
  - `DiffWindow.axaml`: the Added/Changed/Removed key and `Diff_Hint`;
  - `AboutWindow.axaml`: the version row;
  - the Patch Manager view (`DialogEditor.Avalonia.Shared/**/PatchManagerView.axaml`):
    "Add mods", "Apply patches" and the empty-list hint.

**Interfaces:**
- Consumes: Tasks 1, 4, 5 and 6.

- [ ] **Step 1: Write the failing guard**

```csharp
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Guard for issue 177's done-when: on a 1366×768 screen, at 1.0× and 2.0×, no window clips
/// text. Each window type with a parameterless constructor is opened and every visible
/// TextBlock, including those inside control templates, is checked:
///  1. single-line text without an ellipsis must fit its own box (a fixed grid column or a
///     fixed-size control squeezes it, and the rest is cut off or spills over a neighbour);
///  2. the box must lie inside the nearest viewport that does not scroll in that direction:
///     the window itself, or a ScrollViewer with scrolling disabled on that axis.
/// Needs the Skia-backed test host (HeadlessTextMeasurementTests).
/// Main windows are excluded: their panes are covered by issues 82 and 85.
/// </summary>
public class NoClippedTextTests
{
    public NoClippedTextTests() => Loc.Configure(new StubStringProvider());

    // Known clipping that belongs to issue 178 (grid columns that overlap). Remove each entry
    // when 178 fixes it. Format: "<window type name>|<text>".
    private static readonly HashSet<string> TrackedByIssue178 = [];

    private static readonly Assembly[] Apps =
    [
        typeof(DialogEditor.Avalonia.App).Assembly,
        typeof(DialogEditor.Avalonia.Shared.ThemeOnboardingWindow).Assembly,
        typeof(DialogEditor.PatchManager.App).Assembly,
    ];

    public static IEnumerable<object[]> Cases() =>
        from t in Apps.SelectMany(a => a.GetTypes())
        where typeof(Window).IsAssignableFrom(t) && !t.IsAbstract
              && t.GetConstructor(Type.EmptyTypes) is not null && t.Name != "MainWindow"
        orderby t.FullName
        from scale in new[] { 1.0, 2.0 }
        select new object[] { t.AssemblyQualifiedName!, scale };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Window_ClipsNoText(string typeName, double scale)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => new Size(1366, 768);
        var w = (Window)Activator.CreateInstance(Type.GetType(typeName)!)!;
        try
        {
            new FontScaleApplier().Apply(scale);
            w.Show();
            var clipped = Clipped(w).Where(c => !TrackedByIssue178.Contains($"{w.GetType().Name}|{c.Text}")).ToList();
            Assert.True(clipped.Count == 0,
                $"{w.GetType().Name} at {scale}x clips:\n" + string.Join("\n", clipped.Select(c => $"  \"{c.Text}\": {c.Why}")));
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }

    private static IEnumerable<(string Text, string Why)> Clipped(Window w)
    {
        foreach (var tb in w.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!tb.IsEffectivelyVisible || tb.Bounds.Width <= 0) continue;
            var layout = tb.TextLayout;
            var text = tb.Text ?? string.Concat(tb.Inlines?.OfType<global::Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? []);
            if (string.IsNullOrWhiteSpace(text)) continue;

            // 1. Single-line, untrimmed text must fit its own box.
            if (tb.TextWrapping == TextWrapping.NoWrap && tb.TextTrimming == TextTrimming.None)
            {
                var needed = layout.WidthIncludingTrailingWhitespace + tb.Padding.Left + tb.Padding.Right;
                if (needed > tb.Bounds.Width + 0.5)
                    yield return (text, $"needs {needed:0} px, box is {tb.Bounds.Width:0}");
            }

            // 2. The box must lie inside the nearest non-scrolling viewport on each axis.
            var (viewport, clipX, clipY) = NearestViewport(tb, w);
            if (tb.TransformToVisual(viewport) is not { } m) continue;
            var r = new Rect(tb.Bounds.Size).TransformToAABB(m);
            if (clipX && (r.X < -0.5 || r.Right > viewport.Bounds.Width + 0.5))
                yield return (text, $"spans x {r.X:0}–{r.Right:0}, viewport is {viewport.Bounds.Width:0} wide");
            if (clipY && (r.Y < -0.5 || r.Bottom > viewport.Bounds.Height + 0.5))
                yield return (text, $"spans y {r.Y:0}–{r.Bottom:0}, viewport is {viewport.Bounds.Height:0} tall");
        }
    }

    // The nearest ScrollContentPresenter ancestor clips its content; on an axis its ScrollViewer
    // can scroll, overflow is reachable and therefore fine. Without one, the window is the viewport.
    private static (Visual Viewport, bool ClipX, bool ClipY) NearestViewport(Visual v, Window w)
    {
        foreach (var a in v.GetVisualAncestors())
            if (a is ScrollContentPresenter scp)
                return (scp, !scp.CanHorizontallyScroll, !scp.CanVerticallyScroll);
        return (w, true, true);
    }
}
```

If `TextLayout`, `CanHorizontallyScroll` or `TransformToAABB` don't compile as written, look up
the Avalonia 11.3 member names (context7, or the Avalonia source at `release/11.3.0`). Keep the
two checks as specified.

- [ ] **Step 2: Run it and read every failure**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NoClippedTextTests" > "$TEMP/clip.log" 2>&1; grep -A8 "clips:" "$TEMP/clip.log"`
Expected: FAIL. At minimum, Legend fails at 1.0×. Write the full list of offenders (window,
scale, text, reason) into the ledger before changing anything.

Before fixing, sort every offender into one of three buckets:
- **Window too small for its content at this scale:** this should no longer happen after
  Task 4. If it does, it's a sizing bug: debug `WindowSizing` and don't patch the view.
- **Grid columns that overlap (issue 178):** text in a fixed-width data-grid column header or
  cell, such as the Rep/Disposition "Count"/"Share" headers. Add it to `TrackedByIssue178`
  with a one-line comment and leave it for 178.
- **Layout clipping (this task):** fix it with one of the patterns in Step 3.

- [ ] **Step 3: Fix each layout offender with the matching pattern**

| What clips | Fix |
|---|---|
| Running text in a horizontal `StackPanel` (Legend's descriptions) | Replace `<StackPanel Orientation="Horizontal">` with `<DockPanel>`, put the swatch `Border` first with `DockPanel.Dock="Left"`, and give the `TextBlock` `TextWrapping="Wrap"`. |
| A label or control in a fixed grid column (`ColumnDefinitions="70,*"` in Batch Replace; `"70,Auto,*,28,28,28"` for the Condition Editor's And/Or combo) | Change the fixed column to `Auto` and move the old value to the element as `MinWidth="70"`. The Batch Replace `label` style already sets `MinWidth="70"`. |
| A single-line value that can be arbitrarily long (About's version string, paths) | `TextTrimming="CharacterEllipsis"` on the TextBlock, plus `ToolTip.Tip` bound to the same text. In a horizontal `StackPanel`, change the `StackPanel` to a `DockPanel` (label `DockPanel.Dock="Left"`) so the value gets a bounded width. |
| A hint or key that is prose (Diff's hint and Added/Changed/Removed key, Patch Manager's empty-list hint) | `TextWrapping="Wrap"`. If it sits in a horizontal `StackPanel`, use a `WrapPanel` for a row of items (the Diff key), or a `DockPanel` for a single text. |
| A button row that doesn't fit (the action rows in the small dialogs, Patch Manager's buttons) | Replace `<StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">` with `<WrapPanel HorizontalAlignment="Right" ItemSpacing="8" LineSpacing="8">`. If `WrapPanel` in 11.3 lacks `ItemSpacing`, give each button `Margin="8,0,0,8"` instead. |

Each changed element gets a short comment citing issue 177 and saying which pattern it uses
and why. For example:
`<!-- DockPanel, not a horizontal StackPanel (issue 177): gives the description a width to wrap in. -->`

An ellipsised value must keep its full text reachable. That means a tooltip, and UIA through
`AutomationProperties.Name` when the TextBlock is the control's name. Add any new tooltip
string to `Strings.axaml` and every `Strings.{lang}.axaml`, following the existing keys around
it.

After each fix, re-run the single failing case, for example
`--filter "FullyQualifiedName~NoClippedTextTests&DisplayName~LegendWindow"`.

- [ ] **Step 4: Run the guard, then the whole suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NoClippedTextTests"`
Expected: PASS.

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: all pass.

- [ ] **Step 5: Commit**

Commit once per window or group of related fixes, so a reviewer can follow them. For example:

```bash
git add DialogEditor.Tests/Theming/NoClippedTextTests.cs
git commit -m "test: guard against clipped text in every window at 1.0x and 2.0x (#177)"
git add DialogEditor.Avalonia/Views/LegendWindow.axaml
git commit -m "fix: Legend descriptions wrap instead of running off the window (#177)"
```

---

### Task 8: GUI verification and the PR

**Files:** none in the repo. Captures and sheets go in the scratchpad.

- [ ] **Step 1: Build, then capture every surface at 1.0× and 2.0× in all three themes**

```bash
dotnet build DialogEditor.slnx
pwsh -NoProfile -Command "& './tools/ui-automation/Capture-Windows.ps1' -OutDir \"$env:TEMP\cap177\""
```

Expected: `failures.txt` is empty, or lists only surfaces that also failed in the issue 86
run. Re-run any surface that failed for a tooling reason (for example, `vo-alias-picker` needs
`node-details` first: `-Only node-details,vo-alias-picker`).

- [ ] **Step 2: Look at every 2.0× capture**

Read each `*-2.00x-*.png`. Check that no button or label is cut off and that every action row
is visible. Anything that isn't is a defect: go back to Task 7's patterns, and add a
regression test that reproduces it if the guard missed it. Then capture again.

- [ ] **Step 3: Make the before-and-after sheets**

Use the issue 86 captures (scratchpad `cap86b`) as "before" and `cap177` as "after", at
`dark-2.00x`. Cover:
- `unsaved-changes`, `autosave-restore`, `settings`, `about`, `legend`, `batch-replace`,
  `compare-versions`, `condition-editor`, `patch-manager`.

- [ ] **Step 4: Run the suite one last time, then open the PR**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: all pass.

Push the branch and open a PR that closes 177. Its body covers:
- the mechanism and the guards;
- the rulings ledgered in this plan;
- the theme-switch fix;
- the note that the 768 px clamp is proven by the tests (`WindowSizingRuleTests`,
  `TallWindowsFitTests`, `NoClippedTextTests` at 1366×768), because the capture screen is
  larger;
- the `TrackedByIssue178` entries.

Attach the sheets with the `issue-screenshots` skill. Then call `get_status`, then
`set_monitor` with `auto_fix` and `address_comments` set to true.
