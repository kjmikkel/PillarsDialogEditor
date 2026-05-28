# Views / Converter Test Coverage — Design

**Goal:** Close the documented "Views / Converters (26 .cs files) — no unit tests" gap by adding pure unit tests for all 14 converters and headless integration tests for the two logic-bearing View code-behind files that can be tested without full ViewModel wiring.

**Architecture:** Add `Avalonia.Headless.XUnit` and a `DialogEditor.Avalonia` project reference to `DialogEditor.Tests`. Converter tests use standard `[Fact]` (no Avalonia init needed — all brushes are hardcoded `static readonly SolidColorBrush` fields). View tests use `[AvaloniaFact]` with a minimal headless `AppBuilder` entry point.

**Tech Stack:** xUnit, `Avalonia.Headless.XUnit 11.3.14`, `DialogEditor.Avalonia` converters and views, existing `DialogEditor.Tests` project.

---

## Infrastructure Changes

### `DialogEditor.Tests/DialogEditor.Tests.csproj`

Add:
```xml
<PackageReference Include="Avalonia.Headless.XUnit" Version="11.3.14" />
```
```xml
<ProjectReference Include="..\DialogEditor.Avalonia\DialogEditor.Avalonia.csproj" />
```

### `DialogEditor.Tests/AvaloniaTestApp.cs` (new file)

Carries the `[assembly: AvaloniaTestApplication]` attribute that `Avalonia.Headless.XUnit` requires. A minimal `AppBuilder` with no theme or fonts — enough for window/control instantiation.

```csharp
using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(DialogEditor.Tests.TestAppBuilder))]

namespace DialogEditor.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessOptions { UseHeadlessDrawing = true });
}
```

---

## Converter Tests (~70 tests, 5 files)

All converter tests use `[Fact]`. Brush comparisons use `((ISolidColorBrush)result).Color`.

### `DialogEditor.Tests/Converters/BoolConverterTests.cs`

Covers `BoolToOpacityConverter`, `InverseBoolConverter`, `CountToBoolConverter`.

| Converter | Cases |
|---|---|
| `BoolToOpacity` | `true` → `1.0`; `false` → `0.2`; non-bool value → `0.2` (fallback) |
| `InverseBool` | `Convert(true)` → `false`; `Convert(false)` → `true`; `ConvertBack(true)` → `false` |
| `CountToBool` | `0` → `false`; `1` → `true`; `-1` → `true`; non-int → `false` |

### `DialogEditor.Tests/Converters/StringConverterTests.cs`

Covers `NullOrEmptyToBoolConverter`, `StringIsEmptyConverter`, `QTDDisplayConverter`.

| Converter | Cases |
|---|---|
| `NullOrEmptyToBool` | `null` → `false`; `""` → `false`; `"x"` → `true` |
| `StringIsEmpty` | `null` → `true`; `""` → `true`; `"x"` → `false` |
| `QTDDisplay` | `Convert(null, "default")` → `"default"`; `Convert("", "default")` → `"default"`; `Convert("text", "default")` → `"text"`; `ConvertBack("default", "default")` → `""`; `ConvertBack("text", "default")` → `"text"` |

### `DialogEditor.Tests/Converters/NumericConverterTests.cs`

Covers `FloatDecimalConverter`.

Cases: `Convert(1.5f)` → `1.5m`; `Convert(null)` → `null`; `ConvertBack(1.5m)` → `1.5f`; `ConvertBack(null)` → `0f`.

### `DialogEditor.Tests/Converters/LayoutPointConverterTests.cs`

Covers `LayoutPointConverter`.

Cases:
- `Convert(LayoutPoint(10, 20))` → `Avalonia.Point(10, 20)`
- `ConvertBack(Avalonia.Point(10, 20))` → `LayoutPoint(10, 20)`
- `ConvertBack(AvaloniaProperty.UnsetValue)` → `LayoutPoint(0, 0)`
- `ConvertBack(null)` → `LayoutPoint(0, 0)`

### `DialogEditor.Tests/Converters/BrushConverterTests.cs`

Covers the six brush converters. Each test asserts the returned `ISolidColorBrush` has the expected `Color` value.

| Converter | Cases |
|---|---|
| `BoolToFemaleTextBrush` | `true` → one color; `false` → different color |
| `BoolToNewConversationBrush` | `true` → one color; `false` → different color |
| `FlowIssueKindToSeverityBrush` | one test per `FlowIssueKind` enum value |
| `PropertyValueStyleToBrush` | one test per `PropertyValueStyle` enum value |
| `SpeakerCategoryToBrush` | `Npc/Player/Narrator/Script` × `null/"body"/"footer"` parameter (12 cases) |
| `NodeColorConverter` | `[Npc, "Conversation"]` × `null/"body"/"footer"` (3 cases); `[*, "Bark"]` × `null/"body"/"footer"` (3 cases); `[Player, "Conversation"]` header (1 case) |

---

## View Tests (6 tests, 2 files)

View tests use `[AvaloniaFact]`.

### `DialogEditor.Tests/Views/LanguageCodeDialogTests.cs`

Tests `LanguageCodeDialog.AcceptAndClose()` input sanitization. Setup: create the dialog headlessly, retrieve the `_input` TextBox via `dialog.FindControl<TextBox>("_input")`, set `Text`, find the Accept button and raise a `RoutedEventArgs` click (or call the method directly if accessible), await `dialog.ShowDialog<string?>(rootWindow)`.

| Test | Input | Expected result |
|---|---|---|
| `AcceptAndClose_EmptyInput_ClosesWithNull` | `""` | `null` |
| `AcceptAndClose_WhitespaceOnly_ClosesWithNull` | `"   "` | `null` |
| `AcceptAndClose_TrimsText` | `"  hello  "` | `"hello"` |

### `DialogEditor.Tests/Views/LegendWindowTests.cs`

Tests `LegendWindow` cancel-close and position restore behavior.

| Test | What it does |
|---|---|
| `OnClosing_CancelsClose_WindowBecomesHidden` | Call `window.Close()`; assert `IsVisible == false` — window hides via `Hide()` rather than actually closing |
| `ShowAndRestore_SetsPositionFromSavedRect` | Call `ShowAndRestore(savedPixelRect)`; assert `window.Position` equals saved top-left pixel point |
| `ShowAndRestore_WhenNoSavedPosition_UsesDefault` | Call `ShowAndRestore` with no prior save; assert no crash and window is visible |

---

## Explicit Exclusions

The following five logic-bearing view files are excluded from this spec with rationale:

| File | Reason excluded |
|---|---|
| `MainWindow.axaml.cs` | Keyboard shortcuts and dialogs require a fully wired `MainWindowViewModel` with game-data services, file picker, and dispatcher — impractical for unit tests |
| `ConversationView.axaml.cs` | Double-tap coordinate conversion requires a live Nodify `NodifyEditor` instance with content loaded |
| `GameBrowserView.axaml.cs` | TreeView selection workaround requires a populated `TreeView` with real items |
| `ConditionEditorWindow.axaml.cs` | Branch group sub-dialog requires a live `ConditionEditorViewModel` |
| `NodeDetailView.axaml.cs` | Dialog-open logic requires a bound `NodeViewModel` and parent window for `ShowDialog` calls |

`Gaps.md` will be updated: "Views / Converters" gap narrowed to these five files only.

---

## What Is NOT in Scope

- The seven thin wrapper windows (`BatchReplaceWindow`, `FindReplaceWindow`, `FlowAnalyticsWindow`, `PatchManagerWindow`, `ScriptEditorWindow`, `SettingsWindow`, `TestModeOverlay`) — contain only `InitializeComponent()` and ViewModel assignment; no logic to test
- Production code changes — this is a pure test addition; no extraction of helpers required
