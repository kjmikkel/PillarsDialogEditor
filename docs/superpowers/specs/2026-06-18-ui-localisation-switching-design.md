# UI Localisation Switching — Design Spec

**Date:** 2026-06-18
**Scope:** Items 1–3 of the UI Localisation Readiness gap (restart-free language switching
mechanism, AppSettings integration, CoreStrings culture wiring). Translation workflow (item 4),
layout elasticity audit (item 5), and enforcement tests for hard-coded strings (item 6) are
deferred to a follow-up gap.

---

## Problem

The app's string dictionaries (`Strings.axaml`, `SharedStrings.axaml`,
`PatchManager/Strings.axaml`) are English-only, with no mechanism to swap in a translation.
`AppSettings` has no UI-language field. The app can't switch language.

---

## Goals

1. A `LanguageApplier` that injects a per-language overlay ResourceDictionary at runtime —
   last-merged wins, so untranslated keys fall back to the English base automatically.
2. `AppSettings.UiLanguage` persisted setting + `LanguagePickerView`/`LanguagePickerViewModel`
   hosted in both apps' settings UI, switching **live** (no restart required).
3. `CoreLocale.SetCulture` wires the four `CoreStrings` (`Script_Prefix_Enter/Exit/Update`,
   `Condition_Not`) to the selected language's `.resx` satellite assembly.
4. All `{StaticResource <string key>}` in views converted to `{DynamicResource}` so string
   bindings retint live; `NoStaticStringResourceTests` enforces the invariant going forward.
5. A lean `PatchManagerSettingsWindow` (Theme + Language) replacing the existing top-bar theme
   picker in PatchManager.

**Non-goals (this spec):**
- "Auto" (OS locale detection) — deferred until a translation ships; a TODO comment is left in
  the catalog.
- Font-size live switching — `{StaticResource FontSize.*}` stays restart-required by design;
  tracked as a separate deferred gap ("Font scale live switching").
- Actual translation files — the mechanism ships English-only; adding `Strings.de.axaml` etc. is
  a follow-up once a translator is engaged.
- Live-switching for action-result strings (`StatusText` etc.) — these are assigned in action
  handlers and refresh naturally on next action; this is expected behavior.

---

## Architecture

### New components

| Component | Location | Mirrors |
|---|---|---|
| `ILanguageApplier` + `LanguageApplier` | `Shared/Theming/` | `IThemeApplier` + `ThemeApplier` |
| `LocaleService` | `Shared/Theming/` | `ThemeService` |
| `AppSettings.UiLanguage` | `ViewModels/Services/AppSettings.cs` | `AppSettings.Theme` |
| `LanguageOption` record | `ViewModels/ViewModels/` | `ThemeOption` |
| `LanguagePickerViewModel` | `ViewModels/ViewModels/` | `ThemePickerViewModel` |
| `LanguagePickerView` | `Shared/` | `ThemePickerView` |
| `CoreLocale` | `Core/Resources/` | _(new public facade over `CoreStrings.Culture`)_ |
| `PatchManagerSettingsWindow` | `PatchManager/` | editor `SettingsWindow` (lean subset) |
| `NoStaticStringResourceTests` | `Tests/` | `NoStrayHexTests` |

### `LanguageApplier`

Unlike `ThemeApplier`, string overlays live in three assemblies (Shared, editor, PatchManager),
so the applier takes the overlay URI templates at construction. Each `App.axaml.cs` constructs
it with its own list:

```csharp
// Editor
new LanguageApplier(
    "avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.{0}.axaml",
    "avares://DialogEditor.Avalonia/Resources/Strings.{0}.axaml"
).Apply(AppSettings.UiLanguage);

// PatchManager
new LanguageApplier(
    "avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.{0}.axaml",
    "avares://DialogEditor.PatchManager/Resources/Strings.{0}.axaml"
).Apply(AppSettings.UiLanguage);
```

`Apply("en")` is a **no-op** — English is the base, no overlay needed. When a translation ships,
`Strings.de.axaml` etc. are added as `AvaloniaResource` entries and a new catalog entry is added.

The applier instance is created once at app startup, stored on the `App` class, and passed
into `LanguagePickerViewModel` the same way `ThemeApplier` is passed into
`ThemePickerViewModel` — via the settings window constructor. The same instance is reused for
live switching. It tracks injected `ResourceInclude` entries as instance state so it can remove
the previous overlay on a subsequent `Apply` call.

**Catalog** (single add-point, never bake in "there is one language"):

```csharp
// TODO: add "Auto" (OS locale detection) + entries for each translation once one ships.
private static readonly LanguageEntry[] Catalog = [
    new("en", "Settings_Language_English"),
];
```

### `LocaleService`

Exact structural copy of `ThemeService` — an `ObservableObject` singleton with
`[ObservableProperty] int _revision` and an `internal void Bump()`. Lives in
`Shared/Theming/`. Called inside `LanguageApplier.Apply` after the overlay swap.

Theme and locale Revision counters are **independent** — a theme change does not re-evaluate
ViewModel string properties, and vice versa.

### `{DynamicResource}` conversion

All `{StaticResource <key>}` in view `.axaml` files are converted to `{DynamicResource <key>}`
**except**:

| Keep as `StaticResource` | Reason |
|---|---|
| `{StaticResource FontSize.*}` | Font scale is restart-required (deferred live-switching gap) |
| `{StaticResource Palette.*}` | Only appears inside resource dict files, never in views |

`Brush.*` references are already `{DynamicResource}` — no change.

`NoStaticStringResourceTests` (solution-wide scan) fails if any `.axaml` view file contains
`{StaticResource <key>}` where `<key>` is a known string key. Mirrors
`NoStrayHexTests`/`AutomationNameTests` in scope and enforcement style.

### ViewModel subscription

Only ViewModels with **computed string properties** — getters that call `Loc.Get` directly —
need to subscribe to `LocaleService.Revision`. Action-result strings (`StatusText`, etc.) are
assigned imperatively in handlers and refresh naturally on next action; no subscription needed
for those.

Subscription pattern (per affected VM):
```csharp
LocaleService.Current.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(LocaleService.Revision))
        OnPropertyChanged(string.Empty);
};
```

`OnPropertyChanged(string.Empty)` signals "all properties changed" — Avalonia's binding engine
re-evaluates all bindings on the VM. This is safe: it's a sledgehammer, but language changes
are rare (once per session at most).

The implementation plan enumerates exactly which VMs need the subscription; the count is
expected to be small (<10), as most `Loc.Get` usage is in action handlers, not property getters.

### `CoreLocale` public facade

`CoreStrings` is `internal static` in `DialogEditor.Core`. A thin public facade exposes culture
setting without widening `CoreStrings`:

```csharp
// DialogEditor.Core/Resources/CoreLocale.cs
public static class CoreLocale
{
    public static void SetCulture(string? langCode) =>
        CoreStrings.Culture = langCode is null or "en"
            ? null
            : new System.Globalization.CultureInfo(langCode);
}
```

Called at startup and by `LanguagePickerViewModel` on selection change. No interface needed
(pure static call, no Avalonia dependency).

---

## Data Flow

### Startup sequence (both apps)

```
1. AvaloniaXamlLoader.Load      — App.axaml merges Strings.axaml base
2. ThemeApplier.Apply           — palette + Tokens swap
3. LanguageApplier.Apply        — inject overlay if non-English (no-op for "en")
4. CoreLocale.SetCulture        — sets CoreStrings.Culture
5. FontScaleApplier.Apply       — FontSize.* token scaling
6. First window shown
```

### Live-switch flow (LanguagePickerViewModel selection change)

```
1. AppSettings.UiLanguage = newLangCode       — persist
2. ILanguageApplier.Apply(newLangCode)        — swap overlay; fires LocaleService.Bump()
3. CoreLocale.SetCulture(newLangCode)         — CoreStrings satellite culture
4. LocaleService.Bump() → Revision++          — (fired inside Apply)
5. Subscribed VMs raise OnPropertyChanged("") — computed getters re-evaluate
6. DynamicResource string bindings update     — Avalonia binding engine handles automatically
```

---

## `PatchManagerSettingsWindow`

A new `PatchManagerSettingsWindow` in `DialogEditor.PatchManager` replaces the existing
top-bar theme picker in PatchManager's `MainWindow`.

- Two sections: **Appearance** (hosts `ThemePickerView`) and **Language** (hosts
  `LanguagePickerView`).
- Opened by a `⚙ Settings…` button replacing the current top-bar theme picker.
- `SizeToContent="Height"`, `MinWidth` set, `CanResize="False"` **not** used
  (per `ResizableDialogTests`).
- `Icon="avares://DialogEditor.PatchManager/Assets/app.ico"` (per all-windows rule).
- `FocusHintBar` included (both pickers have `HelpText`).

---

## Error handling

| Scenario | Behaviour |
|---|---|
| `AppSettings.UiLanguage` has unknown value | `LanguageApplier` falls back to `"en"`; logs `AppLog.Warn` |
| Overlay file missing (future translation) | `AppLog.Warn`; English base remains intact |
| Invalid culture code in `CoreLocale.SetCulture` | Catch `CultureNotFoundException`; log; fall back to `null` |

---

## Testing

### `LanguageApplierTests` — `DialogEditor.Tests/Theming/`

Mirrors `ThemeApplierTests`. Uses a headless Avalonia fixture:

- Catalog contains `"en"`.
- `Apply("en")` leaves merged dictionaries unchanged.
- `Apply` with unknown code falls back to `"en"` and logs.
- `Apply` called twice (live switch) removes previous overlay before adding new one.

### `LanguagePickerViewModelTests` — `DialogEditor.Tests/ViewModels/`

Exact structural mirror of `ThemePickerViewModelTests`, with `StubLanguageApplier` mirroring
`StubThemeApplier`. Four tests:

1. Available languages sourced from applier.
2. Initial selection from `AppSettings.UiLanguage`.
3. Unknown saved value falls back to first catalog entry.
4. Changing selection persists to `AppSettings` and calls `Apply`.

### `NoStaticStringResourceTests` — `DialogEditor.Tests/`

Solution-wide scan. Fails if any non-resource `.axaml` file contains
`{StaticResource <key>}` where `<key>` is a string key (defined in any of the three
string dictionaries). `{StaticResource FontSize.*}` and `{StaticResource Palette.*}`
are explicitly excluded from the scan.

---

## Deferred gap: Font scale live switching

`FontScaleApplier` currently runs once at startup (restart-required). Making font scaling live
would require:
- Converting `{StaticResource FontSize.*}` → `{DynamicResource FontSize.*}` in all views.
- Making `FontScaleApplier.Apply` callable at runtime (not just startup).
- A `FontScaleService.Revision` tick so text-measurement-dependent layouts reflow.

Tracked as a separate gap: **"Font scale live switching"**.
