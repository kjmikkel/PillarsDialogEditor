# In-App Guided Tour Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a four-step guided tour that highlights the main window's key panels with a coloured ring, driven by a dismissible bar, triggered automatically on first run and on demand via Help ▸ Start Guided Tour.

**Architecture:** `GuidedTourViewModel` owns step navigation (pure C#, no Avalonia). `GuidedTourBar` is a `UserControl` docked above the status bar and bound to the VM. `TourHighlightAdorner` draws a ring on Avalonia's `AdornerLayer` over the target named control; `MainWindow.axaml.cs` swaps the adorner on each `StepChanged` event.

**Tech Stack:** C# 12, Avalonia 11, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), Avalonia `AdornerLayer`.

## Global Constraints

- No hard-coded user-visible strings — all in `DialogEditor.Avalonia/Resources/Strings.axaml` using `{DynamicResource}`.
- Every interactive button needs `ToolTip.Tip` AND `AutomationProperties.HelpText` bound to the same resource. Icon-only buttons (Content contains no letters/digits) additionally need `AutomationProperties.Name`. The Dismiss button (Content="✕") is icon-only.
- `DialogEditor.ViewModels` has no Avalonia reference — `GuidedTourViewModel` must not import anything from `Avalonia.*`.
- Tests run serially (project-wide `AppSettings`/`Loc` global-state rule). `[assembly: CollectionBehavior(DisableTestParallelization = true)]` is already set; do not change it.
- TDD: write the failing test first, then the minimum implementation, then commit.
- Every caught exception must be logged via `AppLog.Error` or `AppLog.Warn`. `OperationCanceledException` may be swallowed silently. Bare `catch { }` is forbidden.

---

### Task 1: GuidedTourStep + GuidedTourViewModel + AppSettings.GuidedTourSeen

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/GuidedTourStep.cs`
- Create: `DialogEditor.ViewModels/ViewModels/GuidedTourViewModel.cs`
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs`
- Create: `DialogEditor.Tests/ViewModels/GuidedTourViewModelTests.cs`
- Modify: `DialogEditor.Tests/Services/AppSettingsTests.cs`

**Interfaces:**
- Produces: `GuidedTourStep(string TargetName, string DescriptionKey)` record; `GuidedTourViewModel` with properties `IsVisible`, `CurrentIndex`, `CurrentStep`, `IsLastStep`, `CounterText`, `CurrentStepText`, `NextButtonLabel`, `NextButtonTooltip`; methods `Start()`, `[RelayCommand] Back()`, `[RelayCommand] Next()`, `[RelayCommand] Dismiss()`; event `Action? StepChanged`; static `DefaultSteps`.
- Produces: `AppSettings.GuidedTourSeen` (bool, `false` for fresh install, `true` for upgrade).

- [ ] **Step 1: Write failing tests**

Create `DialogEditor.Tests/ViewModels/GuidedTourViewModelTests.cs`:

```csharp
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public sealed class GuidedTourViewModelTests
{
    // Two-step fixture used by most tests.
    private static GuidedTourViewModel TwoStep() => new(
    [
        new GuidedTourStep("BrowserPanel", "Tour_Step1_Text"),
        new GuidedTourStep("CanvasView",   "Tour_Step2_Text"),
    ]);

    [Fact]
    public void Start_SetsIsVisibleTrue()
    {
        var vm = TwoStep();
        vm.Start();
        Assert.True(vm.IsVisible);
    }

    [Fact]
    public void Start_ResetsToStep0()
    {
        var vm = TwoStep();
        vm.Start();
        vm.NextCommand.Execute(null);
        vm.Start();                     // restart
        Assert.Equal(0, vm.CurrentIndex);
    }

    [Fact]
    public void Start_WritesGuidedTourSeen()
    {
        AppSettings.GuidedTourSeen = false;
        TwoStep().Start();
        Assert.True(AppSettings.GuidedTourSeen);
    }

    [Fact]
    public void Start_RaisesStepChanged()
    {
        var vm    = TwoStep();
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.Start();
        Assert.True(fired);
    }

    [Fact]
    public void Next_AdvancesCurrentIndex()
    {
        var vm = TwoStep();
        vm.Start();
        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.CurrentIndex);
    }

    [Fact]
    public void Next_AtLastStep_Dismisses()
    {
        // Single-step tour: Next on the only step should end the tour.
        var vm = new GuidedTourViewModel([new GuidedTourStep("A", "K")]);
        vm.Start();
        vm.NextCommand.Execute(null);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Next_RaisesStepChanged()
    {
        var vm    = TwoStep();
        vm.Start();
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.NextCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void Back_RetreatsCurrentIndex()
    {
        var vm = TwoStep();
        vm.Start();
        vm.NextCommand.Execute(null);
        vm.BackCommand.Execute(null);
        Assert.Equal(0, vm.CurrentIndex);
    }

    [Fact]
    public void Back_AtStep0_IsNoOp()
    {
        var vm = TwoStep();
        vm.Start();
        vm.BackCommand.Execute(null);
        Assert.Equal(0, vm.CurrentIndex);
    }

    [Fact]
    public void Back_RaisesStepChanged()
    {
        var vm    = TwoStep();
        vm.Start();
        vm.NextCommand.Execute(null);
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.BackCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void Dismiss_SetsIsVisibleFalse()
    {
        var vm = TwoStep();
        vm.Start();
        vm.DismissCommand.Execute(null);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Dismiss_RaisesStepChanged()
    {
        var vm    = TwoStep();
        vm.Start();
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.DismissCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void IsLastStep_TrueOnlyAtFinalStep()
    {
        var vm = TwoStep();
        vm.Start();
        Assert.False(vm.IsLastStep);
        vm.NextCommand.Execute(null);
        Assert.True(vm.IsLastStep);
    }

    [Fact]
    public void CurrentStep_ReflectsCurrentIndex()
    {
        var vm = TwoStep();
        vm.Start();
        Assert.Equal("BrowserPanel", vm.CurrentStep.TargetName);
        vm.NextCommand.Execute(null);
        Assert.Equal("CanvasView", vm.CurrentStep.TargetName);
    }
}
```

- [ ] **Step 2: Run tests — verify they all fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~GuidedTourViewModelTests" -v quiet
```

Expected: all 14 tests fail with `CS0246` (types not found).

- [ ] **Step 3: Create GuidedTourStep**

Create `DialogEditor.ViewModels/ViewModels/GuidedTourStep.cs`:

```csharp
namespace DialogEditor.ViewModels;

public sealed record GuidedTourStep(
    string TargetName,
    string DescriptionKey);
```

- [ ] **Step 4: Create GuidedTourViewModel**

Create `DialogEditor.ViewModels/ViewModels/GuidedTourViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public sealed partial class GuidedTourViewModel : ObservableObject
{
    // The four steps that every running instance uses by default.
    // MainWindow.axaml.cs maps TargetName to an actual named Control.
    public static readonly IReadOnlyList<GuidedTourStep> DefaultSteps =
    [
        new("BrowserPanel", "Tour_Step1_Text"),
        new("CanvasView",   "Tour_Step2_Text"),
        new("DetailPanel",  "Tour_Step3_Text"),
        new("HelpToggle",   "Tour_Step4_Text"),
    ];

    private readonly IReadOnlyList<GuidedTourStep> _steps;

    // Parameterless constructor for production use (MainWindowViewModel).
    public GuidedTourViewModel() : this(DefaultSteps) { }

    // Overload for tests — lets tests inject a short step list.
    public GuidedTourViewModel(IReadOnlyList<GuidedTourStep> steps) => _steps = steps;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private int  _currentIndex;

    public GuidedTourStep CurrentStep => _steps[_currentIndex];
    public bool           IsLastStep  => _currentIndex == _steps.Count - 1;

    // Computed display strings — resolved lazily so Loc is not required at construction time.
    public string CounterText      => Loc.Format("Tour_Counter", _currentIndex + 1, _steps.Count);
    public string CurrentStepText  => Loc.Get(CurrentStep.DescriptionKey);
    public string NextButtonLabel   => IsLastStep ? Loc.Get("Tour_Finish")          : Loc.Get("Tour_Next");
    public string NextButtonTooltip => IsLastStep ? Loc.Get("ToolTip_Tour_Finish")  : Loc.Get("ToolTip_Tour_Next");

    // Raised after every step change (Next, Back, Dismiss, Start) so
    // MainWindow.axaml.cs can swap the adorner target.
    public event Action? StepChanged;

    /// <summary>
    /// Resets to step 0, marks the tour as seen in AppSettings so the
    /// auto-trigger does not fire again, and makes the bar visible.
    /// Safe to call multiple times — always restarts from step 0.
    /// </summary>
    public void Start()
    {
        AppSettings.GuidedTourSeen = true;
        CurrentIndex = 0;   // setter calls OnCurrentIndexChanged which notifies dependents
        IsVisible    = true;
        StepChanged?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back()
    {
        CurrentIndex--;
        StepChanged?.Invoke();
    }

    private bool CanBack() => _currentIndex > 0;

    [RelayCommand]
    private void Next()
    {
        if (IsLastStep) { Dismiss(); return; }
        CurrentIndex++;
        StepChanged?.Invoke();
    }

    [RelayCommand]
    private void Dismiss()
    {
        IsVisible = false;
        StepChanged?.Invoke();
    }

    // Called by the [ObservableProperty] source generator whenever CurrentIndex changes.
    partial void OnCurrentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(NextButtonLabel));
        OnPropertyChanged(nameof(NextButtonTooltip));
        BackCommand.NotifyCanExecuteChanged();
    }
}
```

- [ ] **Step 5: Add GuidedTourSeen to AppSettings**

Open `DialogEditor.ViewModels/Services/AppSettings.cs`.

In the `SettingsData` inner class, find the `ThemeOnboardingSeen` property and add `GuidedTourSeen` immediately after it, following the **exact same** upgrade/fresh-install pattern:

```csharp
// (after ThemeOnboardingSeen)
public bool GuidedTourSeen { get; set; } = true;
```

Then look at how `AppSettings.Load()` handles `ThemeOnboardingSeen = false` for fresh installs (no settings file on disk). Apply **identical** treatment to `GuidedTourSeen` — if `Load()` returns a modified `SettingsData` with `ThemeOnboardingSeen = false` for a fresh install, add the same override for `GuidedTourSeen`.

Then add the static property accessor after `ThemeOnboardingSeen`'s static property:

```csharp
public static bool GuidedTourSeen
{
    get => Load().GuidedTourSeen;
    set { var s = Load(); s.GuidedTourSeen = value; Save(s); }
}
```

- [ ] **Step 6: Add AppSettings test for GuidedTourSeen**

Open `DialogEditor.Tests/Services/AppSettingsTests.cs`. Find the test for `ThemeOnboardingSeen_DefaultsToFalseForFreshInstall` (or equivalent). Add the same pattern for `GuidedTourSeen`:

```csharp
[Fact]
public void GuidedTourSeen_DefaultsToFalseForFreshInstall()
{
    // Follow the exact same pattern as ThemeOnboardingSeen_DefaultsToFalseForFreshInstall.
    // Read that test first and mirror it here.
}
```

(Read the neighbouring test for the exact file-deletion / temp-path pattern used, then copy it replacing `ThemeOnboardingSeen` → `GuidedTourSeen`.)

- [ ] **Step 7: Run all tests — verify they pass**

```
dotnet test DialogEditor.Tests -v quiet
```

Expected: all existing tests still pass AND all 14 new `GuidedTourViewModelTests` pass. Note the total; it should be previous total + 14 + 1 (AppSettings).

- [ ] **Step 8: Commit**

```
git add DialogEditor.ViewModels/ViewModels/GuidedTourStep.cs
git add DialogEditor.ViewModels/ViewModels/GuidedTourViewModel.cs
git add DialogEditor.ViewModels/Services/AppSettings.cs
git add DialogEditor.Tests/ViewModels/GuidedTourViewModelTests.cs
git add DialogEditor.Tests/Services/AppSettingsTests.cs
git commit -m "feat(tour): add GuidedTourViewModel, GuidedTourStep, and AppSettings.GuidedTourSeen"
```

---

### Task 2: Strings + TourHighlightAdorner + GuidedTourBar

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Create: `DialogEditor.Avalonia/Controls/TourHighlightAdorner.cs`
- Create: `DialogEditor.Avalonia/Controls/GuidedTourBar.axaml`
- Create: `DialogEditor.Avalonia/Controls/GuidedTourBar.axaml.cs`

**Interfaces:**
- Consumes: `GuidedTourViewModel` from Task 1 (bound via `DataContext="{Binding Tour}"` set by Task 3).
- Produces: `TourHighlightAdorner` (a `Control` that draws a 2px ring over its bounds); `GuidedTourBar` user control.

There are no new unit tests for this task — `TourHighlightAdorner` is pure rendering, and `GuidedTourBar` is a dumb view. The existing `AutomationHelpTextTests` and `AutomationNameTests` (solution-wide scanners) will automatically validate that `GuidedTourBar`'s buttons carry the required `ToolTip.Tip`, `AutomationProperties.HelpText`, and `AutomationProperties.Name` attributes. Build must succeed with zero errors.

- [ ] **Step 1: Add tour strings to Strings.axaml**

Open `DialogEditor.Avalonia/Resources/Strings.axaml`. Append before the closing `</ResourceDictionary>` tag:

```xml
    <!-- ── Guided tour ────────────────────────────────────────────────── -->
    <sys:String x:Key="Menu_StartGuidedTour">Start Guided Tour</sys:String>

    <sys:String x:Key="Tour_Counter">Step {0} of {1}</sys:String>
    <sys:String x:Key="Tour_Back">← Back</sys:String>
    <sys:String x:Key="Tour_Next">Next →</sys:String>
    <sys:String x:Key="Tour_Finish">Finish</sys:String>
    <sys:String x:Key="Tour_Dismiss">Dismiss tour</sys:String>

    <sys:String x:Key="ToolTip_Tour_Back">Go to the previous step</sys:String>
    <sys:String x:Key="ToolTip_Tour_Next">Go to the next step</sys:String>
    <sys:String x:Key="ToolTip_Tour_Finish">Close the guided tour</sys:String>
    <sys:String x:Key="ToolTip_Tour_Dismiss">Close the guided tour without finishing</sys:String>
    <sys:String x:Key="AutomationName_Tour_Dismiss">Dismiss tour</sys:String>

    <sys:String x:Key="Tour_Step1_Text">The conversation browser lists every conversation in your game folder. Click one to open it on the canvas.</sys:String>
    <sys:String x:Key="Tour_Step2_Text">The canvas shows the node graph for the open conversation. Click a node to select it; double-click empty space to add a new one.</sys:String>
    <sys:String x:Key="Tour_Step3_Text">The detail panel shows the selected node's text, speaker, and conditions. Edit here to change what characters say.</sys:String>
    <sys:String x:Key="Tour_Step4_Text">The ? button shows a hint for whatever you focus. Help ▸ Open Walkthrough has the full beginner guide.</sys:String>
```

- [ ] **Step 2: Create TourHighlightAdorner**

Create `DialogEditor.Avalonia/Controls/TourHighlightAdorner.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DialogEditor.Avalonia.Controls;

/// <summary>
/// Draws a 2.5px coloured ring over the adorned control using Brush.Border.Focus
/// (visible in all four themes). Added to/removed from AdornerLayer by
/// MainWindow.axaml.cs as the guided tour advances.
/// </summary>
public sealed class TourHighlightAdorner : Control
{
    public override void Render(DrawingContext context)
    {
        var brush = this.TryFindResource("Brush.Border.Focus", out var r) && r is IBrush b
            ? b
            : new SolidColorBrush(Color.Parse("#5599FF")); // fallback if resource not found
        var pen  = new Pen(brush, 2.5);
        var rect = new Rect(1.25, 1.25, Bounds.Width - 2.5, Bounds.Height - 2.5);
        context.DrawRectangle(null, pen, rect, 3, 3);
    }
}
```

- [ ] **Step 3: Create GuidedTourBar.axaml**

Create `DialogEditor.Avalonia/Controls/GuidedTourBar.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="DialogEditor.Avalonia.Controls.GuidedTourBar"
             x:CompileBindings="False">

    <!-- Tour bar: docked between the canvas row and the status bar in MainWindow.
         DataContext is set to MainWindowViewModel.Tour (a GuidedTourViewModel)
         by MainWindow.axaml.
         The hidden live-region TextBlock announces step changes to screen readers. -->
    <Panel>

        <Border Background="{DynamicResource Brush.Surface.Panel}"
                BorderBrush="{DynamicResource Brush.Border.Default}"
                BorderThickness="0,1,0,0"
                Padding="8,6">
            <Grid ColumnDefinitions="Auto,Auto,*,Auto,Auto">

                <!-- Step counter: "Step 2 of 4" -->
                <TextBlock Grid.Column="0"
                           Text="{Binding CounterText}"
                           Foreground="{DynamicResource Brush.Text.Muted}"
                           FontSize="{DynamicResource FontSize.Label}"
                           VerticalAlignment="Center"
                           Margin="0,0,10,0"/>

                <!-- Back button — disabled at step 0 via BackCommand.CanExecute -->
                <Button Grid.Column="1"
                        Content="{DynamicResource Tour_Back}"
                        Command="{Binding BackCommand}"
                        ToolTip.Tip="{DynamicResource ToolTip_Tour_Back}"
                        AutomationProperties.HelpText="{DynamicResource ToolTip_Tour_Back}"
                        Margin="0,0,6,0"/>

                <!-- Step description — fills remaining space -->
                <TextBlock Grid.Column="2"
                           Text="{Binding CurrentStepText}"
                           Foreground="{DynamicResource Brush.Text.Primary}"
                           FontSize="{DynamicResource FontSize.Label}"
                           TextWrapping="Wrap"
                           VerticalAlignment="Center"
                           Margin="0,0,10,0"/>

                <!-- Next / Finish button — label and tooltip switch on IsLastStep -->
                <Button Grid.Column="3"
                        Content="{Binding NextButtonLabel}"
                        Command="{Binding NextCommand}"
                        ToolTip.Tip="{Binding NextButtonTooltip}"
                        AutomationProperties.HelpText="{Binding NextButtonTooltip}"
                        Margin="0,0,6,0"/>

                <!-- Dismiss button — icon-only (✕), needs AutomationProperties.Name -->
                <Button Grid.Column="4"
                        Content="✕"
                        Command="{Binding DismissCommand}"
                        ToolTip.Tip="{DynamicResource ToolTip_Tour_Dismiss}"
                        AutomationProperties.HelpText="{DynamicResource ToolTip_Tour_Dismiss}"
                        AutomationProperties.Name="{DynamicResource AutomationName_Tour_Dismiss}"/>

            </Grid>
        </Border>

        <!-- Hidden live region: announces each step description to screen readers.
             Same pattern as StatusLiveRegion in MainWindow.axaml. -->
        <TextBlock Text="{Binding CurrentStepText}"
                   AutomationProperties.LiveSetting="Polite"
                   IsVisible="False"/>

    </Panel>
</UserControl>
```

- [ ] **Step 4: Create GuidedTourBar.axaml.cs**

Create `DialogEditor.Avalonia/Controls/GuidedTourBar.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Controls;

public partial class GuidedTourBar : UserControl
{
    public GuidedTourBar() => InitializeComponent();
}
```

- [ ] **Step 5: Build — verify zero errors**

```
dotnet build DialogEditor.Avalonia -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Run all tests — verify still passing**

```
dotnet test DialogEditor.Tests -v quiet 2>&1 | tail -4
```

Expected: same count as after Task 1 (no regressions; `AutomationHelpTextTests` and `AutomationNameTests` now cover the new bar's buttons).

- [ ] **Step 7: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml
git add DialogEditor.Avalonia/Controls/TourHighlightAdorner.cs
git add DialogEditor.Avalonia/Controls/GuidedTourBar.axaml
git add DialogEditor.Avalonia/Controls/GuidedTourBar.axaml.cs
git commit -m "feat(tour): add TourHighlightAdorner, GuidedTourBar, and tour strings"
```

---

### Task 3: MainWindowViewModel command + MainWindow wiring + auto-trigger

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `GuidedTourViewModel` (Task 1), `GuidedTourBar` (Task 2), `TourHighlightAdorner` (Task 2), `AppSettings.GuidedTourSeen` (Task 1).
- Produces: fully working guided tour feature.

No new unit tests — the wiring is thin view glue and the adorner is visual. Build + manual verification is the gate.

- [ ] **Step 1: Add `Tour` property and `StartGuidedTourCommand` to MainWindowViewModel**

Open `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`.

After the existing `public NodeDetailViewModel Detail { get; } = new();` line, add:

```csharp
public GuidedTourViewModel Tour { get; } = new();
```

Then add the command (alongside other `[RelayCommand]` methods in the file):

```csharp
[RelayCommand]
private void StartGuidedTour() => Tour.Start();
```

- [ ] **Step 2: Update MainWindow.axaml — add tour row and Help menu item**

Open `DialogEditor.Avalonia/Views/MainWindow.axaml`.

**2a.** Change the root Grid's `RowDefinitions` from `"Auto,*,Auto"` to `"Auto,*,Auto,Auto"`.

**2b.** Find the status bar `Border` (currently `Grid.Row="2"`) and change it to `Grid.Row="3"`.

**2c.** Add the `GuidedTourBar` between the content grid and the status bar. Add the namespace import and the element:

At the top of the file, add the namespace (alongside existing `xmlns:views` etc.):
```xml
xmlns:controls="clr-namespace:DialogEditor.Avalonia.Controls;assembly=DialogEditor.Avalonia"
```

Then insert the bar before the status bar `Border`:

```xml
<controls:GuidedTourBar x:Name="TourBar"
                         Grid.Row="2"
                         IsVisible="{Binding Tour.IsVisible}"/>
```

The `DataContext` of `TourBar` is set in code-behind (Task 3 Step 3c) so the bar's
inner bindings are unambiguous. Do NOT set `DataContext` in XAML on this element.

**2d.** Find the Help menu (`<MenuItem Header="{DynamicResource Menu_Help}">`). Inside it, locate the `Menu_OpenWalkthrough` item. Add the new item immediately after it, before the `<Separator/>`:

```xml
<MenuItem Header="{DynamicResource Menu_StartGuidedTour}"
          Command="{Binding StartGuidedTourCommand}"
          ToolTip.Tip="{DynamicResource ToolTip_Tour_Next}"
          AutomationProperties.HelpText="{DynamicResource ToolTip_Tour_Next}"/>
```

Wait — the menu item needs its own tooltip describing what it does, not the "Next step" tooltip. Add this string to `Strings.axaml` now:

```xml
<sys:String x:Key="ToolTip_Menu_StartGuidedTour">Start the in-app guided tour, which highlights the main panels of the editor one at a time.</sys:String>
```

Then use it on the menu item:

```xml
<MenuItem Header="{DynamicResource Menu_StartGuidedTour}"
          Command="{Binding StartGuidedTourCommand}"
          ToolTip.Tip="{DynamicResource ToolTip_Menu_StartGuidedTour}"
          AutomationProperties.HelpText="{DynamicResource ToolTip_Menu_StartGuidedTour}"/>
```

- [ ] **Step 3: Wire StepChanged, adorner swap, and auto-trigger in MainWindow.axaml.cs**

Open `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`.

**3a.** Add using statements at the top if not already present:

```csharp
using Avalonia.Controls.Adorners;
using DialogEditor.Avalonia.Controls;
```

**3b.** Add two private fields at the class level (near other private fields):

```csharp
private TourHighlightAdorner? _tourAdorner;
private Control?              _tourTarget;
```

**3c.** In the constructor, after `DataContext = new MainWindowViewModel(...)`, add:

```csharp
var vm = (MainWindowViewModel)DataContext!;
TourBar.DataContext = vm.Tour;   // set before any bindings fire
vm.Tour.StepChanged += OnTourStepChanged;
```

**3d.** In `OnOpened`, after the `ReopenLastProjectOnStartup()` call, add the auto-trigger:

```csharp
protected override void OnOpened(EventArgs e)
{
    base.OnOpened(e);
    if (_startupDone) return;
    _startupDone = true;
    var vm = (MainWindowViewModel)DataContext!;
    vm.ReopenLastProjectOnStartup();
    if (!AppSettings.GuidedTourSeen)
        vm.Tour.Start();
}
```

(`AppSettings` is already imported in the file via `using DialogEditor.ViewModels.Services;`.)

**3e.** Add the three new private methods at the bottom of the class:

```csharp
private void OnTourStepChanged()
{
    RemoveTourAdorner();

    var vm = (MainWindowViewModel)DataContext!;
    if (!vm.Tour.IsVisible) return;

    var step   = vm.Tour.CurrentStep;
    var target = this.FindControl<Control>(step.TargetName);
    if (target is null) return;

    EnsureTourPanelVisible(step.TargetName);

    var layer = AdornerLayer.GetAdornerLayer(target);
    if (layer is null) return;

    _tourAdorner = new TourHighlightAdorner();
    AdornerLayer.SetAdornedElement(_tourAdorner, target);
    layer.Children.Add(_tourAdorner);
    _tourTarget = target;
}

private void RemoveTourAdorner()
{
    if (_tourTarget is null || _tourAdorner is null) return;
    AdornerLayer.GetAdornerLayer(_tourTarget)?.Children.Remove(_tourAdorner);
    _tourTarget  = null;
    _tourAdorner = null;
}

private void EnsureTourPanelVisible(string targetName)
{
    var vm = (MainWindowViewModel)DataContext!;
    // Read MainWindowViewModel to find the exact property names for
    // browser-expanded and detail-expanded state, then set them to true.
    // Typical pattern (verify against the actual property names in the file):
    if (targetName == "BrowserPanel" && !vm.IsBrowserExpanded)
        vm.IsBrowserExpanded = true;
    else if (targetName == "DetailPanel" && !vm.IsDetailExpanded)
        vm.IsDetailExpanded  = true;
    // CanvasView and HelpToggle are always visible — no action needed.
}
```

For `EnsureTourPanelVisible`: look at `MainWindowViewModel` to find the exact bool property names for browser and detail panel expanded state (they may be `IsBrowserExpanded`/`IsDetailExpanded` or similar). Set both to `true` if they exist and the panel is the target.

- [ ] **Step 4: Build — verify zero errors**

```
dotnet build DialogEditor.Avalonia -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Run all tests**

```
dotnet test DialogEditor.Tests -v quiet 2>&1 | tail -4
```

Expected: all tests pass (same count as after Task 2; no regressions).

- [ ] **Step 6: Manual verification**

Launch the app. Verify:

1. **First run:** Delete (or rename) `%LocalAppData%\PillarsDialogEditor\settings.json`, then launch. The tour bar should appear automatically after `MainWindow` opens. The `BrowserPanel` should be highlighted with a coloured ring. `Step 1 of 4` shown.
2. **Navigation:** Click Next → step counter advances to `Step 2 of 4`, ring moves to `CanvasView`. Click Back → returns to step 1 on `BrowserPanel`. Back button is disabled on step 1.
3. **Finish:** Click Next through all four steps. On step 4 (`HelpToggle`), the button reads "Finish". Clicking it hides the bar and removes the ring.
4. **Dismiss:** Start the tour via Help ▸ Start Guided Tour. Click ✕ — bar disappears, ring removed.
5. **Collapsed panels:** Collapse `BrowserPanel` (click its arrow toggle). Start tour via Help menu. Step 1 should expand the browser before highlighting it.
6. **Second run:** Close and relaunch. The tour should NOT auto-trigger (because `GuidedTourSeen` was set to `true` on first run).
7. **On demand:** Help ▸ Start Guided Tour launches the tour from step 1 regardless of history.

- [ ] **Step 7: Update Gaps.md**

Open `Gaps.md`. Find the "Onboarding" section and mark the in-app guided tour as implemented:

```markdown
**In-app guided tour ✓ implemented (2026-06-23):** four-step tour bar + adorner ring
highlighting BrowserPanel → CanvasView → DetailPanel → HelpToggle. Auto-triggers on
fresh install; re-accessible via Help ▸ Start Guided Tour.
```

- [ ] **Step 8: Commit**

```
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git add DialogEditor.Avalonia/Views/MainWindow.axaml
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git add DialogEditor.Avalonia/Resources/Strings.axaml
git add Gaps.md
git commit -m "feat(tour): wire guided tour into MainWindow — adorner ring, Help menu, auto-trigger"
```
