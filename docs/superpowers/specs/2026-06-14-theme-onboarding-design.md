# First-Run Theme Onboarding — design

**Status:** Approved 2026-06-14, not yet implemented.
**Source:** Gaps.md, Accessibility audit item 15 — "No first-run theme-picker
onboarding." Split off from item 11(a) (`2026-06-13-os-theme-detection-design.md` §5)
during its brainstorm.
**Builds on:** Layer 2 runtime theme switching (`2026-06-11-layer2-runtime-theme-design.md`)
and the `"Auto"` OS-detection resolver (`2026-06-13-os-theme-detection-design.md`), both
implemented. Reuses `ThemePickerView`/`ThemePickerViewModel`/`IThemeApplier` as-is — no
changes to any of those.

## 1. Goal

On a user's very first launch of either app (the editor or the standalone PatchManager),
show a small modal dialog that lets them pick a colour theme before the main window
appears, with a live preview of what each theme looks like. Users who never open this
dialog (existing installs, or anyone who already has a `settings.json`) are unaffected —
`"Auto"` already gives new installs a reasonable OS-matched default
(item 11(a), implemented), so this dialog is a *discoverability* aid, not a requirement.

## 2. Scope decisions

- **Palettes offered:** all five entries from `IThemeApplier.Available` — System Default
  (`"Auto"`), Dark, Light, Colourblind, High Contrast. Same list as the Settings picker;
  no curation needed since `Available` already drives both.
- **Preview mechanism:** **live self-retint**. The dialog hosts the existing
  `ThemePickerView` unmodified — selecting an entry calls `IThemeApplier.Apply(id)`
  exactly as the Settings picker does today, which swaps the merged palette + reloaded
  `Tokens.axaml` and flips `RequestedThemeVariant` on `Application.Current`. Every
  `{DynamicResource Brush.*}` binding in the open dialog (including its own preview
  panel) re-resolves automatically — no new rendering/preview machinery.
- **Modality:** modal, shown **before** `MainWindow` is constructed. The user picks a
  theme (or accepts the pre-selected default — `"Auto"` for fresh installs) and presses
  **Continue**; `MainWindow` then opens as normal. There is no separate "Skip" — leaving
  the pre-selected `"Auto"` and pressing Continue *is* the skip path.
- **Apps:** both `DialogEditor.Avalonia` and `DialogEditor.PatchManager` gain the same
  startup check, using the **shared** `AppSettings.ThemeOnboardingSeen` flag (one
  `settings.json`, as today). In practice this means whichever of the two apps the user
  launches first shows the dialog once; the other app sees the flag already set and skips
  it. This avoids double-prompting a user who uses both tools, while still covering a
  user who only ever runs PatchManager standalone.

## 3. Components

### 3.1 `AppSettings` (`DialogEditor.ViewModels/Services/AppSettings.cs`)

New persisted flag:

```csharp
// Whether the first-run theme-onboarding dialog has been shown. Defaults to true so that
// EXISTING installs upgrading to this version (whose settings.json predates this field)
// silently treat onboarding as already-seen — only a genuinely fresh install (no
// settings.json yet) gets false, via Load().
public bool ThemeOnboardingSeen { get; set; } = true;
```

`Load()` is adjusted so the *no file yet* and *load failed* paths construct
`new() { ThemeOnboardingSeen = false }` instead of bare `new()`; the normal
deserialize-existing-file path is unchanged (an existing file without the key fills in
the C# default, `true`).

New accessor, following the existing `Theme`/`FontScale` pattern:

```csharp
public static bool ThemeOnboardingSeen
{
    get => Load().ThemeOnboardingSeen;
    set { var s = Load(); s.ThemeOnboardingSeen = value; Save(s); }
}
```

### 3.2 `ThemeOnboardingWindow` (new, `DialogEditor.Avalonia.Shared`)

A standalone shared `Window` (the first one — existing shared components are
`UserControl`s hosted by per-app windows). Layout, scaled to the **Variant A** mockup
approved during brainstorming:

- **Title bar**: localized title (app-agnostic wording — this window is shown by either
  app, so it must not say "Welcome to the Dialog Editor" when hosted by PatchManager).
- **Intro text**: 1–2 sentences explaining the picker and that it can be changed later in
  Settings.
- **Theme picker**: the existing `ThemePickerView` UserControl, unmodified, bound to a
  `ThemePickerViewModel` constructed with the host app's `IThemeApplier`.
- **Preview panel**, all bound to `{DynamicResource Brush.*}` tokens (so it retints with
  the picker, like every other themed surface):
  - Three small node cards side by side — NPC, Player, Narrator — each with its header
    colour, Layer 2.5 shape glyph (circle/square/triangle), and a line of sample dialogue
    text in the card body.
  - A row of status badges: Warning (`Brush.Severity.Warning`) and Error
    (`Brush.Severity.Error`), each with their Layer 2.5 glyph (⚠/⛔) so the badges read
    correctly even in Colourblind/High-Contrast. (No "OK/success" badge — there is no
    `Brush.Severity.Success` token today, and adding one solely for this preview would
    be scope creep.)
  - A row of three buttons demonstrating `Brush.Button.Primary.Background`,
    `Brush.Button.Destructive.Background`, and the plain toolbar-button style.
- **Footer**: a single **Continue** button (primary style).

The window:
- carries `AutomationProperties.Name`/`HelpText` mirroring its `ToolTip.Tip` per
  `AutomationHelpTextTests` (solution-wide, already enforced — nothing new to opt into).
- is resizable (`SizeToContent="Height"`, `MinWidth`) per `ResizableDialogTests`.
- gets a `FocusHintBar` if the `ThemePickerView` ComboBox's `HelpText` (the existing
  `Settings_ThemeTooltip`) says more than its visible "Theme" label — following the
  item-13/16 precedent, this is a real explanation beyond the label, so: **yes**, add a
  `FocusHintBar`.
- gets the app icon (see §3.4).
- All static text uses `{DynamicResource}` (not `{StaticResource}`) for both strings and
  `Brush.*`/`FontSize.*` tokens, matching `ThemePickerView`'s existing convention of
  resolving correctly regardless of which app's resource scope hosts it.

No new ViewModel is needed for the window itself: the picker's logic lives entirely in
`ThemePickerViewModel` (unchanged), and the preview panel is static markup with no
data bindings beyond resource lookups. The window's code-behind only wires the
**Continue** button / window-close to the App.axaml.cs handoff in §3.3.

### 3.3 `App.axaml.cs` (both apps)

Both apps already call `new ThemeApplier().Apply(AppSettings.Theme)` (and, for the
editor, `FontScaleApplier`) before constructing `MainWindow`. The onboarding check slots
in right after those calls, so the dialog opens already retinted to the resolved
startup theme:

```csharp
new ThemeApplier().Apply(AppSettings.Theme);
// (editor only) new FontScaleApplier().Apply(AppSettings.FontScale);

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

`ThemeOnboardingWindow`'s Continue button simply calls `Close()`; the `Closed` handler
covers both Continue and the title-bar close button, so there's no path where the flag
is left unset and the dialog loops. Re-pointing `desktop.MainWindow` to the new
`MainWindow` *before* the onboarding window finishes closing keeps the default
`ShutdownMode.OnLastWindowClose` from tearing down the app between the two windows.

PatchManager's existing `.patchlist` command-line handling (loaded after `MainWindow` is
constructed today) is unaffected — it just runs inside the `else` branch / inside the
`Closed` handler's `MainWindow` construction, whichever path is taken.

### 3.4 Icon asset

`ThemeOnboardingWindow.axaml` is the first **standalone Window** in
`DialogEditor.Avalonia.Shared`, so it can't reference either app's
`avares://DialogEditor.{Avalonia,PatchManager}/Assets/app.ico` (an `avares://` URI is
assembly-scoped, and this XAML is compiled into the Shared assembly, loaded by both
hosts). Fix: copy `app.ico` into `DialogEditor.Avalonia.Shared/Assets/app.ico`, mark it
`<AvaloniaResource>` in the Shared `.csproj`, and reference
`avares://DialogEditor.Avalonia.Shared/Assets/app.ico` from the window. Both apps already
depend on the Shared assembly, so this resolves identically from either host (mirrors the
Layer 2 fix that embedded PatchManager's own previously-`<None>` icon).

### 3.5 Localisation (`DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml`)

New strings, all app-agnostic (no app name in the copy):

| Key | Example English text |
|---|---|
| `ThemeOnboarding_Title` | "Choose a Theme" |
| `ThemeOnboarding_Intro` | "Pick a colour theme below — you can change this anytime later in Settings." |
| `ThemeOnboarding_PreviewLabel` | "Preview" |
| `ThemeOnboarding_Continue` | "Continue" |
| `ThemeOnboarding_Card_Npc` | "NPC" |
| `ThemeOnboarding_Card_Player` | "Player" |
| `ThemeOnboarding_Card_Narrator` | "Narrator" |
| `ThemeOnboarding_Sample_Npc` | a short sample line of NPC dialogue |
| `ThemeOnboarding_Sample_Player` | a short sample player-choice line |
| `ThemeOnboarding_Sample_Narrator` | a short sample narration line |
| `ThemeOnboarding_Badge_Warning` | "Warning" |
| `ThemeOnboarding_Badge_Error` | "Error" |
| `ThemeOnboarding_Button_Primary` | "Primary" |
| `ThemeOnboarding_Button_Destructive` | "Destructive" |
| `ThemeOnboarding_Button_Plain` | "Plain" |

These live in `SharedStrings.axaml` (not the editor-only `Strings.axaml`) because the
window is shared. Existing editor-only strings like `Legend_NpcLine`/`Legend_Narrator`
aren't reused — PatchManager doesn't merge the editor's dictionary, and this window must
work standalone in both.

## 4. Tests

- **`AppSettingsTests`**: new `ThemeOnboardingSeen` cases mirroring `FontScale`/`Theme` —
  defaults to `false` on a fresh (no-file) settings path, defaults to `true` when loading
  a pre-existing settings file that lacks the key (simulated by writing a JSON file
  without `ThemeOnboardingSeen` before `Load()`), round-trips `true`/`false` via the
  setter.
- **`ThemeOnboardingWindow`** gets the same headless-Avalonia construction test as other
  dialogs (`LanguageCodeDialog`, `UnsavedChangesDialog`, …): constructs without throwing,
  `ThemePickerView`'s ComboBox is populated with all 5 entries, Continue button exists
  and is named.
- **Existing solution-wide structural tests** automatically extend to the new view with
  no extra opt-in: `NoStrayHexTests`, `NoStrayFontSizeTests`, `AutomationNameTests`,
  `AutomationHelpTextTests`, `ResizableDialogTests`, `FakeWatermarkTests`,
  `NoNamedColourForegroundTests`. These are the primary correctness net for the new
  XAML — if any convention is missed, the build fails.
- **App.axaml.cs wiring** (the `if (!ThemeOnboardingSeen)` branch and window handoff) is
  not unit-tested, consistent with the existing `ThemeApplier`/`FontScaleApplier` startup
  calls in the same file — these are thin, manually-verified integration glue.

## 5. Out of scope

- Live OS-theme-change subscription (already out of scope per the Layer 2/11(a) designs).
- A "don't show this again" checkbox — the dialog is inherently one-shot via
  `ThemeOnboardingSeen`.
- Re-showing onboarding after a settings reset/corruption recovery is an acceptable
  edge case (falls into the `Load()` failure path, which sets `ThemeOnboardingSeen =
  false` — the user simply sees the dialog again once).
