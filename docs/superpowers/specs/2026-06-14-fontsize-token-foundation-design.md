# FontSize Token Foundation — Design Spec

**Gaps.md item 6 (part A):** "Tiny fixed font sizes, no text scaling." This spec covers
only the token-foundation half: centralising every literal `FontSize` value in the app
into a named `FontSize.*` token layer in `Tokens.axaml`, with a `NoStrayHexTests`-style
enforcement test. The runtime UI-scale-factor setting and fixed-size-window clipping
fixes (item 6 part B) are explicitly **out of scope** — they remain Gaps.md item 6,
narrowed to just that follow-up work once this spec lands.

## Goals

- One source of truth for font sizes, named by semantic role, alongside the existing
  `Brush.*` token layer in `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`.
- **Zero visual change.** Every literal value maps 1:1 to a token of the same value —
  this is a rename/centralisation pass, not a redesign.
- An enforcement test that fails the build if any `.axaml` file (outside `Tokens.axaml`)
  contains a bare numeric `FontSize`, exactly mirroring the rigor of `NoStrayHexTests`
  for colours.

## Token Definitions

Added to `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`, as a new section near
the top of the file (before the `Brush.*` section), using Avalonia's `x:Double` resource
type:

```xml
<!-- Font sizes — semantic type-scale layer (mirrors the Brush.* token layer below).
     Every view binds these keys; no bare FontSize="N" literal may appear outside
     this file. 1:1 mapping of every size value in use today — pure renaming, no
     visual change. See docs/superpowers/specs/2026-06-14-fontsize-token-foundation-design.md. -->
<x:Double x:Key="FontSize.Micro">8</x:Double>
<x:Double x:Key="FontSize.Caption">9</x:Double>
<x:Double x:Key="FontSize.Small">10</x:Double>
<x:Double x:Key="FontSize.Label">11</x:Double>
<x:Double x:Key="FontSize.Body">12</x:Double>
<x:Double x:Key="FontSize.Medium">13</x:Double>
<x:Double x:Key="FontSize.Subtitle">14</x:Double>
<x:Double x:Key="FontSize.Title">18</x:Double>
<x:Double x:Key="FontSize.Display">32</x:Double>
```

| Token              | Value | Example usage |
|--------------------|-------|----------------|
| `FontSize.Micro`   | 8     | `NodeDetailView` `TextBlock.group-header` |
| `FontSize.Caption` | 9     | `NodeDetailView` `TextBlock.field-label`, small captions |
| `FontSize.Small`   | 10    | Detail-panel fields/combos/checkboxes, metadata text |
| `FontSize.Label`   | 11    | Secondary labels, disabled hints |
| `FontSize.Body`    | 12    | The default/most common body text size (162 instances) |
| `FontSize.Medium`  | 13    | Slightly emphasised body text |
| `FontSize.Subtitle`| 14    | Section headers within windows |
| `FontSize.Title`   | 18    | `AboutWindow` app name |
| `FontSize.Display` | 32    | `TestModeOverlay` banner |

## Migration Surface

All `.axaml` files containing a literal numeric `FontSize` (excluding `Tokens.axaml`
itself, which defines the tokens). Two syntactic patterns, both must be rewritten:

1. **Inline attribute:** `FontSize="12"` → `FontSize="{StaticResource FontSize.Body}"`
2. **Style setter:** `<Setter Property="FontSize" Value="12"/>` →
   `<Setter Property="FontSize" Value="{StaticResource FontSize.Body}"/>`

Affected files (30 total, ~316+ occurrences across both patterns):

```
DialogEditor.Avalonia.Shared/FocusHintBar.axaml
DialogEditor.Avalonia.Shared/PatchManagerView.axaml
DialogEditor.Avalonia.Shared/ThemePickerView.axaml
DialogEditor.Avalonia/Views/AboutWindow.axaml
DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml
DialogEditor.Avalonia/Views/BlameWindow.axaml
DialogEditor.Avalonia/Views/BranchNameDialog.axaml
DialogEditor.Avalonia/Views/ChangelogWindow.axaml
DialogEditor.Avalonia/Views/CommitConsentDialog.axaml
DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml
DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml
DialogEditor.Avalonia/Views/ConversationNameDialog.axaml
DialogEditor.Avalonia/Views/ConversationView.axaml
DialogEditor.Avalonia/Views/DiffHelpWindow.axaml
DialogEditor.Avalonia/Views/DiffWindow.axaml
DialogEditor.Avalonia/Views/FindReplaceWindow.axaml
DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml
DialogEditor.Avalonia/Views/ForceDeleteDialog.axaml
DialogEditor.Avalonia/Views/GameBrowserView.axaml
DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml
DialogEditor.Avalonia/Views/HistoryWindow.axaml
DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml
DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml
DialogEditor.Avalonia/Views/LegendWindow.axaml
DialogEditor.Avalonia/Views/MainWindow.axaml
DialogEditor.Avalonia/Views/NodeDetailView.axaml
DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml
DialogEditor.Avalonia/Views/SettingsWindow.axaml
DialogEditor.Avalonia/Views/TestModeOverlay.axaml
DialogEditor.Avalonia/Views/UnsavedChangesDialog.axaml
```

(30 entries listed — `SettingsWindow.axaml` was found only via the Setter-pattern scan
and must be included alongside the 29 found via the attribute-pattern scan.)

No `FontSize="{Binding ...}"` or other non-literal `FontSize` usages exist anywhere in
the app today (confirmed by search), so every occurrence is one of the two patterns
above — no third case to handle.

## Enforcement Test

New file `DialogEditor.Tests/Theming/NoStrayFontSizeTests.cs`, structurally mirroring
`NoStrayHexTests`:

- Reuses the same `SolutionRoot()` / `IsBuildArtifact()` helper pattern (walk up to
  `DialogEditor.slnx`, skip `bin`/`obj`).
- Scans every `*.axaml` file under the solution root.
- Skips `Tokens.axaml` (the one file allowed to define raw numeric `FontSize` values).
- Regex `FontSize\s*=\s*"[0-9]` catches both `FontSize="12"` (inline attribute) and
  `Value="12"` lines are matched via a second regex `Property\s*=\s*"FontSize"` on the
  same line as `Value\s*=\s*"[0-9]` — in practice, since every existing Setter has
  `Property="FontSize"` and `Value="N"` on the same line, a single combined check
  suffices: a line is a violation if it contains `Property="FontSize"` and a bare
  numeric `Value="..."`, OR contains `FontSize="<number>` as a direct attribute.
- One `[Fact]`: `NoFontSizeLiteralsOutsideTokens`, asserting `offenders.Count == 0` with
  the same `"{filename}:{line}: {trimmed line}"` offender-reporting format as
  `NoStrayHexTests`.

This test starts RED (≈316 failures) and is driven to GREEN by the migration — it is
both the spec for "done" and the safety net catching any of the 30 files missed during
the mechanical sweep.

## Additional Verification

A small addition to the existing `TokenRegistryTests.cs` (or a new
`FontSizeTokenTests.cs` alongside it, following whichever pattern
`TokenRegistryTests` already uses for `Brush.*`) asserting the registry contains exactly
the 9 `FontSize.*` keys above with their documented values — pins the token values
themselves against accidental future edits, the same way the colour registry tests pin
`Brush.*` → `Palette.*` mappings.

## Testing Strategy

1. **RED:** Write `NoStrayFontSizeTests` first; confirm it fails with ~316+ offenders
   across 30 files.
2. **GREEN, incrementally:** Migrate files in batches (grouped for subagent execution —
   exact grouping left to the implementation plan). After each batch, re-run the
   enforcement test to confirm the offender count strictly decreases and no new
   offenders appear; run the full `dotnet build` to catch XAML resource-resolution
   errors early (a typo'd `{StaticResource FontSize.Bdoy}` fails at build/load time).
3. **Final:** Enforcement test passes with zero offenders; `TokenRegistryTests`/
   `FontSizeTokenTests` passes; full `DialogEditor.Tests` suite (currently 1364 tests)
   passes with no new failures.

## Out of Scope

- Runtime UI-scale-factor Settings control (Gaps.md item 6 part B).
- Fixing fixed-size windows (`SettingsWindow CanResize="False" Height="220"`, etc.)
  that would clip under OS text scaling — also part B.
- Any deliberate size *changes* (e.g. bumping `FontSize.Micro` for legibility) — this
  pass is value-preserving by design; legibility improvements are a separate, explicit
  decision for a future gap.
