# Flow Analytics — Severity Tier as Text

**Date:** 2026-07-04
**Status:** Approved
**Gaps.md origin:** accessibility item 12's deferred FlowAnalytics fragment ("per-row icons
are a different 'many tab stops in a list' problem").

## Problem

Each Flow Analytics issue row shows a coloured severity icon (⛔ error / ⚠ warning inside
a tinted 16px `Border`) plus a textual `KindLabel`. The issue *kind* is therefore never
colour-only, but the severity *tier* — error vs warning — is carried solely by the icon's
colour and glyph. The glyph character is not reliably announced by screen readers and has
no hover explanation for sighted users.

## Decision

Expose the tier as text on the icon itself (Approach A): a localized `SeverityLabel` on
`FlowIssueViewModel`, bound to the severity `Border` as both `ToolTip.Tip` and
`AutomationProperties.Name`. No visible layout change.

Rejected: folding the tier into the visible `KindLabel` (changes visible UI, fights the
fixed 110px kind column, duplicates the glyph for sighted users); combined text on the row
container (rows are not focusable; buries the tier in a long utterance).

## Design

1. **ViewModel** — `FlowIssueViewModel` (`DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`):

   ```csharp
   /// Severity tier as text — the icon's colour/glyph carry it visually; this is
   /// the screen-reader/tooltip equivalent. Same binary rule as
   /// FlowIssueKindToSeverityGlyphConverter (each pinned by its own test).
   public string SeverityLabel => Kind == FlowIssueKind.Unreachable
       ? Loc.Get("FlowAnalytics_Severity_Error")
       : Loc.Get("FlowAnalytics_Severity_Warning");
   ```

2. **Strings** (`Strings.axaml`, Flow Analytics block):
   - `FlowAnalytics_Severity_Error` = `Error`
   - `FlowAnalytics_Severity_Warning` = `Warning`

3. **View** (`FlowAnalyticsWindow.axaml`, severity `Border` ~line 118): add
   `ToolTip.Tip="{Binding SeverityLabel}"` and
   `AutomationProperties.Name="{Binding SeverityLabel}"`.

4. **Gaps.md** — item 12: replace the FlowAnalytics deferral sentence with a resolution
   note (severity tier now textual per row; the "many tab stops" concern was avoided by
   putting the text on the existing non-focusable icon rather than adding tab stops).
   The `DiffWindow` swatch half remains a documented won't-do.

## Testing

- Red/green: new tests in `FlowAnalyticsViewModelTests` — `SeverityLabel` returns the
  `FlowAnalytics_Severity_Error` key for `Unreachable` and the
  `FlowAnalytics_Severity_Warning` key for every other `FlowIssueKind`
  (`StubStringProvider` echoes keys).
- Existing `FlowIssueKindToSeverityGlyphConverterTests` continues to pin the glyph side.
- Manual: hover the icon → tooltip shows the tier; no visual layout change.
