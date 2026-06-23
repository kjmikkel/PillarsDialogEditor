# Diff Window First-Run Intro Banner — Design Spec

**Date:** 2026-06-23
**Scope:** `DiffWindow` in `DialogEditor.Avalonia`. No changes to `DiffViewModel` or any
other window.

---

## Goal

Show a one-time, dismissible orientation banner the first time a writer opens the
compare/apply window. The audience is narrative writers who are not version-control
experts. The banner must answer "what am I looking at?" and immediately follow with the
safety reassurance "am I going to break something?" — in that order, in plain language,
in five seconds of reading.

---

## Background

The diff/selective-apply feature already ships with always-on in-context cues:

- A colour-key strip (green / orange / red squares with labels).
- A hint line in the apply bar: "Tick the changes you want to bring into your copy, then
  Bring in."
- Plain-language tooltips on every control.
- A `?` Help button that opens `DiffHelpWindow` (six sections, comprehensive).

These cover the need once a writer understands the window's purpose. The intro banner
covers the gap *before* that understanding exists — the moment a writer opens the window
for the first time and has no mental model.

The feature was deferred from the selective-apply design (`2026-05-30-selective-apply-design.md`)
because it required persisted "seen" state; that infrastructure now exists via the
`GuidedTourSeen` / `ThemeOnboardingSeen` pattern in `AppSettings`.

---

## Approach: Dismissible Banner Inside DiffWindow

A single-row `Border` inserted between the endpoint pickers and the main content area
(conversation list + canvas). The writer can see the list and canvas *behind* it while
reading — "the list on the left" refers to something that is literally on their screen.

Rejected alternatives:

- **Separate dialog before DiffWindow opens** — the things being described are not
  visible while reading, making orientation harder.
- **Overlay panel over main content** — blocks the UI the writer is trying to understand.

---

## Banner Content

```
This window shows what's different between two versions of your dialogue.
Use the list on the left to see changed conversations — tick what you want
to keep, then press Bring in. Nothing in your file changes until you press
that button.
```

Three sentences: orientation → workflow → safety. Five seconds to read.

No "Read more" link — the `?` Help button is already present on the canvas header for
writers who want the full explanation.

---

## Trigger and Lifecycle

`AppSettings.DiffWindowSeen` flag:

- `SettingsData.DiffWindowSeen` defaults to `true` (upgrade default) — existing users
  who upgrade never see the banner.
- `AppSettings.Load()` returns `false` for a fresh install (no settings file) or a load
  failure, matching the identical pattern used by `ThemeOnboardingSeen` and
  `GuidedTourSeen`.
- The banner is shown in `DiffWindow`'s constructor: if `!AppSettings.DiffWindowSeen`,
  make the banner visible and immediately set `AppSettings.DiffWindowSeen = true`.
  Marking it seen on open (not on dismiss) means closing the window without clicking
  "Got it" does not re-show the banner — one show, ever.

---

## Layout

`DiffWindow.axaml` root grid currently has `RowDefinitions="Auto,*,Auto,Auto"` (endpoint
row, main content, dangling-link panel, apply bar / status). A new `Auto` row is inserted
after the endpoint row:

```
Row 0: Endpoint pickers          (Auto)   — unchanged
Row 1: Intro banner              (Auto)   — NEW; collapses to zero when hidden
Row 2: Main content (list+canvas) (*)     — was Row 1
Row 3: Dangling-link panel       (Auto)   — was Row 2
Row 4: Apply bar + status        (Auto)   — was Row 3
```

The banner is a `Border` styled to match `FocusHintBar`:

- `Background="{DynamicResource Brush.Surface.Card}"`
- `BorderBrush="{DynamicResource Brush.Border.Default}"`, `BorderThickness="0,0,0,1"`
- `Padding="12,8"`

Inside, a single-row `Grid ColumnDefinitions="*,Auto"`:

- Col 0: `TextBlock` bound to `{DynamicResource DiffIntro_Text}`, `TextWrapping="Wrap"`,
  `Foreground="{DynamicResource Brush.Text.Primary}"`,
  `FontSize="{DynamicResource FontSize.Label}"`.
- Col 1: `Button` `Content="{DynamicResource DiffIntro_GotIt}"`, with
  `ToolTip.Tip="{DynamicResource ToolTip_DiffIntro_GotIt}"` and matching
  `AutomationProperties.HelpText`.

---

## Code-Behind

`DiffWindow.axaml.cs` constructor, after `InitializeComponent()`:

```csharp
if (!AppSettings.DiffWindowSeen)
{
    AppSettings.DiffWindowSeen = true;
    IntroBanner.IsVisible = true;
}
```

`IntroBanner` is the `x:Name` of the banner `Border`. Default `IsVisible="False"` in
XAML so the banner is invisible for all existing users with no flicker.

The "Got it" button's click handler:

```csharp
private void IntroBanner_GotIt_Click(object? sender, RoutedEventArgs e)
    => IntroBanner.IsVisible = false;
```

No ViewModel changes. No new class files. The logic is entirely view-layer lifecycle
state, consistent with the `_startupDone` pattern in `MainWindow.axaml.cs`.

---

## Strings (`Strings.axaml`)

```xml
<sys:String x:Key="DiffIntro_Text">This window shows what's different between two
versions of your dialogue. Use the list on the left to see changed conversations —
tick what you want to keep, then press Bring in. Nothing in your file changes until
you press that button.</sys:String>

<sys:String x:Key="DiffIntro_GotIt">Got it</sys:String>

<sys:String x:Key="ToolTip_DiffIntro_GotIt">Dismiss this introduction. You can re-read it any time via the ? Help button.</sys:String>
```

---

## Files to Create / Modify

| File | Change |
|---|---|
| `DialogEditor.ViewModels/Services/AppSettings.cs` | Add `DiffWindowSeen` flag (same pattern as `GuidedTourSeen`) |
| `DialogEditor.Avalonia/Views/DiffWindow.axaml` | Add `Row 1` banner; bump existing row indices |
| `DialogEditor.Avalonia/Views/DiffWindow.axaml.cs` | Show/hide logic in constructor; "Got it" click handler |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | Add `DiffIntro_Text`, `DiffIntro_GotIt`, `ToolTip_DiffIntro_GotIt` |
| `DialogEditor.Tests/Services/AppSettingsTests.cs` | `DiffWindowSeen_DefaultsFalseForFreshInstall` |

---

## Testing

**`AppSettingsTests`:**
- `DiffWindowSeen_DefaultsFalseForFreshInstall` — no settings file → `false`
- `DiffWindowSeen_DefaultsTrueForUpgrade` — existing settings.json without the key → `true`

No automated test for the banner's show/hide (view-layer lifecycle). Manual verification:

1. Delete `settings.json`, launch app, open Versions ▸ Compare Versions — banner appears.
2. Click "Got it" — banner collapses.
3. Close and reopen the window — banner does not appear.
4. Relaunch the app, reopen the window — banner does not appear.
5. Launch with an existing `settings.json` (upgrade simulation) — banner never appears.

---

## Out of Scope

- Re-show trigger (e.g., "show again after N months") — YAGNI.
- A "Read more" link on the banner — the `?` Help button already serves this.
- Changes to `DiffViewModel` or any other window.
