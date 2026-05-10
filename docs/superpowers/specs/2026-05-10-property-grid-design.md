# Property Grid for Node Detail Panel

**Date:** 2026-05-10
**Status:** Approved

## Problem

The right-pane `NodeDetailView` currently displays node properties using bespoke per-property `TextBlock` blocks, each with its own colour, conditional visibility, and formatting logic. Several properties (`Comments`, `ActorDirection`, the entire Voice section) are hidden when their value is empty/false. Adding a new property requires new hand-crafted XAML. The panel is visually inconsistent and does not always show all properties.

## Goal

Replace the bespoke display with a uniform, data-driven property grid that:

- Always shows every property (no conditional hiding based on data)
- Uses a consistent label/value row layout grouped into named sections
- Is driven by the ViewModel, not by XAML visibility bindings
- Makes adding a new property a one-line ViewModel change with no XAML touch

## Scope

Read-only display only. No editing, undo/redo, or write-back to the model. Canvas node appearance (`NodeViewModel`, canvas XAML) is not changed.

## Layout

The panel is split into two parts:

### Text section (top)

Two dedicated bordered `TextBlock` areas, always visible:
- **Default Text** — main dialog text, white
- **Female Text** — italic/dimmed when same as default, normal when distinct

### Property grid (below text)

Four named groups rendered by a single `ItemsControl` over `IReadOnlyList<PropertyGroup>`:

| Group | Properties |
|---|---|
| **Identity** | Node ID, Type, Speaker, Speaker GUID, Listener |
| **Display** | Display Type, Persistence, Actor Direction |
| **Logic** | Conditions, Scripts, Comments |
| **Voice** | External VO, Has VO, Hide Speaker |

### Links section (bottom)

A separate `IReadOnlyList<LinkRow>` rendered as one row per outgoing link. Each row has an arrow column (`→ NodeId`) and a detail column (`[w:2, cinematic]` or `—` if no extras).

## Data Model

Three new records in `DialogEditor.ViewModels` (presentation model only, not domain):

```csharp
public record PropertyRow(string Label, string Value);
public record PropertyGroup(string Name, IReadOnlyList<PropertyRow> Rows);
public record LinkRow(string Arrow, string Detail);
```

## ViewModel

`NodeDetailViewModel` is reduced from 26 observable properties to 6:

```csharp
[ObservableProperty] private bool _hasContent;
[ObservableProperty] private string _defaultText = string.Empty;
[ObservableProperty] private string _femaleTextDisplay = string.Empty;
[ObservableProperty] private bool _hasFemaleText;   // drives italic style only, not visibility
[ObservableProperty] private IReadOnlyList<PropertyGroup> _propertyGroups = [];
[ObservableProperty] private IReadOnlyList<LinkRow> _links = [];
```

All 20+ boolean "has-X" visibility flags are removed. Every property is always present in the group model. Empty/false values display as `"(none)"` (reusing the existing `NodeDetail_None` string key).

`FormatLink` is refactored to return `LinkRow` instead of a formatted string. `Clear()` and the overall `Load(NodeViewModel?)` signature are unchanged.

## XAML

`NodeDetailView.axaml` is rebuilt around:

- Two named `DataTemplate` resources: `PropertyRowTemplate` and `LinkRowTemplate`
- One named `Style` for section headers (small-caps, bottom border)
- One `ItemsControl` bound to `PropertyGroups`, using `PropertyRowTemplate`
- One `ItemsControl` bound to `Links`, using `LinkRowTemplate`

Semantic colour-coding is preserved in `PropertyRowTemplate` via value converters or DataTriggers where it adds meaning (orange for conditions, green for scripts, monospace for VO path). All label strings use `{StaticResource}` keys — no hard-coded text in XAML.

## Localisation

New keys in `DialogEditor.Avalonia/Resources/Strings.axaml`:

**Group headers:**
```
Label_GroupIdentity    → "IDENTITY"
Label_GroupDisplay     → "DISPLAY"
Label_GroupLogic       → "LOGIC"
Label_GroupVoice       → "VOICE"
Label_GroupLinks       → "LINKS"
```

**Property row labels** (used in ViewModel via `Loc.Get()`):
```
PropertyRow_NodeId         → "Node ID"
PropertyRow_Type           → "Type"
PropertyRow_Speaker        → "Speaker"
PropertyRow_SpeakerGuid    → "Speaker GUID"
PropertyRow_Listener       → "Listener"
PropertyRow_DisplayType    → "Display Type"
PropertyRow_Persistence    → "Persistence"
PropertyRow_ActorDirection → "Actor Direction"
PropertyRow_Conditions     → "Conditions"
PropertyRow_Scripts        → "Scripts"
PropertyRow_Comments       → "Comments"
PropertyRow_ExternalVO     → "External VO"
PropertyRow_HasVO          → "Has VO"
PropertyRow_HideSpeaker    → "Hide Speaker"
```

Obsolete `Label_*` keys from the old bespoke XAML are removed once the new view is in place.

## Tests

New class `NodeDetailViewModelTests` in `DialogEditor.Tests`:

```
Load_SetsHasContentTrue
Load_WithAllProperties_PopulatesAllFourGroups
Load_IdentityGroup_ContainsCorrectRows
Load_DisplayGroup_AlwaysContainsActorDirectionRow_EvenWhenEmpty
Load_LogicGroup_AlwaysContainsCommentsRow_EvenWhenEmpty
Load_VoiceGroup_AlwaysContainsAllThreeRows
Load_WithMultipleLinks_CreatesOneRowPerLink
Load_WithNoLinks_ProducesEmptyLinksList
Load_FemaleTextDisplay_WhenEmpty_ShowsSameAsDefaultString
Load_FemaleTextDisplay_WhenPresent_ShowsActualText
Clear_SetsHasContentFalse
Clear_DoesNotThrowWhenCalledBeforeLoad
```

The "AlwaysContains…EvenWhenEmpty" tests directly encode the always-visible requirement.

`NodeDetailViewModel` had no existing tests, so no existing tests become obsolete or require changes. The parser tests (`Poe1ConversationParserTests`, `Poe2ConversationParserTests`) assert on `ConversationNode` properties, which this change does not touch, and remain valid as-is.

## Files Changed

| File | Change |
|---|---|
| `DialogEditor.ViewModels/Models/PropertyRow.cs` | New |
| `DialogEditor.ViewModels/Models/PropertyGroup.cs` | New |
| `DialogEditor.ViewModels/Models/LinkRow.cs` | New |
| `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` | Rewrite |
| `DialogEditor.Avalonia/Views/NodeDetailView.axaml` | Rewrite |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | Add 19 keys, remove obsolete Label_* keys |
| `DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs` | New |
