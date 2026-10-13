# Windows that follow the font scale and fit the screen — design

GitHub issue 177. A follow-up from the issue 86 look-and-feel review. It depends on issue 176,
which made every control's text follow the font scale.

## Goal

At any font scale from 1.0× to 2.0×, every window shows all of its buttons and labels without
clipping, and no window is larger than the screen's working area. A 1366×768 working area is
the smallest screen this design has to fit. Windows that are already open resize live when
the font scale changes.

## The problem

The font scale changes text size, but not layout units. A `Width="420"` stays 420 at any
scale, so window geometry and text size drift apart as soon as the text grows. At 2.0×:

- **Width doesn't grow with the text.** The small dialogs open at a fixed width. The earlier
  font-scale work set `MinWidth` to the old fixed `Width` and added `SizeToContent="Height"`,
  but nothing makes the window wider. Buttons in their single unwrapped row are cut off:
  "Save to Project" in Unsaved Changes, "Discard" in Autosave Restore.
- **Nothing keeps windows within the screen.** Settings (775 px) and About (755 px) grow taller
  than a 768 px screen through `SizeToContent="Height"`, which pushes Close off-screen. The main
  window's 1400×850 default is larger than that screen too. The app never reads the screen's
  working area.
- **Fixed sizes on interactive controls:**
  - Node Details' `ComboBox Height="22"`;
  - the conversation search box's `Height="26"`;
  - the annotation header's eight `Width="24" Height="24"` glyph buttons.
- **Text clipping that isn't caused by the scale:**
  - Legend's descriptions are cut off even at 1.0×;
  - the Condition Editor's And/Or combo shows "An";
  - About's version string and Patch Manager's "Add mods", "Apply patches" and empty-list
    hint run past the window edge;
  - Batch Replace's "Replace" label and Compare Versions' Added/Changed/Removed key and bottom
    hint are cut off.

## Approach

The approach is a hybrid. A single shared mechanism sizes every window from its XAML design size,
the font scale and the screen. That one rule fixes all the proportional clipping. Local layout
fixes cover what a size rule can't: tall bodies that must scroll, fixed control sizes, and text
that clips at any scale.

Fixing each of the ~45 windows by hand was rejected. It has no shared rule, and a new window
would miss it.

## 1. `WindowSizing` (DialogEditor.Avalonia.Shared/Theming)

The mechanism lives in the shared project, so the editor and the standalone Patch Manager both
use it.

### The pure rule

```csharp
public readonly record struct WindowSize(
    double Width, double Height,
    double MinWidth, double MinHeight,
    double MaxWidth, double MaxHeight);

public enum WindowSizingMode { Scale, ClampOnly }

public static WindowSize Compute(WindowSize design, double scale, Size? workingArea, WindowSizingMode mode);
```

1. **Scale.** In `Scale` mode, `Width`, `Height`, `MinWidth`, `MinHeight`, `MaxWidth` and
   `MaxHeight` are multiplied by `scale`. In `ClampOnly` mode they are left unchanged.
   - `NaN` (auto) stays `NaN`, so `SizeToContent` keeps working.
   - `0` minimums and `Infinity` maximums stay as they are.
2. **Clamp.** If `workingArea` is known, the limit is the working area minus a margin of 16 DIP
   from each edge. `Width`, `Height` and the minimums are clamped to the limit. Clamping the
   minimums means a 2.0× `MinWidth` can never force a window wider than the screen.
   - The maximums are capped at the limit only on an axis where the window uses
     `SizeToContent`, and never for a `ClampOnly` window. Such an axis has no `Width`/`Height`
     to cap, so the maximum is what keeps a tall dialog on screen.
   - *Corrected after the final review:* this spec first capped every `MaxWidth`/`MaxHeight`.
     A finite maximum becomes the OS's maximum track size, so that stopped a maximized window
     filling its screen and stopped any window growing on a larger second monitor.
3. **No screen.** If `workingArea` is `null` (no screen found, which happens in headless
   tests), the clamp step is skipped.

The rule is a pure function, so every case is a plain unit test.

### Which windows

- Every `Window` is sized by default (`WindowSizingMode.Scale`).
- The attached property `WindowSizing.Mode` opts a window into `ClampOnly`. Both apps' main
  windows set it: their panes already resize, and scaling 1400×850 by 2 would only be clamped
  away. Clamping stops the main window's default size from overflowing a 1366×768 screen.

### When it runs

- `WindowSizing.Register()` is called once from each app's `OnFrameworkInitializationCompleted`.
  It adds a class handler for `TemplatedControl.TemplateAppliedEvent` on `Window`.
- `Window.Show()` and `ShowDialog()` apply the template during styling, before the first layout
  pass and before startup positioning. So the window is sized before it appears, and
  `WindowStartupLocation="CenterOwner"` centres it at its new size.
  - This ordering is the design's one assumption about Avalonia internals.
  - The plan's first task verifies it in a headless test: when the handler has run, the window
    has not yet been positioned.
  - If the assumption turns out false, the fallback is the `Opened` event plus a re-centre on
    the owner.
- On its first run for a window, the handler records the XAML-authored values as that window's
  design size, in an attached property. It also records the scale it applied.

### Which screen

- The screen is the owner window's screen if the window has an owner, otherwise the primary
  screen.
- `Screen.WorkingArea` is in physical pixels. It is divided by `Screen.Scaling` to get DIPs.

### Live resize

- `FontScaleService` gains a `double Scale` property. `FontScaleApplier.Apply` sets it before
  bumping `Revision`.
- `Register()` subscribes to the service. When the scale changes, every open window is
  rescaled:
  - The current `Width`/`Height` are multiplied by `new / old`, where `old` is the scale the
    window was last sized at. This keeps your own resizing in proportion.
  - The minimums and maximums are recomputed from the design size.
  - The result is then clamped.
- `NaN` dimensions stay `NaN`, so `SizeToContent` windows re-measure on their own.
- In the editor, the open windows are the desktop lifetime's `Windows` collection.

## 2. Local layout fixes

### Tall windows scroll, and the action row stays put

Settings, About and Theme Onboarding grow past 768 px at 2.0×. In each one:
- the body moves into a `ScrollViewer`;
- the button row moves into a separate row of the window's root `Grid`, below the
  `ScrollViewer`.

When `MaxHeight` clamps the window, the body scrolls and Close stays visible. The window keeps
`SizeToContent="Height"`, so it is no taller than it needs to be until the clamp applies.

### Fixed control sizes become minimums

- Node Details' `ComboBox Height="22"` becomes `MinHeight="22"`.
- The conversation search box's `Height="26"` becomes `MinHeight="26"`, and any other
  `Height="26"` search box found in the plan's survey gets the same change.
- The annotation header's 24×24 glyph buttons get `MinWidth="24" MinHeight="24"`, and their
  fixed `Width`/`Height` are removed.

### Text that clips at any scale

- **Single-line values that can't wrap** (About's version string, Patch Manager's buttons):
  `TextTrimming="CharacterEllipsis"`, with the full text in the tooltip.
- **Running text** (Legend's descriptions, Patch Manager's empty-list hint, Compare Versions'
  bottom hint): `TextWrapping="Wrap"` inside a container whose width is constrained.
- **Legend:**
  - The plan diagnoses why the descriptions are cut off at 1.0×.
  - The finding so far is that each one is a `TextBlock` with `Run`s inside a horizontal layout,
    which measures it at unbounded width.
  - The fix gives it a constrained width so it wraps.
- **The And/Or combo:** its fixed width is removed, so it sizes to its content.
- **Batch Replace's "Replace" label** and **Compare Versions' key** are fixed in whatever way
  the plan's diagnosis shows is needed.

## 3. Tests

**`WindowSizingTests`** (pure), for `Compute`:
- scale 1.0 is the identity;
- 2.0 doubles every finite dimension;
- `NaN` stays `NaN`;
- `0` and `Infinity` are preserved;
- a 1366×768 working area clamps `Width`, `Height` and the minimums, and sets the maximums;
- `ClampOnly` doesn't scale but still clamps;
- with a `null` working area, scaling happens and clamping doesn't.

**Live rescale rule** (pure): from 1.0 to 2.0 doubles a user-resized width, then clamps it.

**`WindowSizingBehaviourTests`** (headless):
- At 2.0×, an opened dialog's `MinWidth` is twice its XAML value.
- A window with `WindowSizing.Mode="ClampOnly"` is not scaled.
- Calling `FontScaleApplier.Apply(2.0)` while a window is open doubles its `MinWidth`, and
  calling `Apply(1.0)` restores it.
- At the point the handler runs, the window has not yet been positioned (the ordering
  assumption above).

**`EveryWindowIsSizedTests`** (guard):
- Reflection finds every `Window` subclass with a parameterless constructor in
  `DialogEditor.Avalonia`, `DialogEditor.Avalonia.Shared` and `DialogEditor.PatchManager`.
- Each one is opened at 2.0×. The test asserts that its recorded applied scale is 2.0 and that
  its `MinWidth` (if it had one) doubled.
- A new window that somehow bypasses the mechanism fails this test.

**`ResizableDialogTests`** keeps its current assertions at scale 1.0. It still pins the
resizable-dialog contract.

**GUI check:**
- `tools/ui-automation/Capture-Windows.ps1` captures every surface at 1.0× and 2.0× in Dark,
  Light and High Contrast.
- The PR carries before and after contact sheets at 2.0×.
- The capture machine's screen is larger than 1366×768, so the 768 px clamp is proven by the
  unit tests rather than by a capture. The PR says so.

## Out of scope

- Grid columns that overlap at large scales (issue 178).
- Colour token problems (issues 179 and 180).
- The Patch Manager standalone app gaining a font-scale setting of its own. It runs at 1.0×, so
  it only gets the clamp.
