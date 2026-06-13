# HelpText Mirroring & Focus Hint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Resumability:** Tasks and steps are checked off (`- [x]`) as they complete. If
> the session is interrupted, the next worker should scan for the first unchecked
> `- [ ]` and resume from there — no other context is required.

**Goal:** Close Gaps.md Accessibility item 5 — mirror every focusable control's
`ToolTip.Tip` into `AutomationProperties.HelpText` (Part A), and surface the
focused control's hint in MainWindow's status bar (Part B).

**Architecture:**
- Part A is a structural-contract test (`AutomationHelpTextTests`, modelled on the
  existing `AutomationNameTests`) that scans every `.axaml` file for focusable
  elements with a `ToolTip.Tip` and asserts a matching `AutomationProperties.HelpText`
  using the *same* resource key — then a mechanical sweep makes it pass.
- Part B adds `MainWindowViewModel.FocusHintText` + computed `DisplayStatusText`
  (falls back to `StatusText` when no hint), and a bubbling `GotFocus` handler on
  `MainWindow` that reads `AutomationProperties.GetHelpText` off the focused element.

**Tech Stack:** C#/.NET 8, Avalonia 11.3.14, CommunityToolkit.Mvvm, xUnit +
Avalonia.Headless.XUnit (`[AvaloniaFact]`).

**Design doc:** `docs/superpowers/specs/2026-06-13-helptext-and-focus-hint-design.md`

---

## Part A — Mirror ToolTip.Tip into AutomationProperties.HelpText

### Task 1: Write the enforcement test (RED)

**Files:**
- Create: `DialogEditor.Tests/Accessibility/AutomationHelpTextTests.cs`

- [x] **Step 1: Create the test file**

```csharp
using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Gaps.md "Accessibility — Assistive Technology &amp; Keyboard" item 5: tooltips are
/// hover-only, so keyboard and screen-reader users never see the explanatory text that
/// sighted mouse users get from ToolTip.Tip. Every *focusable* control that carries a
/// ToolTip.Tip must mirror the same resource into AutomationProperties.HelpText, which
/// screen readers announce on focus and which MainWindow's focus-hint status bar reads
/// directly (see MainWindowFocusHintTests).
///
/// Scoped to focusable element types only — a tooltip on a non-focusable wrapper
/// (TextBlock/Border used for static legend/info content) is never reachable by
/// keyboard focus, so mirroring it there would be dead weight (tracked separately as
/// Gaps.md item 12).
///
/// Mirrors AutomationNameTests' structural-contract approach (solution-wide scan
/// anchored on DialogEditor.slnx).
/// </summary>
public class AutomationHelpTextTests
{
    private static readonly HashSet<string> FocusableElementNames = new()
    {
        "Button", "ToggleButton", "CheckBox", "RadioButton", "TextBox", "ComboBox",
        "AutoCompleteBox", "NumericUpDown", "ListBox", "ListBoxItem", "MenuItem",
        "Slider", "Expander",
    };

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static bool IsExcluded(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}");

    [Fact]
    public void FocusableControlsWithTooltipsMirrorHelpText()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file)) continue;
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);

            foreach (var el in doc.Descendants().Where(e => FocusableElementNames.Contains(e.Name.LocalName)))
            {
                var tip = el.Attribute("ToolTip.Tip")?.Value;
                if (tip is null || !tip.StartsWith("{StaticResource ", StringComparison.Ordinal))
                    continue; // no tooltip, or a non-resource tooltip (none exist today)

                var help = el.Attribute("AutomationProperties.HelpText")?.Value;
                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;

                if (help is null)
                    offenders.Add($"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName}> has ToolTip.Tip={tip} but no AutomationProperties.HelpText");
                else if (help != tip)
                    offenders.Add($"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName}> AutomationProperties.HelpText={help} does not match ToolTip.Tip={tip}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Focusable controls with a ToolTip.Tip must mirror the same resource into "
            + "AutomationProperties.HelpText so keyboard and screen-reader users get the "
            + "same explanation sighted mouse users get from hover. Offenders:\n"
            + string.Join("\n", offenders));
    }
}
```

- [x] **Step 2: Run the test and capture the full offender list (RED)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AutomationHelpTextTests"`

Expected: FAIL. The assertion message lists every `<file>:<line>: <Element> has
ToolTip.Tip={StaticResource X} but no AutomationProperties.HelpText` — this list
*is* the work item for Task 2. Save the full failure output (e.g. redirect to
`/tmp/helptext-offenders.txt` or similar) so Task 2 doesn't need to re-run the test
to see it.

- [x] **Step 3: Commit the failing test**

```bash
git add DialogEditor.Tests/Accessibility/AutomationHelpTextTests.cs
git commit -m "test(a11y): require AutomationProperties.HelpText to mirror ToolTip.Tip on focusable controls"
```

---

### Task 2: Sweep — add AutomationProperties.HelpText to every offender (GREEN)

**Files:**
- Modify: every `.axaml` file listed in Task 1's offender output (expected ~20
  views, ~40-50 elements — `Button`, `ToggleButton`, `CheckBox`, `RadioButton`,
  `TextBox`, `ComboBox`, etc.)

- [x] **Step 1: Apply the mechanical transformation**

For **each** offender line of the form:

```
<File.axaml>:<Line>: <ElementType> has ToolTip.Tip={StaticResource X} but no AutomationProperties.HelpText
```

open `<File.axaml>` at `<Line>` and add a new attribute
`AutomationProperties.HelpText="{StaticResource X}"` immediately after the existing
`ToolTip.Tip="{StaticResource X}"` attribute on that element — same resource key
`X`, copied verbatim. Match the file's existing attribute-per-line indentation
style (look at how `AutomationProperties.Name` is placed after `ToolTip.Tip` in
files item 1 already swept, e.g. `DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml:57-58`,
for the expected style).

This is a pure copy of an existing attribute value to a new attribute name — no new
resource strings are created, no values change, only the attribute is added.

- [x] **Step 2: Run the test again (GREEN)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AutomationHelpTextTests"`

Expected: PASS (0 offenders). If any remain, repeat Step 1 for the remaining
offenders — the assertion message always lists exactly what's left.

- [x] **Step 3: Run the full suite to confirm no regressions**

Run: `dotnet test DialogEditor.Tests`

Expected: PASS, same total test count as before Task 1 plus the one new test.

- [x] **Step 4: Commit the sweep**

```bash
git add -A
git commit -m "feat(a11y): mirror ToolTip.Tip into AutomationProperties.HelpText on focusable controls"
```

---

## Part B — Focused-control hint in MainWindow's status bar

### Task 3: ViewModel — FocusHintText and DisplayStatusText (RED)

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`

- [x] **Step 1: Add the failing tests**

Add these tests to `MainWindowViewModelTests` (uses the existing `MakeVm()` helper
at line 39-40):

```csharp
[Fact]
public void DisplayStatusText_FallsBackToStatusText_WhenNoFocusHint()
{
    var vm = MakeVm();
    vm.StatusText = "Saved";

    Assert.Equal("Saved", vm.DisplayStatusText);
}

[Fact]
public void DisplayStatusText_PrefersFocusHintText_WhenSet()
{
    var vm = MakeVm();
    vm.StatusText = "Saved";
    vm.FocusHintText = "Opens the settings dialog";

    Assert.Equal("Opens the settings dialog", vm.DisplayStatusText);
}

[Fact]
public void DisplayStatusText_RevertsToStatusText_WhenFocusHintCleared()
{
    var vm = MakeVm();
    vm.StatusText = "Saved";
    vm.FocusHintText = "Opens the settings dialog";

    vm.FocusHintText = "";

    Assert.Equal("Saved", vm.DisplayStatusText);
}

[Fact]
public void DisplayStatusText_RaisesPropertyChanged_WhenEitherSourceChanges()
{
    var vm = MakeVm();
    var raised = new List<string?>();
    vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

    vm.FocusHintText = "Opens the settings dialog";
    Assert.Contains(nameof(MainWindowViewModel.DisplayStatusText), raised);

    raised.Clear();
    vm.StatusText = "Saved";
    Assert.Contains(nameof(MainWindowViewModel.DisplayStatusText), raised);
}
```

- [x] **Step 2: Run the tests to verify they fail (RED)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelTests"`

Expected: compile error — `FocusHintText` and `DisplayStatusText` do not exist on
`MainWindowViewModel` yet. A compile error counts as RED.

- [x] **Step 3: Commit the failing tests**

```bash
git add DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "test(a11y): MainWindowViewModel.DisplayStatusText falls back to StatusText without a focus hint"
```

---

### Task 4: Implement FocusHintText and DisplayStatusText (GREEN)

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`

- [x] **Step 1: Add the `FocusHintText` observable property**

In `MainWindowViewModel.cs:102`, alongside the existing `_statusText` declaration:

```csharp
    [ObservableProperty] private string              _statusText          = Loc.Get("Status_OpenFolder");
    [ObservableProperty] private string              _focusHintText       = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _availableLanguages = [];
```

- [x] **Step 2: Add the `DisplayStatusText` computed property**

Add next to `WindowTitle` (`MainWindowViewModel.cs:120-131`):

```csharp
    // ── Status bar shows the focused control's hint when present ──────────
    /// <summary>
    /// What the status bar TextBlock actually displays: the focused control's
    /// AutomationProperties.HelpText (set by MainWindow's GotFocus handler) when
    /// present, otherwise the last operation's StatusText.
    /// </summary>
    public string DisplayStatusText =>
        string.IsNullOrEmpty(FocusHintText) ? StatusText : FocusHintText;
```

- [x] **Step 3: Wire both sources to notify DisplayStatusText**

Add next to the other partial hooks (`MainWindowViewModel.cs:223-233`):

```csharp
    partial void OnStatusTextChanged(string value)
        => OnPropertyChanged(nameof(DisplayStatusText));

    partial void OnFocusHintTextChanged(string value)
        => OnPropertyChanged(nameof(DisplayStatusText));
```

- [x] **Step 4: Run the tests to verify they pass (GREEN)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelTests"`

Expected: PASS, including the 4 new tests from Task 3.

- [x] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat(a11y): add FocusHintText and DisplayStatusText to MainWindowViewModel"
```

---

### Task 5: MainWindow — GotFocus wiring (RED)

**Files:**
- Create: `DialogEditor.Tests/Views/MainWindowFocusHintTests.cs`

- [ ] **Step 1: Create the failing AvaloniaFact tests**

```csharp
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DialogEditor.Avalonia.Views;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// <summary>
/// Gaps.md a11y item 5 Part B: MainWindow mirrors the focused control's
/// AutomationProperties.HelpText (set by AutomationHelpTextTests' sweep) into
/// MainWindowViewModel.FocusHintText, which DisplayStatusText then surfaces in the
/// status bar — giving sighted keyboard users the same explanation screen readers get.
/// </summary>
public class MainWindowFocusHintTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowFocusHintTests()
    {
        Loc.Configure(new StubStringProvider());
        // Fresh settings file so MainWindow's startup ReopenLastProjectOnStartup
        // (triggered by window.Show() -> OnOpened) finds no last project and is a
        // no-op — see project_flaky_test_appsettings.
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwfh_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    [AvaloniaFact]
    public void GotFocus_OnControlWithHelpText_SetsFocusHintText()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();

        var button = window.FindControl<Button>("SettingsButton")!;
        var expectedHint = AutomationProperties.GetHelpText(button);
        Assert.False(string.IsNullOrEmpty(expectedHint)); // sanity: Part A's sweep covered this button

        button.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });

        Assert.Equal(expectedHint, vm.FocusHintText);
        Assert.Equal(expectedHint, vm.DisplayStatusText);
    }

    [AvaloniaFact]
    public void GotFocus_OnElementWithoutHelpText_ClearsFocusHint()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        vm.FocusHintText = "stale hint";

        window.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Unspecified,
        });

        Assert.Equal(string.Empty, vm.FocusHintText);
        Assert.Equal(vm.StatusText, vm.DisplayStatusText);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (RED)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowFocusHintTests"`

Expected: FAIL — `vm.FocusHintText` stays `string.Empty` in the first test because
nothing yet updates it on focus (no GotFocus handler wired).

- [ ] **Step 3: Commit the failing tests**

```bash
git add DialogEditor.Tests/Views/MainWindowFocusHintTests.cs
git commit -m "test(a11y): MainWindow mirrors focused control's HelpText into FocusHintText"
```

---

### Task 6: Implement the GotFocus handler and rebind the status bar (GREEN)

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml:307`

- [ ] **Step 1: Add the `Avalonia.Automation` using**

In `MainWindow.axaml.cs`, add to the using block (alongside the existing
`Avalonia`, `Avalonia.Controls`, `Avalonia.Input`, `Avalonia.Interactivity` lines
at the top of the file):

```csharp
using Avalonia.Automation;
```

- [ ] **Step 2: Subscribe to GotFocus in the constructor**

In `MainWindow.axaml.cs`, in the constructor (after `InitializeComponent();` and
the `vm.PropertyChanged += OnVmPropertyChanged;` line, around line 47), add:

```csharp
        this.AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble);
```

- [ ] **Step 3: Add the handler method**

Add a new private method to `MainWindow` (near the other small event handlers,
e.g. after `OnPointerPressed` around line 146-160):

```csharp
    /// <summary>
    /// Mirrors the focused control's AutomationProperties.HelpText (set by item 5's
    /// Part A sweep) into the view model so the status bar can show it — giving
    /// sighted keyboard users the same explanation screen readers announce on focus.
    /// </summary>
    private void OnAnyGotFocus(object? sender, GotFocusEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        vm.FocusHintText = e.Source is AvaloniaObject obj
            ? AutomationProperties.GetHelpText(obj) ?? string.Empty
            : string.Empty;
    }
```

- [ ] **Step 4: Rebind the status bar TextBlock**

In `MainWindow.axaml:307`, change:

```xml
                <TextBlock Grid.Column="0" Text="{Binding StatusText}"
```

to:

```xml
                <TextBlock Grid.Column="0" Text="{Binding DisplayStatusText}"
```

- [ ] **Step 5: Run the focus-hint tests (GREEN)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowFocusHintTests"`

Expected: PASS (both tests from Task 5).

- [ ] **Step 6: Run the full suite to confirm no regressions**

Run: `dotnet test DialogEditor.Tests`

Expected: PASS, total test count = baseline + 1 (Task 1) + 4 (Task 3) + 2 (Task 5).

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat(a11y): show the focused control's HelpText in MainWindow's status bar"
```

---

## Task 7: Update Gaps.md and final verification

**Files:**
- Modify: `Gaps.md` (item 5)

- [ ] **Step 1: Mark item 5 as implemented**

Replace the item 5 entry (currently):

```
5. **Tooltips are the sole explanation channel, and tooltips are hover-only.** Keyboard
   and screen-reader users never reach them. Mirror tooltip text into
   `AutomationProperties.HelpText` (can ride along with item 1's sweep), and consider
   showing the focused control's hint in the status bar.
```

with:

```
5. **Tooltips are the sole explanation channel. ✅ IMPLEMENTED (2026-06-13).** Every
   focusable control's `ToolTip.Tip` is mirrored into `AutomationProperties.HelpText`
   (enforced by `AutomationHelpTextTests`, solution-wide), and `MainWindow` mirrors the
   focused control's `HelpText` into the status bar via
   `MainWindowViewModel.DisplayStatusText` — sighted keyboard users now see the same
   explanation screen readers announce on focus. Design:
   `docs/superpowers/specs/2026-06-13-helptext-and-focus-hint-design.md`.
   **Deferred follow-ups:** info icons on non-focusable elements (item 12), and a hint
   surface for windows other than MainWindow (item 13).
```

- [ ] **Step 2: Run the full suite one last time**

Run: `dotnet test DialogEditor.Tests`

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark a11y item 5 (HelpText mirroring + focus hint) implemented"
```
