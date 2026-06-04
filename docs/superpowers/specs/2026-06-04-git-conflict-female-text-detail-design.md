# Git conflict resolution — female-variant text detail

**Date:** 2026-06-04
**Status:** Approved (design)

## Problem

When two branches diverge on the localized text of the same node + language, the
git merge-conflict resolution dialog shows a single mine-vs-theirs text block. A
`NodeTranslation` carries two strings — `DefaultText` and `FemaleText` — but the
analyzer collapses the conflict to **one** display value via a `DisplayText`
helper:

```csharp
// GitMergeAnalyzer.DisplayText (current)
self.DefaultText != other.DefaultText ? self.DefaultText : self.FemaleText
```

Two consequences:

1. **Female-only diff (documented gap).** When `DefaultText` is identical on both
   sides and only `FemaleText` differs, the dialog shows the female text *without
   labelling it as the female variant* — the writer sees what looks like the main
   text and has no cue why it differs.
2. **Both-differ case (undocumented).** When *both* `DefaultText` and `FemaleText`
   differ, only the `DefaultText` diff is shown; the female change is hidden
   entirely.

The merge itself is correct in both cases — `MergeBuilder.SetTranslation` replaces
the whole `NodeTranslation` (both fields) when a side is chosen. This is purely a
**display** defect.

## Goal

Show both the Default and Female text variants, each as its own labelled,
word-level-highlighted mine-vs-theirs row, whenever the node has female text.
Fixes both cases above.

## Non-goals

- No change to merge/apply logic — `MergeBuilder` already replaces the whole
  `NodeTranslation`.
- `FieldName` stays a pure language code (it is the language key
  `MergeBuilder.cs:90-91` uses to apply the chosen side — encoding a variant into
  it would break the merge).
- No new variants beyond the fixed two (`DefaultText`, `FemaleText`).

## Design

### Model — `DialogEditor.Patch/GitConflict/MergeConflict.cs`

For a `TranslationEdit` conflict, `MineValue`/`TheirsValue` change meaning: they
now always hold the **Default** text (previously the collapsed `DisplayText`
result). Add two init-only properties for the female variant:

```csharp
/// Female-variant text for a TranslationEdit conflict (mine / theirs side).
/// Empty for every other conflict kind. Display-only: the merge replaces the
/// whole NodeTranslation regardless of which sub-field differs.
public string MineFemaleValue   { get; init; } = "";
public string TheirsFemaleValue { get; init; } = "";
```

Init-only (not positional) so existing `new MergeConflict(...)` call sites stay
valid.

### Analyzer — `DialogEditor.Patch/GitConflict/GitMergeAnalyzer.cs`

In the translation-edit loop (`:94-100`), build the conflict with the Default text
in `MineValue`/`TheirsValue` and the female text in the new properties:

```csharp
granular.Add(new MergeConflict(
    MergeConflictKind.TranslationEdit, conv, key.NodeId, key.Lang,
    mineT.DefaultText, theirT.DefaultText)
{
    MineFemaleValue   = mineT.FemaleText,
    TheirsFemaleValue = theirT.FemaleText,
});
```

Delete the now-unused `DisplayText` helper (`:169-172`).

### ViewModel — `DialogEditor.ViewModels/ViewModels/GitConflictResolutionViewModel.cs`

`ConflictRowViewModel` exposes the female values and a visibility flag:

```csharp
public string MineFemaleValue   => Conflict.MineFemaleValue;
public string TheirsFemaleValue => Conflict.TheirsFemaleValue;

public bool HasFemaleRow =>
    Kind == MergeConflictKind.TranslationEdit
    && (!string.IsNullOrEmpty(MineFemaleValue) || !string.IsNullOrEmpty(TheirsFemaleValue));
```

`Title` is unchanged (`GitConflict_RowTitleTranslation`) — labelling moves into the
detail body.

### View — `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml(.cs)`

Per side, add a "Default text" / "Female text" caption above the existing diff
block and a second diff block for the female variant. Captions and female blocks
bind `IsVisible` to `HasFemaleRow` (DataContext is the selected
`ConflictRowViewModel`):

```
[Mine header]
  [Default text caption]   (IsVisible = HasFemaleRow)
  MineDiffText             (existing — Default text)
  [Female text caption]    (IsVisible = HasFemaleRow)
  MineFemaleDiffText       (IsVisible = HasFemaleRow)
[Mine radio]
```

Mirror on the theirs side (`TheirsFemaleDiffText`). When `HasFemaleRow` is false
(field edits, structural conflicts, translations with no female text) the layout
is identical to today: single unlabelled block.

`UpdateDiff` runs `TextDiff` on the Default pair as today, and — when
`HasFemaleRow` — also on the female pair (`MineFemaleValue` vs `TheirsFemaleValue`),
assigning the highlighted inlines to the female blocks. The existing
FieldEdit/structural branches are unchanged.

### Localization — `DialogEditor.Avalonia/Resources/Strings.axaml`

```xml
<sys:String x:Key="GitConflict_DefaultTextLabel">Default text</sys:String>
<sys:String x:Key="GitConflict_FemaleTextLabel">Female text</sys:String>
```

Added to the existing git-conflict region (after `GitConflict_RowTitleTranslation`).
The captions are static labels, so they bind as `{StaticResource ...}` in XAML —
no hard-coded inline text.

## Behaviour matrix

| Case | Default row | Female row |
|------|-------------|------------|
| Default differs, no female text | highlighted diff | hidden |
| Female-only differs (the bug) | identical (no highlight) | highlighted diff |
| Both differ | highlighted diff | highlighted diff |
| Field edit / structural | single unlabelled block (unchanged) | hidden |

## Testing (TDD order)

1. **Analyzer** (`GitMergeAnalyzerTests`):
   - Rewrite `TranslationFemaleTextDiffers_*` to the new contract: female-only diff
     → `MineValue`/`TheirsValue` equal (the shared Default), female text in
     `MineFemaleValue`/`TheirsFemaleValue`.
   - Add a both-differ test: Default *and* female differ → all four values carry
     their respective sides.
   - Existing `TranslationTextDiffers_IsTranslationEditConflict` still holds
     (`MineValue`/`TheirsValue` = the differing Default; female values empty).
2. **ViewModel** (`GitConflictResolutionViewModelTests`): `TranslationEdit` with
   female text → `HasFemaleRow` true and female values populated; `FieldEdit` →
   `HasFemaleRow` false.
3. **View** (`GitConflictResolutionWindowTests`): a translation conflict with
   female text → female blocks present, visible, and carry highlighted inlines; a
   field edit → female blocks collapsed (`IsVisible` false).

## Files touched

- `DialogEditor.Patch/GitConflict/MergeConflict.cs`
- `DialogEditor.Patch/GitConflict/GitMergeAnalyzer.cs`
- `DialogEditor.ViewModels/ViewModels/GitConflictResolutionViewModel.cs`
- `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml`
- `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml.cs`
- `DialogEditor.Avalonia/Resources/Strings.axaml`
- Tests: `GitMergeAnalyzerTests`, `GitConflictResolutionViewModelTests`,
  `GitConflictResolutionWindowTests`
- Docs: update `Gaps.md` (remove the cosmetic-limitation bullet).
