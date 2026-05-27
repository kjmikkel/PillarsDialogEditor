# Barks System — Design Spec

**Date:** 2026-05-27  
**Status:** Approved

---

## Background

`DisplayType` already exists on every `ConversationNode` (stored as `"Conversation"` or `"Bark"`) and round-trips correctly through both PoE1 and PoE2 serializers. A ComboBox in `NodeDetailView` lets writers toggle it. However, bark nodes are visually indistinguishable from conversation nodes on the canvas, and there is no bark-specific validation to help writers catch common authoring mistakes.

This spec covers three additions:

1. **Canvas visual distinction** — bark nodes render with a distinct amber color scheme so they are recognisable at any zoom level.
2. **Bark-specific validation** — non-blocking warnings for text length and structurally inappropriate child links, surfaced in the node detail panel.
3. **Flow Analytics integration** — the same two bark rules appear as issues in the Flow Analytics window, navigable like any other flow issue.

---

## Canvas Visual (Approach A — Amber color override)

### NodeColorConverter

Replace the three `SpeakerCategoryToBrush` multi-bindings in `ConversationView.axaml` with a single `NodeColorConverter` that implements `IMultiValueConverter`. It receives two values in order:

1. `SpeakerCategory` (enum)
2. `DisplayType` (string)

**When `DisplayType == "Bark"`** — return amber tones regardless of speaker category:

| Zone   | Color      | Hex       |
|--------|------------|-----------|
| Header | Dark gold  | `#7A5C00` |
| Body   | Warm cream | `#FFF8DC` |
| Footer | Light amber| `#E8D080` |

**When `DisplayType == "Conversation"`** — delegate to the existing speaker-category palette (same colors as `SpeakerCategoryToBrushConverter` today). The old converter is retained for any other use sites.

### Speaker-identity dot

Because the amber override hides speaker-category color from the header, a small `Ellipse` (6×6 px) is added to the left of the title `TextBlock` in the node template. It binds `Fill` to `SpeakerCategory` via the existing `SpeakerCategoryToBrushConverter` (header variant). This dot is always present but only informative on bark nodes; on conversation nodes the header color already conveys the same information.

### IsBark on NodeViewModel

`NodeViewModel` gains a computed `bool IsBark` property:

```csharp
public bool IsBark => _displayType == "Bark";
```

It must be raised in the `DisplayType` setter's `OnPropertyChanged` chain so bindings update immediately when the writer changes the ComboBox.

---

## Validation

Warnings are non-blocking — they inform writers but do not prevent saving or serialisation.

### Shared constant

The text-length threshold is defined once in `DialogEditor.Core` so both the ViewModel layer and `FlowAnalysisService` reference the same value:

```csharp
// DialogEditor.Core/Analytics/BarkConstants.cs
public static class BarkConstants
{
    public const int TextLengthWarningThreshold = 150;
}
```

### Rule 1 — Text length (NodeViewModel)

`NodeViewModel` exposes:

```csharp
public IReadOnlyList<string> BarkWarnings { get; }
```

The list contains a length-warning string when `IsBark && DefaultText.Length > BarkConstants.TextLengthWarningThreshold`. It is empty for non-bark nodes or bark nodes within the limit.

`BarkWarnings` is re-evaluated (via `OnPropertyChanged`) whenever `DefaultText` or `DisplayType` changes.

### Rule 2 — Player-choice children (NodeDetailViewModel)

`NodeDetailViewModel` already exposes `Links` (a list of `ConnectionViewModel`). It gains an additional computed list:

```csharp
public IReadOnlyList<string> BarkWarnings { get; }
```

This list contains a player-choice warning when `IsBark && Links.Any(l => l.Target.Owner.IsPlayerChoice)`. It merges with any warnings from `NodeViewModel.BarkWarnings` (text length) so the UI has a single source to bind to.

The property is re-evaluated when `Links`, `DisplayType`, or any linked node's `IsPlayerChoice` changes.

### Warning UI

In `NodeDetailView.axaml`, immediately below the DisplayType ComboBox, a `Border` with amber background (`#7A5C00` at 15% opacity) and amber border contains an `ItemsControl` bound to `BarkWarnings`. It is `IsVisible` only when `BarkWarnings.Count > 0`. Each warning is one line of text in a small font.

---

## Flow Analytics Integration

### New FlowIssueKind values

Two new members are added to the `FlowIssueKind` enum in `FlowAnalysisModels.cs`:

- `BarkTextTooLong` — bark node whose `DefaultText.Length > BarkConstants.TextLengthWarningThreshold`
- `BarkHasPlayerChoiceChild` — bark node that links to at least one node where `IsPlayerChoice == true`

### FlowAnalysisService changes

`FlowAnalysisService.Analyze()` builds a node-by-id lookup before its main loop. During the loop it adds `BarkTextTooLong` issues directly. For `BarkHasPlayerChoiceChild` it checks each outgoing link's target against the lookup — this is a second pass or an inline check using the already-built `linksByFrom` map.

Both bark issue kinds use the amber severity bar in `FlowIssueKindToSeverityBrushConverter` (they are warnings, not errors). The converter's current fallback to amber already handles them without code changes, but the switch should be made explicit for the two new values.

### FlowIssueViewModel.KindLabel

Two new cases in the `KindLabel` switch:

- `FlowIssueKind.BarkTextTooLong` → `Loc.Get("FlowAnalytics_Issue_BarkTextTooLong")`
- `FlowIssueKind.BarkHasPlayerChoiceChild` → `Loc.Get("FlowAnalytics_Issue_BarkHasPlayerChoiceChild")`

---

## Testing

All tests are written before implementation (TDD — red/green).

### NodeViewModelTests

- `BarkWarnings_EmptyForConversationNode` — a node with `DisplayType = "Conversation"` and long text produces no warnings.
- `BarkWarnings_EmptyForShortBark` — a bark node with `DefaultText.Length <= 150` produces no warnings.
- `BarkWarnings_TextLengthWarning_WhenBarkTextExceedsThreshold` — a bark node with `DefaultText.Length > 150` produces exactly one warning.
- `IsBark_TrueWhenDisplayTypeBark` and `IsBark_FalseWhenDisplayTypeConversation`.

### NodeDetailViewModelTests

- `BarkWarnings_EmptyWhenNoPlayerChoiceChildren` — bark node with only NPC children produces no warnings.
- `BarkWarnings_PlayerChoiceWarning_WhenChildIsPlayerChoice` — bark node with at least one player-choice child produces a warning.

### FlowAnalysisServiceTests

- `Analyze_BarkNode_LongText_EmitsBarkTextTooLongIssue`
- `Analyze_BarkNode_ShortText_NoBarkTextIssue`
- `Analyze_BarkNode_WithPlayerChoiceChild_EmitsBarkHasPlayerChoiceChildIssue`
- `Analyze_BarkNode_WithNpcChild_NoBarkChildIssue`
- `Analyze_ConversationNode_LongText_NoIssue` — confirms the text rule only fires on bark nodes.

### NodeColorConverterTests (new test class)

- `Convert_BarkNode_ReturnsAmberHeader` — any `SpeakerCategory` + `"Bark"` → dark gold brush for the header zone.
- `Convert_ConversationNode_ReturnsSpeakerColor` — `SpeakerCategory.Player` + `"Conversation"` → player-blue header brush.

---

## Files Affected

| File | Change |
|------|--------|
| `DialogEditor.Avalonia/Converters/NodeColorConverter.cs` | New — `IMultiValueConverter` |
| `DialogEditor.Avalonia/Views/ConversationView.axaml` | Switch to `NodeColorConverter`; add speaker-identity dot |
| `DialogEditor.Avalonia/Views/NodeDetailView.axaml` | Add warning box below DisplayType ComboBox |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | Add warning message strings + two new `FlowAnalytics_Issue_Bark*` labels |
| `DialogEditor.Core/Analytics/BarkConstants.cs` | New — shared `TextLengthWarningThreshold` constant |
| `DialogEditor.Core/Analytics/FlowAnalysisModels.cs` | Add `BarkTextTooLong`, `BarkHasPlayerChoiceChild` to `FlowIssueKind` |
| `DialogEditor.Core/Analytics/FlowAnalysisService.cs` | Add bark issue checks |
| `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs` | Add `KindLabel` cases for two new kinds |
| `DialogEditor.Avalonia/Converters/FlowIssueKindToSeverityBrushConverter.cs` | Explicit amber cases for bark kinds |
| `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs` | Add `IsBark`, `BarkWarnings` (references `BarkConstants`) |
| `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` | Add merged `BarkWarnings` computed property |
| `DialogEditor.Tests/ViewModels/NodeViewModelTests.cs` | New bark warning tests |
| `DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs` | New player-choice child tests |
| `DialogEditor.Tests/Converters/NodeColorConverterTests.cs` | New converter tests |
| `DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs` | New bark issue tests |
