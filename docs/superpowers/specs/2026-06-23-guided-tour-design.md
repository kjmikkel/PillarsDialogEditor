# In-App Guided Tour — Design Spec

**Date:** 2026-06-23
**Scope:** Editor app only (`DialogEditor.Avalonia`). PatchManager has no canvas or node
editing; the tour doesn't apply there.

---

## Goal

Give new users an optional, dismissible, step-by-step orientation to the four major areas
of the main window, without blocking the UI or requiring any prior setup. Experienced users
can trigger it on demand via Help ▸ Start Guided Tour.

---

## Background

The onboarding track already ships:
- `ThemeOnboardingWindow` — shown before `MainWindow` on first run; picks a colour theme.
- Help ▸ Create Sample Project — seeds a safe practice conversation.
- `docs/walkthrough.md` (Help ▸ Open Walkthrough) — written beginner guide.

The guided tour is the interactive complement to the written walkthrough. It runs *after*
`MainWindow` is visible, pointing at live controls rather than describing them in prose.

---

## Approach: Tour Bar + Highlight Ring

A dismissible `GuidedTourBar` is docked between the canvas row and the status bar in
`MainWindow.axaml`. It shows the current step's description, a `Step N of M` counter, and
Back / Next·Finish / Dismiss (✕) buttons. The target control — a named panel in
`MainWindow.axaml` — receives a coloured ring drawn on the Avalonia `AdornerLayer`. The
real UI remains fully visible and interactive throughout.

Rejected alternatives:
- **Spotlight overlay** — requires a full-window transparent cutout panel; complex to
  implement and prevents interaction with the highlighted area.
- **Callout popups** — fiddly positioning (must stay within window, flip near edges);
  Avalonia `Popup` lifetime is awkward to manage alongside window resize.

---

## Components

### `GuidedTourViewModel` — `DialogEditor.ViewModels/ViewModels/GuidedTourViewModel.cs`

Owns the step sequence and all navigation state. No Avalonia references.

```csharp
public sealed partial class GuidedTourViewModel : ObservableObject
{
    // Step definitions are data; MainWindow.axaml.cs maps TargetName → actual Control.
    public IReadOnlyList<GuidedTourStep> Steps { get; }

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private int  _currentIndex;

    public bool IsLastStep => _currentIndex == Steps.Count - 1;
    public GuidedTourStep CurrentStep => Steps[_currentIndex];

    // Raised after CurrentStep changes so the view can swap the adorner target.
    public event Action? StepChanged;

    public void Start();       // resets to 0, sets GuidedTourSeen=true, IsVisible=true
    [RelayCommand] void Next();    // advance; on last step, behaves like Finish
    [RelayCommand] void Back();    // retreat; no-op at step 0
    [RelayCommand] void Dismiss(); // IsVisible=false, StepChanged fires with null target
}
```

`MainWindowViewModel` exposes it as:

```csharp
public GuidedTourViewModel Tour { get; } = new GuidedTourViewModel();
```

### `GuidedTourStep` — `DialogEditor.ViewModels/ViewModels/GuidedTourStep.cs`

```csharp
public sealed record GuidedTourStep(
    string TargetName,   // x:Name of the control to highlight in MainWindow
    string DescriptionKey // Strings.axaml resource key for the step text
);
```

### `GuidedTourBar` — `DialogEditor.Avalonia/Controls/GuidedTourBar.axaml`

A `UserControl` docked between Row 1 (content grid) and Row 2 (status bar) in
`MainWindow.axaml`. `IsVisible` bound to `{Binding Tour.IsVisible}`.

Layout (single row, `HorizontalAlignment="Stretch"`):

```
[ Step 2 of 4 ]  [ ← Back ]  [ description text … fills space ]  [ Next → ]  [ ✕ ]
```

- Step counter: `TextBlock`, `Loc.Format("Tour_Counter", index+1, total)`.
- Description: `TextBlock`, `TextWrapping="Wrap"`, `{Binding Tour.CurrentStep.DescriptionKey}` resolved via `Loc.Get(...)` in the ViewModel (or a converter).
- Back: disabled at step 0 (`CanExecute` on `BackCommand`).
- Next / Finish: label switches on `Tour.IsLastStep` — `Tour_Next` or `Tour_Finish`.
- Dismiss (✕): always enabled; calls `DismissCommand`.
- All buttons carry `ToolTip.Tip` and `AutomationProperties.HelpText` (mandatory per CLAUDE.md).
- A hidden `TextBlock` with `AutomationProperties.LiveSetting="Polite"` is bound to the
  current step description so screen readers announce each step change. Same pattern as
  `StatusLiveRegion` in `MainWindow.axaml`.

### `TourHighlightAdorner` — `DialogEditor.Avalonia/Controls/TourHighlightAdorner.cs`

```csharp
public sealed class TourHighlightAdorner : Adorner
{
    // Draws a 2px ring inset by 2px, using Brush.Border.Focus resolved from resources.
    public override void Render(DrawingContext context) { … }
}
```

Uses `Brush.Border.Focus` (existing token, visible in all four themes and High-Contrast).
Applied/removed via `AdornerLayer.GetAdornerLayer(target)?.Add/Remove(adorner)`.

### Wiring in `MainWindow.axaml.cs`

```csharp
// In constructor, after vm is set:
vm.Tour.StepChanged += OnTourStepChanged;

private TourHighlightAdorner? _tourAdorner;
private Control?              _tourTarget;

private void OnTourStepChanged()
{
    // Remove adorner from previous target.
    if (_tourTarget is not null && _tourAdorner is not null)
        AdornerLayer.GetAdornerLayer(_tourTarget)?.Remove(_tourAdorner);

    if (!vm.Tour.IsVisible) { _tourTarget = null; _tourAdorner = null; return; }

    var step    = vm.Tour.CurrentStep;
    var target  = this.FindControl<Control>(step.TargetName);
    if (target is null) return;

    // Expand collapsed panels before highlighting.
    EnsurePanelVisible(step.TargetName);

    _tourTarget  = target;
    _tourAdorner = new TourHighlightAdorner(target);
    AdornerLayer.GetAdornerLayer(target)?.Add(_tourAdorner);
}

private void EnsurePanelVisible(string targetName)
{
    // BrowserPanel: if collapsed, call the same expand logic as the toggle arrow.
    // DetailPanel:  same.
    // CanvasView / HelpToggle: always visible; no-op.
}
```

---

## Tour Steps

Four steps, in order:

| # | `TargetName` | `DescriptionKey` | English text |
|---|---|---|---|
| 1 | `BrowserPanel` | `Tour_Step1_Text` | "The conversation browser lists every conversation in your game folder. Click one to open it on the canvas." |
| 2 | `CanvasView` | `Tour_Step2_Text` | "The canvas shows the node graph for the open conversation. Click a node to select it; double-click empty space to add a new one." |
| 3 | `DetailPanel` | `Tour_Step3_Text` | "The detail panel shows the selected node's text, speaker, and conditions. Edit here to change what characters say." |
| 4 | `HelpToggle` | `Tour_Step4_Text` | "The ? button shows a hint for whatever you focus. Help ▸ Open Walkthrough has the full beginner guide." |

`BrowserPanel` and `DetailPanel` are collapsible. If either is collapsed when its step is
reached, `EnsurePanelVisible` expands it before adding the adorner, so the user can see
what is being highlighted.

---

## Triggering

### Auto-trigger (first run)

`MainWindow.OnOpened` already calls `vm.ReopenLastProjectOnStartup()`. Immediately after:

```csharp
if (!AppSettings.GuidedTourSeen)
    vm.Tour.Start();
```

This fires after the window is visible and the last project begins loading — the user
sees the real UI before the tour bar appears. The theme onboarding (`ThemeOnboardingWindow`)
runs before `MainWindow` is created, so it always precedes the tour.

### On-demand (Help menu)

New menu item **Help ▸ Start Guided Tour** (`Menu_StartGuidedTour`), placed between
"Open Walkthrough" and the separator before "Export UI Strings":

```xml
<MenuItem Header="{DynamicResource Menu_OpenWalkthrough}"  … />
<MenuItem Header="{DynamicResource Menu_StartGuidedTour}"
          Command="{Binding StartGuidedTourCommand}" />
<Separator/>
```

`StartGuidedTourCommand` on `MainWindowViewModel` calls `Tour.Start()` unconditionally —
always restarts from step 1, even if the tour has been seen before.

### `AppSettings.GuidedTourSeen`

```csharp
// SettingsData:
public bool GuidedTourSeen { get; set; } = true;  // true = upgrade default (no auto-trigger)

// AppSettings static property:
public static bool GuidedTourSeen
{
    get => Load().GuidedTourSeen;
    set { var s = Load(); s.GuidedTourSeen = value; Save(s); }
}
```

`Tour.Start()` writes `AppSettings.GuidedTourSeen = true` immediately, so the auto-trigger
never fires again even if the user dismisses at step 1.

---

## Strings (`Strings.axaml`)

```xml
<!-- ── Guided tour ───────────────────────────────────────────────────── -->
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

<sys:String x:Key="Tour_Step1_Text">The conversation browser lists every conversation in your game folder. Click one to open it on the canvas.</sys:String>
<sys:String x:Key="Tour_Step2_Text">The canvas shows the node graph for the open conversation. Click a node to select it; double-click empty space to add a new one.</sys:String>
<sys:String x:Key="Tour_Step3_Text">The detail panel shows the selected node's text, speaker, and conditions. Edit here to change what characters say.</sys:String>
<sys:String x:Key="Tour_Step4_Text">The ? button shows a hint for whatever you focus. Help ▸ Open Walkthrough has the full beginner guide.</sys:String>
```

---

## Files to Create / Modify

| File | Change |
|---|---|
| `DialogEditor.ViewModels/ViewModels/GuidedTourStep.cs` | **Create** — step record |
| `DialogEditor.ViewModels/ViewModels/GuidedTourViewModel.cs` | **Create** — navigation logic |
| `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | **Modify** — add `Tour` property + `StartGuidedTourCommand` |
| `DialogEditor.Avalonia/Controls/TourHighlightAdorner.cs` | **Create** — adorner ring |
| `DialogEditor.Avalonia/Controls/GuidedTourBar.axaml` | **Create** — tour bar view |
| `DialogEditor.Avalonia/Controls/GuidedTourBar.axaml.cs` | **Create** — code-behind |
| `DialogEditor.Avalonia/Views/MainWindow.axaml` | **Modify** — add `GuidedTourBar` row; add menu item |
| `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | **Modify** — wire `StepChanged`, adorner swap, auto-trigger |
| `DialogEditor.ViewModels/Services/AppSettings.cs` | **Modify** — add `GuidedTourSeen` |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | **Modify** — add tour strings |
| `DialogEditor.Tests/ViewModels/GuidedTourViewModelTests.cs` | **Create** — VM unit tests |
| `DialogEditor.Tests/Services/AppSettingsTests.cs` | **Modify** — `GuidedTourSeen` default |

---

## Testing Strategy

**`GuidedTourViewModelTests`:**
- `Start_SetsIsVisibleTrue_AndResetsToStep0`
- `Start_SetsGuidedTourSeenTrue`
- `Next_AdvancesCurrentIndex`
- `Next_AtLastStep_SetsIsVisibleFalse` (Finish behaviour)
- `Back_RetreatsCurrentIndex`
- `Back_AtStep0_IsNoOp`
- `Dismiss_SetsIsVisibleFalse`
- `IsLastStep_TrueOnlyAtFinalStep`
- `StepChanged_RaisedOnNextBackDismiss`

**`AppSettingsTests`:**
- `GuidedTourSeen_DefaultsTrueForUpgrades` (existing settings.json without the key)
- `GuidedTourSeen_DefaultsFalseForFreshInstall` (no settings.json → SettingsData default
  is `true`, but `GuidedTourSeen` property for fresh installs needs `false` — achieved by
  the same upgrade/new-install detection pattern used for `ThemeOnboardingSeen`)

No automated test for `TourHighlightAdorner` rendering (visual) or `EnsurePanelVisible`
(requires a running window). The adorner layer mechanism is already proven by Avalonia's
focus rings.

---

## Out of Scope

- PatchManager — no canvas, no node editing; tour doesn't apply.
- Animated transitions between steps — YAGNI.
- Tour progress persistence (resuming a half-finished tour) — always restarts from step 1.
- More than four steps — the walkthrough covers advanced topics; the tour is orientation only.
