# Views / Converter Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unit tests for all 14 converters and headless tests for `LanguageCodeDialog` and `LegendWindow`, closing the "Views / Converters" gap documented in Gaps.md.

**Architecture:** Pure test additions. Converter tests use standard `[Fact]` — all brushes are `static readonly SolidColorBrush` requiring no Avalonia init. View tests use `[AvaloniaFact]` with a single `TestAppBuilder` entry point that configures the real `App` class headlessly so `avares://` resource loading works. No production code changes.

**Tech Stack:** xUnit 2.5.3, `Avalonia.Headless.XUnit 11.3.14`, `DialogEditor.Avalonia` converters and views, `DialogEditor.ViewModels.Services.AppSettings`.

---

## Codebase orientation

Before starting, note these exact types and values you will use in tests:

**Converter namespaces:**
- `DialogEditor.Avalonia.Converters` — all 14 converters
- `System.Globalization.CultureInfo.InvariantCulture` — pass to every `Convert`/`ConvertBack` call
- `Avalonia.Media.ISolidColorBrush`, `Avalonia.Media.Color` — brush comparison pattern: `((ISolidColorBrush)result!).Color`
- `Avalonia.AvaloniaProperty.UnsetValue` — sentinel returned/accepted by `LayoutPointConverter`
- `DialogEditor.Core.Models.LayoutPoint` — `readonly record struct LayoutPoint(double X, double Y)`
- `Avalonia.Point` — returned by `LayoutPointConverter.Convert`
- `DialogEditor.Core.Analytics.FlowIssueKind` — `Unreachable, PlayerDeadEnd, EmptyText, NoIncomingLinks, BarkTextTooLong, BarkHasPlayerChoiceChild`
- `DialogEditor.ViewModels.Models.PropertyValueStyle` — `Default, Condition, Script, Code`
- `DialogEditor.Core.Models.SpeakerCategory` — `Npc, Player, Narrator, Script`

**View details:**
- `LanguageCodeDialog(string? initialValue)` — constructor sets `LanguageCodeBox.Text`. Field `Result` is public `string?`. `AcceptAndClose()` is private, triggered by `OkButton` (AXAML name). Use: `dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))`.
- `LegendWindow.OnClosing(e)` — sets `e.Cancel = true`, calls `AppSettings.SetLegendPosition(...)`, `Hide()`, `OnHidden?.Invoke()`. Test by calling `legend.Close()` and asserting `legend.IsVisible == false`.
- `LegendWindow.ShowAndRestore(Window owner)` — reads `AppSettings.GetLegendPosition()`, sets `Position`, calls `Show(owner)`.
- `AppSettings.SettingsPathOverride` is `internal static AsyncLocal<string?>`. Set it inside each `[AvaloniaFact]` body (same UI-thread context) and clean up in `finally`.

---

## Files to create / modify

| File | Action |
|---|---|
| `DialogEditor.Tests/DialogEditor.Tests.csproj` | Modify — add `Avalonia.Headless.XUnit` package + `DialogEditor.Avalonia` project reference |
| `DialogEditor.Tests/AvaloniaTestApp.cs` | Create — headless AppBuilder entry point |
| `DialogEditor.Tests/Converters/BoolConverterTests.cs` | Create — 10 tests |
| `DialogEditor.Tests/Converters/StringConverterTests.cs` | Create — 13 tests |
| `DialogEditor.Tests/Converters/NumericConverterTests.cs` | Create — 4 tests |
| `DialogEditor.Tests/Converters/LayoutPointConverterTests.cs` | Create — 5 tests |
| `DialogEditor.Tests/Converters/BrushConverterTests.cs` | Create — 45 tests |
| `DialogEditor.Tests/Views/LanguageCodeDialogTests.cs` | Create — 3 tests |
| `DialogEditor.Tests/Views/LegendWindowTests.cs` | Create — 3 tests |
| `Gaps.md` | Modify — narrow the Views/Converters gap to the 5 excluded views |

---

## Task 1: Infrastructure — csproj + AppBuilder

**Files:**
- Modify: `DialogEditor.Tests/DialogEditor.Tests.csproj`
- Create: `DialogEditor.Tests/AvaloniaTestApp.cs`

- [ ] **Step 1: Add package and project reference to the test csproj**

In `DialogEditor.Tests/DialogEditor.Tests.csproj`, add inside the existing `<ItemGroup>` that has package references:

```xml
<PackageReference Include="Avalonia.Headless.XUnit" Version="11.3.14" />
```

And in the `<ItemGroup>` that has project references:

```xml
<ProjectReference Include="..\DialogEditor.Avalonia\DialogEditor.Avalonia.csproj" />
```

- [ ] **Step 2: Create the headless AppBuilder entry point**

Create `DialogEditor.Tests/AvaloniaTestApp.cs`:

```csharp
using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(DialogEditor.Tests.TestAppBuilder))]

namespace DialogEditor.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DialogEditor.Avalonia.App>()
            .UseHeadless(new AvaloniaHeadlessOptions { UseHeadlessDrawing = true });
}
```

Using the real `App` class ensures `App.Initialize()` calls `AvaloniaXamlLoader.Load(this)`, which merges `Strings.axaml` and all other resource dictionaries. The headless lifetime is not `IClassicDesktopStyleApplicationLifetime`, so `MainWindow` is never constructed.

- [ ] **Step 3: Build to verify the project compiles**

```
dotnet build DialogEditor.Tests
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s)` (or only pre-existing warnings — zero new errors).

- [ ] **Step 4: Commit**

```
git add DialogEditor.Tests/DialogEditor.Tests.csproj DialogEditor.Tests/AvaloniaTestApp.cs
git commit -m "test: add Avalonia.Headless.XUnit infrastructure for view tests"
```

---

## Task 2: Bool converter tests

**Files:**
- Create: `DialogEditor.Tests/Converters/BoolConverterTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using System.Globalization;
using DialogEditor.Avalonia.Converters;

namespace DialogEditor.Tests.Converters;

public class BoolConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── BoolToOpacityConverter ────────────────────────────────────────────

    [Fact]
    public void BoolToOpacity_True_ReturnsOne()
    {
        var result = new BoolToOpacityConverter().Convert(true, typeof(double), null, Inv);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void BoolToOpacity_False_ReturnsPointTwo()
    {
        var result = new BoolToOpacityConverter().Convert(false, typeof(double), null, Inv);
        Assert.Equal(0.2, result);
    }

    [Fact]
    public void BoolToOpacity_NullValue_ReturnsPointTwo()
    {
        var result = new BoolToOpacityConverter().Convert(null, typeof(double), null, Inv);
        Assert.Equal(0.2, result);
    }

    // ── InverseBoolConverter ──────────────────────────────────────────────

    [Fact]
    public void InverseBool_Convert_True_ReturnsFalse()
    {
        var result = new InverseBoolConverter().Convert(true, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    [Fact]
    public void InverseBool_Convert_False_ReturnsTrue()
    {
        var result = new InverseBoolConverter().Convert(false, typeof(bool), null, Inv);
        Assert.Equal(true, result);
    }

    [Fact]
    public void InverseBool_ConvertBack_True_ReturnsFalse()
    {
        var result = new InverseBoolConverter().ConvertBack(true, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    // ── CountToBoolConverter ──────────────────────────────────────────────

    [Fact]
    public void CountToBool_Zero_ReturnsFalse()
    {
        var result = new CountToBoolConverter().Convert(0, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    [Fact]
    public void CountToBool_Positive_ReturnsTrue()
    {
        var result = new CountToBoolConverter().Convert(3, typeof(bool), null, Inv);
        Assert.Equal(true, result);
    }

    [Fact]
    public void CountToBool_Negative_ReturnsFalse()
    {
        // Count should never be negative, but the converter treats any non-positive int as false
        var result = new CountToBoolConverter().Convert(-1, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    [Fact]
    public void CountToBool_NonInt_ReturnsFalse()
    {
        var result = new CountToBoolConverter().Convert("5", typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }
}
```

- [ ] **Step 2: Run the tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~BoolConverterTests" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 10`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/Converters/BoolConverterTests.cs
git commit -m "test: BoolToOpacity, InverseBool, CountToBool converters"
```

---

## Task 3: String converter tests

**Files:**
- Create: `DialogEditor.Tests/Converters/StringConverterTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using System.Globalization;
using DialogEditor.Avalonia.Converters;

namespace DialogEditor.Tests.Converters;

public class StringConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── NullOrEmptyToBoolConverter ────────────────────────────────────────

    [Fact]
    public void NullOrEmptyToBool_Null_ReturnsFalse()
        => Assert.Equal(false, new NullOrEmptyToBoolConverter().Convert(null, typeof(bool), null, Inv));

    [Fact]
    public void NullOrEmptyToBool_EmptyString_ReturnsFalse()
        => Assert.Equal(false, new NullOrEmptyToBoolConverter().Convert("", typeof(bool), null, Inv));

    [Fact]
    public void NullOrEmptyToBool_NonEmptyString_ReturnsTrue()
        => Assert.Equal(true, new NullOrEmptyToBoolConverter().Convert("hello", typeof(bool), null, Inv));

    [Fact]
    public void NullOrEmptyToBool_NonStringObject_ReturnsFalse()
        => Assert.Equal(false, new NullOrEmptyToBoolConverter().Convert(42, typeof(bool), null, Inv));

    // ── StringIsEmptyConverter ────────────────────────────────────────────

    [Fact]
    public void StringIsEmpty_Null_ReturnsTrue()
        => Assert.Equal(true, new StringIsEmptyConverter().Convert(null, typeof(bool), null, Inv));

    [Fact]
    public void StringIsEmpty_EmptyString_ReturnsTrue()
        => Assert.Equal(true, new StringIsEmptyConverter().Convert("", typeof(bool), null, Inv));

    [Fact]
    public void StringIsEmpty_NonEmptyString_ReturnsFalse()
        => Assert.Equal(false, new StringIsEmptyConverter().Convert("hello", typeof(bool), null, Inv));

    [Fact]
    public void StringIsEmpty_NonStringObject_ReturnsTrue()
    {
        // value is not string → treated as empty
        var result = new StringIsEmptyConverter().Convert(42, typeof(bool), null, Inv);
        Assert.Equal(true, result);
    }

    // ── QTDDisplayConverter ───────────────────────────────────────────────

    [Fact]
    public void QTDDisplay_Convert_EmptyString_ReturnsParameter()
    {
        var result = new QTDDisplayConverter().Convert("", typeof(string), "(default)", Inv);
        Assert.Equal("(default)", result);
    }

    [Fact]
    public void QTDDisplay_Convert_NonEmptyString_PassesThrough()
    {
        var result = new QTDDisplayConverter().Convert("ShowOnce", typeof(string), "(default)", Inv);
        Assert.Equal("ShowOnce", result);
    }

    [Fact]
    public void QTDDisplay_Convert_NullValue_PassesThrough()
    {
        // null is not an empty string — passes through unchanged
        var result = new QTDDisplayConverter().Convert(null, typeof(string), "(default)", Inv);
        Assert.Null(result);
    }

    [Fact]
    public void QTDDisplay_ConvertBack_ParameterValue_ReturnsEmpty()
    {
        var result = new QTDDisplayConverter().ConvertBack("(default)", typeof(string), "(default)", Inv);
        Assert.Equal("", result);
    }

    [Fact]
    public void QTDDisplay_ConvertBack_NonParameterValue_PassesThrough()
    {
        var result = new QTDDisplayConverter().ConvertBack("ShowOnce", typeof(string), "(default)", Inv);
        Assert.Equal("ShowOnce", result);
    }
}
```

- [ ] **Step 2: Run the tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~StringConverterTests" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 13`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/Converters/StringConverterTests.cs
git commit -m "test: NullOrEmptyToBool, StringIsEmpty, QTDDisplay converters"
```

---

## Task 4: Numeric and LayoutPoint converter tests

**Files:**
- Create: `DialogEditor.Tests/Converters/NumericConverterTests.cs`
- Create: `DialogEditor.Tests/Converters/LayoutPointConverterTests.cs`

- [ ] **Step 1: Create NumericConverterTests.cs**

```csharp
using System.Globalization;
using DialogEditor.Avalonia.Converters;

namespace DialogEditor.Tests.Converters;

public class NumericConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── FloatDecimalConverter ─────────────────────────────────────────────

    [Fact]
    public void FloatDecimal_Convert_Float_ReturnsDecimal()
    {
        var result = new FloatDecimalConverter().Convert(1.5f, typeof(decimal?), null, Inv);
        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void FloatDecimal_Convert_Null_ReturnsNull()
    {
        var result = new FloatDecimalConverter().Convert(null, typeof(decimal?), null, Inv);
        Assert.Null(result);
    }

    [Fact]
    public void FloatDecimal_ConvertBack_Decimal_ReturnsFloat()
    {
        var result = new FloatDecimalConverter().ConvertBack(1.5m, typeof(float), null, Inv);
        Assert.Equal(1.5f, result);
    }

    [Fact]
    public void FloatDecimal_ConvertBack_Null_ReturnsNull()
    {
        var result = new FloatDecimalConverter().ConvertBack(null, typeof(float), null, Inv);
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Create LayoutPointConverterTests.cs**

```csharp
using System.Globalization;
using Avalonia;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Converters;

public class LayoutPointConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Fact]
    public void Convert_LayoutPoint_ReturnsAvaloniaPoint()
    {
        var result = new LayoutPointConverter().Convert(new LayoutPoint(10, 20), typeof(Point), null, Inv);
        Assert.Equal(new Point(10, 20), result);
    }

    [Fact]
    public void Convert_NonLayoutPoint_ReturnsUnsetValue()
    {
        var result = new LayoutPointConverter().Convert("not a point", typeof(Point), null, Inv);
        Assert.Equal(AvaloniaProperty.UnsetValue, result);
    }

    [Fact]
    public void ConvertBack_AvaloniaPoint_ReturnsLayoutPoint()
    {
        var result = new LayoutPointConverter().ConvertBack(new Point(10, 20), typeof(LayoutPoint), null, Inv);
        Assert.Equal(new LayoutPoint(10, 20), result);
    }

    [Fact]
    public void ConvertBack_UnsetValue_ReturnsDefaultLayoutPoint()
    {
        var result = new LayoutPointConverter().ConvertBack(AvaloniaProperty.UnsetValue, typeof(LayoutPoint), null, Inv);
        Assert.Equal(default(LayoutPoint), result);
    }

    [Fact]
    public void ConvertBack_Null_ReturnsDefaultLayoutPoint()
    {
        var result = new LayoutPointConverter().ConvertBack(null, typeof(LayoutPoint), null, Inv);
        Assert.Equal(default(LayoutPoint), result);
    }
}
```

- [ ] **Step 3: Run the tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NumericConverterTests|FullyQualifiedName~LayoutPointConverterTests" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 9`

- [ ] **Step 4: Commit**

```
git add DialogEditor.Tests/Converters/NumericConverterTests.cs DialogEditor.Tests/Converters/LayoutPointConverterTests.cs
git commit -m "test: FloatDecimal and LayoutPoint converters"
```

---

## Task 5: Brush converter tests

**Files:**
- Create: `DialogEditor.Tests/Converters/BrushConverterTests.cs`

- [ ] **Step 1: Create BrushConverterTests.cs**

```csharp
using System.Globalization;
using Avalonia.Media;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Models;

namespace DialogEditor.Tests.Converters;

public class BrushConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static Color BrushColor(object? result)
        => ((ISolidColorBrush)result!).Color;

    // ── BoolToFemaleTextBrushConverter ────────────────────────────────────

    [Fact]
    public void BoolToFemaleTextBrush_True_ReturnsActiveBrush()
        => Assert.Equal(Color.FromRgb(0xe8, 0xe8, 0xe8),
            BrushColor(new BoolToFemaleTextBrushConverter().Convert(true, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void BoolToFemaleTextBrush_False_ReturnsDimBrush()
        => Assert.Equal(Color.FromRgb(0x55, 0x55, 0x55),
            BrushColor(new BoolToFemaleTextBrushConverter().Convert(false, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void BoolToFemaleTextBrush_Null_ReturnsDimBrush()
        => Assert.Equal(Color.FromRgb(0x55, 0x55, 0x55),
            BrushColor(new BoolToFemaleTextBrushConverter().Convert(null, typeof(ISolidColorBrush), null, Inv)));

    // ── BoolToNewConversationBrushConverter ───────────────────────────────

    [Fact]
    public void BoolToNewConversationBrush_True_ReturnsGreenBrush()
        => Assert.Equal(Color.FromRgb(0x7d, 0xce, 0xa0),
            BrushColor(new BoolToNewConversationBrushConverter().Convert(true, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void BoolToNewConversationBrush_False_ReturnsNormalBrush()
        => Assert.Equal(Color.FromRgb(0xcc, 0xcc, 0xcc),
            BrushColor(new BoolToNewConversationBrushConverter().Convert(false, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void BoolToNewConversationBrush_Null_ReturnsNormalBrush()
        => Assert.Equal(Color.FromRgb(0xcc, 0xcc, 0xcc),
            BrushColor(new BoolToNewConversationBrushConverter().Convert(null, typeof(ISolidColorBrush), null, Inv)));

    // ── FlowIssueKindToSeverityBrushConverter ─────────────────────────────

    [Theory]
    [InlineData(FlowIssueKind.Unreachable,              0xc0, 0x39, 0x2b)] // Red
    [InlineData(FlowIssueKind.PlayerDeadEnd,            0xb8, 0x76, 0x0a)] // Amber
    [InlineData(FlowIssueKind.EmptyText,                0xb8, 0x76, 0x0a)]
    [InlineData(FlowIssueKind.NoIncomingLinks,          0xb8, 0x76, 0x0a)]
    [InlineData(FlowIssueKind.BarkTextTooLong,          0xb8, 0x76, 0x0a)]
    [InlineData(FlowIssueKind.BarkHasPlayerChoiceChild, 0xb8, 0x76, 0x0a)]
    public void FlowIssueKindToSeverityBrush_ReturnsExpectedColor(FlowIssueKind kind, byte r, byte g, byte b)
        => Assert.Equal(Color.FromRgb(r, g, b),
            BrushColor(new FlowIssueKindToSeverityBrushConverter().Convert(kind, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void FlowIssueKindToSeverityBrush_NonKindValue_ReturnsAmber()
        => Assert.Equal(Color.FromRgb(0xb8, 0x76, 0x0a),
            BrushColor(new FlowIssueKindToSeverityBrushConverter().Convert("not-a-kind", typeof(ISolidColorBrush), null, Inv)));

    // ── PropertyValueStyleToBrushConverter ────────────────────────────────

    [Theory]
    [InlineData(PropertyValueStyle.Condition, 0xe8, 0xa0, 0x20)]
    [InlineData(PropertyValueStyle.Script,    0x7d, 0xce, 0xa0)]
    [InlineData(PropertyValueStyle.Code,      0x9c, 0xdc, 0xfe)]
    [InlineData(PropertyValueStyle.Default,   0xe8, 0xe8, 0xe8)]
    public void PropertyValueStyleToBrush_ReturnsExpectedColor(PropertyValueStyle style, byte r, byte g, byte b)
        => Assert.Equal(Color.FromRgb(r, g, b),
            BrushColor(new PropertyValueStyleToBrushConverter().Convert(style, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void PropertyValueStyleToBrush_NullValue_ReturnsNull()
    {
        var result = new PropertyValueStyleToBrushConverter().Convert(null, typeof(ISolidColorBrush), null, Inv);
        Assert.Null(result);
    }

    // ── SpeakerCategoryToBrushConverter ───────────────────────────────────

    [Theory]
    // Header (default zone — null or any unrecognised parameter)
    [InlineData(SpeakerCategory.Npc,      null,     0x7b, 0x24, 0x1c)]
    [InlineData(SpeakerCategory.Player,   null,     0x1a, 0x52, 0x76)]
    [InlineData(SpeakerCategory.Narrator, null,     0x0e, 0x66, 0x55)]
    [InlineData(SpeakerCategory.Script,   null,     0x2c, 0x3e, 0x50)]
    // Body
    [InlineData(SpeakerCategory.Npc,      "body",   0xF5, 0xF0, 0xD0)]
    [InlineData(SpeakerCategory.Player,   "body",   0xD5, 0xE8, 0xF5)]
    [InlineData(SpeakerCategory.Narrator, "body",   0xD5, 0xF0, 0xE8)]
    [InlineData(SpeakerCategory.Script,   "body",   0xE0, 0xE0, 0xE0)]
    // Footer
    [InlineData(SpeakerCategory.Npc,      "footer", 0xE8, 0xE0, 0xB0)]
    [InlineData(SpeakerCategory.Player,   "footer", 0xB0, 0xCD, 0xE8)]
    [InlineData(SpeakerCategory.Narrator, "footer", 0xB0, 0xE0, 0xD5)]
    [InlineData(SpeakerCategory.Script,   "footer", 0xC8, 0xC8, 0xC8)]
    public void SpeakerCategoryToBrush_ReturnsExpectedColor(SpeakerCategory cat, string? param, byte r, byte g, byte b)
        => Assert.Equal(Color.FromRgb(r, g, b),
            BrushColor(new SpeakerCategoryToBrushConverter().Convert(cat, typeof(ISolidColorBrush), param, Inv)));

    // ── NodeColorConverter ────────────────────────────────────────────────

    public static IEnumerable<object?[]> NodeColorData => new object?[][]
    {
        // Bark display type — SpeakerCategory is ignored by the converter
        [SpeakerCategory.Npc, "Bark",         null,     (byte)0x7A, (byte)0x5C, (byte)0x00], // BarkHeader
        [SpeakerCategory.Npc, "Bark",         "body",   (byte)0xFF, (byte)0xF8, (byte)0xDC], // BarkBody
        [SpeakerCategory.Npc, "Bark",         "footer", (byte)0xE8, (byte)0xD0, (byte)0x80], // BarkFooter
        // Npc
        [SpeakerCategory.Npc,      "Conversation", null,     (byte)0x7b, (byte)0x24, (byte)0x1c],
        [SpeakerCategory.Npc,      "Conversation", "body",   (byte)0xF5, (byte)0xF0, (byte)0xD0],
        [SpeakerCategory.Npc,      "Conversation", "footer", (byte)0xE8, (byte)0xE0, (byte)0xB0],
        // Player
        [SpeakerCategory.Player,   "Conversation", null,     (byte)0x1a, (byte)0x52, (byte)0x76],
        [SpeakerCategory.Player,   "Conversation", "body",   (byte)0xD5, (byte)0xE8, (byte)0xF5],
        [SpeakerCategory.Player,   "Conversation", "footer", (byte)0xB0, (byte)0xCD, (byte)0xE8],
        // Narrator
        [SpeakerCategory.Narrator, "Conversation", null,     (byte)0x0e, (byte)0x66, (byte)0x55],
        [SpeakerCategory.Narrator, "Conversation", "body",   (byte)0xD5, (byte)0xF0, (byte)0xE8],
        [SpeakerCategory.Narrator, "Conversation", "footer", (byte)0xB0, (byte)0xE0, (byte)0xD5],
        // Script
        [SpeakerCategory.Script,   "Conversation", null,     (byte)0x2c, (byte)0x3e, (byte)0x50],
        [SpeakerCategory.Script,   "Conversation", "body",   (byte)0xE0, (byte)0xE0, (byte)0xE0],
        [SpeakerCategory.Script,   "Conversation", "footer", (byte)0xC8, (byte)0xC8, (byte)0xC8],
    };

    [Theory, MemberData(nameof(NodeColorData))]
    public void NodeColorConverter_ReturnsExpectedColor(
        SpeakerCategory cat, string displayType, string? zone, byte r, byte g, byte b)
    {
        var conv = new NodeColorConverter();
        var result = conv.Convert(
            new object?[] { cat, displayType },
            typeof(ISolidColorBrush), zone, Inv);
        Assert.Equal(Color.FromRgb(r, g, b), BrushColor(result));
    }
}
```

- [ ] **Step 2: Run the tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~BrushConverterTests" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 45`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/Converters/BrushConverterTests.cs
git commit -m "test: all 6 brush converters — BoolToFemaleText, BoolToNewConversation, FlowIssueKind, PropertyValueStyle, SpeakerCategory, NodeColor"
```

---

## Task 6: LanguageCodeDialog headless tests

**Files:**
- Create: `DialogEditor.Tests/Views/LanguageCodeDialogTests.cs`

- [ ] **Step 1: Create LanguageCodeDialogTests.cs**

These tests use `[AvaloniaFact]` which runs on the Avalonia UI thread managed by the headless platform configured in `TestAppBuilder`. The dialog is shown headlessly (no screen needed) so that `Close()` in `AcceptAndClose()` succeeds. `Result` is a public property set before `Close()` is called, so it is readable after the click.

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Views;

namespace DialogEditor.Tests.Views;

public class LanguageCodeDialogTests
{
    [AvaloniaFact]
    public void AcceptAndClose_EmptyInput_SetsResultToNull()
    {
        var dialog = new LanguageCodeDialog("");
        dialog.Show();
        dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(dialog.Result);
    }

    [AvaloniaFact]
    public void AcceptAndClose_WhitespaceOnly_SetsResultToNull()
    {
        var dialog = new LanguageCodeDialog("   ");
        dialog.Show();
        dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(dialog.Result);
    }

    [AvaloniaFact]
    public void AcceptAndClose_TrimsInput_SetsResult()
    {
        var dialog = new LanguageCodeDialog("  en-US  ");
        dialog.Show();
        dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("en-US", dialog.Result);
    }
}
```

- [ ] **Step 2: Run the tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~LanguageCodeDialogTests" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 3`

If the tests fail with a resource-not-found error (missing `Localization_LanguageCodeDialog_Title`), the `AppBuilder.Configure<App>()` may need the `Avalonia.Themes.Fluent` style loaded explicitly. In that case update `TestAppBuilder`:
```csharp
AppBuilder.Configure<DialogEditor.Avalonia.App>()
    .UseHeadless(new AvaloniaHeadlessOptions { UseHeadlessDrawing = true });
```
should already handle this via `App.Initialize()`. If it still fails, check whether `AvaloniaXamlLoader.Load(this)` is being called (it is, in `App.Initialize()`).

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/Views/LanguageCodeDialogTests.cs
git commit -m "test: LanguageCodeDialog AcceptAndClose — empty, whitespace, and trimming"
```

---

## Task 7: LegendWindow headless tests

**Files:**
- Create: `DialogEditor.Tests/Views/LegendWindowTests.cs`

- [ ] **Step 1: Create LegendWindowTests.cs**

`LegendWindow.OnClosing` calls `AppSettings.SetLegendPosition` (writes to disk) and `Hide()`. Each test sets `AppSettings.SettingsPathOverride` to a temp file inside the `[AvaloniaFact]` body (same UI-thread async context) so `AppSettings` reads/writes go to a throwaway file. The temp file is cleaned up in `finally`.

`ShowAndRestore` also reads `AppSettings.GetLegendPosition()`, so the same override pattern applies.

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

public class LegendWindowTests
{
    [AvaloniaFact]
    public void OnClosing_HidesWindowAndInvokesCallback()
    {
        var tempPath = Path.GetTempFileName();
        AppSettings.SettingsPathOverride = tempPath;
        try
        {
            var owner = new Window();
            owner.Show();
            var legend = new LegendWindow();
            legend.Show(owner);
            Assert.True(legend.IsVisible);

            bool hiddenCallbackFired = false;
            legend.OnHidden = () => hiddenCallbackFired = true;

            legend.Close(); // triggers OnClosing → e.Cancel=true → Hide()
            Assert.False(legend.IsVisible);
            Assert.True(hiddenCallbackFired);
        }
        finally
        {
            AppSettings.SettingsPathOverride = null;
            File.Delete(tempPath);
        }
    }

    [AvaloniaFact]
    public void ShowAndRestore_RestoresPosition_WhenSaved()
    {
        var tempPath = Path.GetTempFileName();
        AppSettings.SettingsPathOverride = tempPath;
        try
        {
            AppSettings.SetLegendPosition(150.0, 250.0);
            var owner = new Window();
            owner.Show();
            var legend = new LegendWindow();
            legend.ShowAndRestore(owner);
            Assert.Equal(new PixelPoint(150, 250), legend.Position);
        }
        finally
        {
            AppSettings.SettingsPathOverride = null;
            File.Delete(tempPath);
        }
    }

    [AvaloniaFact]
    public void ShowAndRestore_NoSavedPosition_DoesNotThrow()
    {
        var tempPath = Path.GetTempFileName();
        AppSettings.SettingsPathOverride = tempPath;
        try
        {
            // No prior call to SetLegendPosition → GetLegendPosition returns null
            // ShowAndRestore should skip the Position assignment and not throw
            var owner = new Window();
            owner.Show();
            var legend = new LegendWindow();
            var ex = Record.Exception(() => legend.ShowAndRestore(owner));
            Assert.Null(ex);
        }
        finally
        {
            AppSettings.SettingsPathOverride = null;
            File.Delete(tempPath);
        }
    }
}
```

- [ ] **Step 2: Run the tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~LegendWindowTests" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/Views/LegendWindowTests.cs
git commit -m "test: LegendWindow — OnClosing hides with callback, ShowAndRestore position"
```

---

## Task 8: Update Gaps.md and run the full suite

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Run the full test suite to confirm no regressions**

```
dotnet test DialogEditor.Tests --no-build
```

Expected: All tests pass, zero failures. The count should be the prior total (≈666) plus the 83 new converter and view tests (≈749 total). If any test fails, fix the failure before committing.

- [ ] **Step 2: Update Gaps.md**

Replace the current "Views / Converters (26 .cs files) — no unit tests, relies on manual verification" line with:

```markdown
### Views Code-Behind — Partial Test Coverage

All 14 converter classes are covered by unit tests (no Avalonia initialization required — all brushes are hardcoded). `LanguageCodeDialog` and `LegendWindow` are covered by headless integration tests via `Avalonia.Headless.XUnit`.

The following five logic-bearing view files remain untested due to deep coupling with ViewModel services or Nodify's canvas internals:

- `MainWindow.axaml.cs` — keyboard shortcuts and dialogs require a fully wired `MainWindowViewModel` with game-data services
- `ConversationView.axaml.cs` — double-tap coordinate conversion requires a live Nodify `NodifyEditor` with content
- `GameBrowserView.axaml.cs` — TreeView selection workaround requires a populated `TreeView`
- `ConditionEditorWindow.axaml.cs` — branch group sub-dialog requires a live `ConditionEditorViewModel`
- `NodeDetailView.axaml.cs` — dialog-open logic requires a bound `NodeViewModel` and parent window
```

- [ ] **Step 3: Commit**

```
git add Gaps.md
git commit -m "docs: narrow Views/Converters gap — converters and two views now tested"
```
