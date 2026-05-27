# Barks System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make bark nodes visually distinct on the canvas (amber color scheme), add bark-specific warnings in the node detail panel, and surface those warnings as issues in Flow Analytics.

**Architecture:** A new `BarkConstants` class in Core holds the text-length threshold so both `FlowAnalysisService` and `NodeViewModel` share the same value. `NodeColorConverter` (a new `IMultiValueConverter`) takes `SpeakerCategory + DisplayType` and returns amber brushes for bark nodes. `NodeViewModel.BarkWarnings` and `NodeDetailViewModel.BarkWarnings` expose computed warning lists bound to a warning box in the detail panel. `FlowAnalysisService` emits two new `FlowIssueKind` values that the existing analytics window displays.

**Tech Stack:** C# 12, .NET 8, Avalonia UI 11, CommunityToolkit.Mvvm, xUnit, Nodify canvas library.

---

## File Map

| File | Action |
|------|--------|
| `DialogEditor.Core/Analytics/BarkConstants.cs` | **Create** — shared threshold constant |
| `DialogEditor.Core/Analytics/FlowAnalysisModels.cs` | **Modify** — two new `FlowIssueKind` values |
| `DialogEditor.Core/Analytics/FlowAnalysisService.cs` | **Modify** — bark issue checks in `Analyze()` |
| `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs` | **Modify** — `KindLabel` cases for bark kinds |
| `DialogEditor.Avalonia/Converters/FlowIssueKindToSeverityBrushConverter.cs` | **Modify** — explicit amber cases |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | **Modify** — four new string keys |
| `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs` | **Modify** — `IsBark`, `BarkWarnings` |
| `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` | **Modify** — merged `BarkWarnings`, notify on links |
| `DialogEditor.Avalonia/Views/NodeDetailView.axaml` | **Modify** — warning box below DisplayType ComboBox |
| `DialogEditor.Avalonia/Converters/NodeColorConverter.cs` | **Create** — `IMultiValueConverter` |
| `DialogEditor.Avalonia/App.axaml` | **Modify** — register `NodeColorConverter` |
| `DialogEditor.Avalonia/Views/ConversationView.axaml` | **Modify** — MultiBinding + speaker dot |
| `DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs` | **Modify** — five new bark issue tests |
| `DialogEditor.Tests/ViewModels/NodeViewModelTests.cs` | **Modify** — four new bark warning tests |
| `DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs` | **Modify** — two new bark child warning tests |

---

## Task 1: BarkConstants

**Files:**
- Create: `DialogEditor.Core/Analytics/BarkConstants.cs`

`BarkConstants` is a pure constant — no logic, no test needed.

- [ ] **Step 1: Create BarkConstants.cs**

```csharp
namespace DialogEditor.Core.Analytics;

public static class BarkConstants
{
    public const int TextLengthWarningThreshold = 150;
}
```

- [ ] **Step 2: Commit**

```bash
git add DialogEditor.Core/Analytics/BarkConstants.cs
git commit -m "feat: add BarkConstants.TextLengthWarningThreshold to Core"
```

---

## Task 2: FlowAnalysis — new issue kinds and service checks

**Files:**
- Modify: `DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs`
- Modify: `DialogEditor.Core/Analytics/FlowAnalysisModels.cs`
- Modify: `DialogEditor.Core/Analytics/FlowAnalysisService.cs`

- [ ] **Step 1: Update the MakeNode helper in FlowAnalysisServiceTests.cs to accept displayType**

Find the existing `MakeNode` helper (line 11) and replace it:

```csharp
private static NodeEditSnapshot MakeNode(
    int id,
    SpeakerCategory category   = SpeakerCategory.Npc,
    bool isPlayerChoice        = false,
    string defaultText         = "",
    string femaleText          = "",
    string displayType         = "Conversation",
    IReadOnlyList<LinkEditSnapshot>? links = null) =>
    new(id, isPlayerChoice, category,
        "", "", defaultText, femaleText,
        displayType, "None", "", "", "", false, false,
        links ?? [], [], []);
```

- [ ] **Step 2: Write the five failing tests — append to FlowAnalysisServiceTests.cs before the closing brace**

```csharp
// ── Issue: BarkTextTooLong ────────────────────────────────────────

[Fact]
public void Analyze_BarkNode_LongText_EmitsBarkTextTooLongIssue()
{
    var longText = new string('x', BarkConstants.TextLengthWarningThreshold + 1);
    var snapshot = Snapshot(
        MakeNode(0, links: [Link(0, 1)]),
        MakeNode(1, defaultText: longText, displayType: "Bark"));

    var report = FlowAnalysisService.Analyze(snapshot);

    Assert.Contains(report.Issues, i => i.Kind == FlowIssueKind.BarkTextTooLong && i.NodeId == 1);
}

[Fact]
public void Analyze_BarkNode_ShortText_NoBarkTextIssue()
{
    var shortText = new string('x', BarkConstants.TextLengthWarningThreshold);
    var snapshot = Snapshot(
        MakeNode(0, links: [Link(0, 1)]),
        MakeNode(1, defaultText: shortText, displayType: "Bark"));

    var report = FlowAnalysisService.Analyze(snapshot);

    Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.BarkTextTooLong));
}

[Fact]
public void Analyze_ConversationNode_LongText_NoIssue()
{
    var longText = new string('x', BarkConstants.TextLengthWarningThreshold + 1);
    var snapshot = Snapshot(
        MakeNode(0, links: [Link(0, 1)]),
        MakeNode(1, defaultText: longText, displayType: "Conversation"));

    var report = FlowAnalysisService.Analyze(snapshot);

    Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.BarkTextTooLong));
}

// ── Issue: BarkHasPlayerChoiceChild ──────────────────────────────

[Fact]
public void Analyze_BarkNode_WithPlayerChoiceChild_EmitsBarkHasPlayerChoiceChildIssue()
{
    var snapshot = Snapshot(
        MakeNode(0, links: [Link(0, 1)]),
        MakeNode(1, displayType: "Bark", links: [Link(1, 2)]),
        MakeNode(2, isPlayerChoice: true));

    var report = FlowAnalysisService.Analyze(snapshot);

    Assert.Contains(report.Issues,
        i => i.Kind == FlowIssueKind.BarkHasPlayerChoiceChild && i.NodeId == 1);
}

[Fact]
public void Analyze_BarkNode_WithNpcChild_NoBarkChildIssue()
{
    var snapshot = Snapshot(
        MakeNode(0, links: [Link(0, 1)]),
        MakeNode(1, displayType: "Bark", links: [Link(1, 2)]),
        MakeNode(2, SpeakerCategory.Npc));

    var report = FlowAnalysisService.Analyze(snapshot);

    Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.BarkHasPlayerChoiceChild));
}
```

- [ ] **Step 3: Run to verify tests fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~FlowAnalysisServiceTests"
```

Expected: five new tests fail with `FlowIssueKind` not containing the new members.

- [ ] **Step 4: Add the two new enum values to FlowAnalysisModels.cs**

Open `DialogEditor.Core/Analytics/FlowAnalysisModels.cs`. The current `FlowIssueKind` enum is:

```csharp
public enum FlowIssueKind
{
    Unreachable,
    PlayerDeadEnd,
    EmptyText,
    NoIncomingLinks
}
```

Replace it with:

```csharp
public enum FlowIssueKind
{
    Unreachable,
    PlayerDeadEnd,
    EmptyText,
    NoIncomingLinks,
    BarkTextTooLong,
    BarkHasPlayerChoiceChild
}
```

- [ ] **Step 5: Add bark checks to FlowAnalysisService.Analyze()**

Open `DialogEditor.Core/Analytics/FlowAnalysisService.cs`.

After the line `var linksByFrom = nodes.ToDictionary(n => n.NodeId, n => n.Links);` (line 17), add:

```csharp
var nodeById = nodes.ToDictionary(n => n.NodeId);
```

Then in the main issue-detection loop, after the existing four issue checks (after line 92 `if (node.NodeId != 0 ...)`), add:

```csharp
if (node.DisplayType == "Bark")
{
    if (node.DefaultText.Length > BarkConstants.TextLengthWarningThreshold)
        issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.BarkTextTooLong));

    foreach (var link in node.Links)
    {
        if (nodeById.TryGetValue(link.ToNodeId, out var target) && target.IsPlayerChoice)
        {
            issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.BarkHasPlayerChoiceChild));
            break;
        }
    }
}
```

Also add the `using` at the top of the file if not already present (it's in the same namespace so no import needed).

- [ ] **Step 6: Run to verify tests pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~FlowAnalysisServiceTests"
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Core/Analytics/FlowAnalysisModels.cs
git add DialogEditor.Core/Analytics/FlowAnalysisService.cs
git add DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs
git commit -m "feat: BarkTextTooLong and BarkHasPlayerChoiceChild flow issues"
```

---

## Task 3: FlowAnalytics display layer

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`
- Modify: `DialogEditor.Avalonia/Converters/FlowIssueKindToSeverityBrushConverter.cs`

- [ ] **Step 1: Add four string keys to Strings.axaml**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, find the block at line 513–516:

```xml
    <sys:String x:Key="FlowAnalytics_Issue_Unreachable">Unreachable</sys:String>
    <sys:String x:Key="FlowAnalytics_Issue_PlayerDeadEnd">Dead end</sys:String>
    <sys:String x:Key="FlowAnalytics_Issue_EmptyText">Empty text</sys:String>
    <sys:String x:Key="FlowAnalytics_Issue_NoIncomingLinks">No incoming links</sys:String>
```

Add two lines immediately after:

```xml
    <sys:String x:Key="FlowAnalytics_Issue_BarkTextTooLong">Bark text too long</sys:String>
    <sys:String x:Key="FlowAnalytics_Issue_BarkHasPlayerChoiceChild">Bark has player choice</sys:String>
```

Then find the bark option strings around line 328–329:

```xml
    <sys:String x:Key="Option_DisplayConversation">Conversation</sys:String>
    <sys:String x:Key="Option_DisplayBark">Bark</sys:String>
```

Add two warning message strings immediately after:

```xml
    <sys:String x:Key="Bark_Warning_TextTooLong">Bark text exceeds 150 characters and may be cut off in-game.</sys:String>
    <sys:String x:Key="Bark_Warning_PlayerChoiceChild">Bark nodes should not lead to player choice responses.</sys:String>
```

- [ ] **Step 2: Add KindLabel cases in FlowAnalyticsViewModel.cs**

Open `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`. The `KindLabel` switch (lines 18–25) currently reads:

```csharp
public string KindLabel => Kind switch
{
    FlowIssueKind.Unreachable      => Loc.Get("FlowAnalytics_Issue_Unreachable"),
    FlowIssueKind.PlayerDeadEnd    => Loc.Get("FlowAnalytics_Issue_PlayerDeadEnd"),
    FlowIssueKind.EmptyText        => Loc.Get("FlowAnalytics_Issue_EmptyText"),
    FlowIssueKind.NoIncomingLinks  => Loc.Get("FlowAnalytics_Issue_NoIncomingLinks"),
    _                              => Kind.ToString()
};
```

Replace with:

```csharp
public string KindLabel => Kind switch
{
    FlowIssueKind.Unreachable               => Loc.Get("FlowAnalytics_Issue_Unreachable"),
    FlowIssueKind.PlayerDeadEnd             => Loc.Get("FlowAnalytics_Issue_PlayerDeadEnd"),
    FlowIssueKind.EmptyText                 => Loc.Get("FlowAnalytics_Issue_EmptyText"),
    FlowIssueKind.NoIncomingLinks           => Loc.Get("FlowAnalytics_Issue_NoIncomingLinks"),
    FlowIssueKind.BarkTextTooLong           => Loc.Get("FlowAnalytics_Issue_BarkTextTooLong"),
    FlowIssueKind.BarkHasPlayerChoiceChild  => Loc.Get("FlowAnalytics_Issue_BarkHasPlayerChoiceChild"),
    _                                       => Kind.ToString()
};
```

- [ ] **Step 3: Update FlowIssueKindToSeverityBrushConverter.cs with explicit amber cases**

Open `DialogEditor.Avalonia/Converters/FlowIssueKindToSeverityBrushConverter.cs`. Replace the `Convert` method:

```csharp
public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    => value is FlowIssueKind kind && kind is FlowIssueKind.Unreachable ? Red : Amber;
```

With:

```csharp
public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    => value is FlowIssueKind kind
        ? kind switch
        {
            FlowIssueKind.Unreachable => Red,
            _                         => Amber
        }
        : Amber;
```

- [ ] **Step 4: Run the full test suite to verify no regressions**

```
dotnet test DialogEditor.Tests
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml
git add DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs
git add DialogEditor.Avalonia/Converters/FlowIssueKindToSeverityBrushConverter.cs
git commit -m "feat: FlowAnalytics display layer for bark issue kinds"
```

---

## Task 4: NodeViewModel — IsBark and BarkWarnings

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/NodeViewModelTests.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs`

- [ ] **Step 1: Update MakeNode helper in NodeViewModelTests.cs to accept displayType**

The existing `MakeNode` helper (line 13) hardcodes `DisplayType: "Conversation"`. Replace it:

```csharp
private static NodeViewModel MakeNode(
    int    id             = 1,
    string defaultText    = "Hello",
    string femaleText     = "",
    bool   isPlayerChoice = false,
    SpeakerCategory speakerCategory = SpeakerCategory.Npc,
    string speakerGuid   = "",
    string listenerGuid  = "",
    string displayType   = "Conversation")
{
    var node = new ConversationNode(
        NodeId: id,
        IsPlayerChoice: isPlayerChoice,
        SpeakerCategory: speakerCategory,
        SpeakerGuid: speakerGuid,
        ListenerGuid: listenerGuid,
        Links: [],
        Conditions: [],
        Scripts: [],
        DisplayType: displayType,
        Persistence: "None");
    return new NodeViewModel(node, new StringEntry(id, defaultText, femaleText));
}
```

- [ ] **Step 2: Write the four failing tests — append to NodeViewModelTests.cs before the closing brace**

```csharp
// ── IsBark ───────────────────────────────────────────────────────

[Fact]
public void IsBark_TrueWhenDisplayTypeBark()
{
    var vm = MakeNode(displayType: "Bark");
    Assert.True(vm.IsBark);
}

[Fact]
public void IsBark_FalseWhenDisplayTypeConversation()
{
    var vm = MakeNode(displayType: "Conversation");
    Assert.False(vm.IsBark);
}

// ── BarkWarnings ─────────────────────────────────────────────────

[Fact]
public void BarkWarnings_EmptyForConversationNode_EvenWithLongText()
{
    var longText = new string('x', BarkConstants.TextLengthWarningThreshold + 1);
    var vm = MakeNode(defaultText: longText, displayType: "Conversation");
    Assert.Empty(vm.BarkWarnings);
}

[Fact]
public void BarkWarnings_EmptyForShortBark()
{
    var shortText = new string('x', BarkConstants.TextLengthWarningThreshold);
    var vm = MakeNode(defaultText: shortText, displayType: "Bark");
    Assert.Empty(vm.BarkWarnings);
}

[Fact]
public void BarkWarnings_TextLengthWarning_WhenBarkTextExceedsThreshold()
{
    var longText = new string('x', BarkConstants.TextLengthWarningThreshold + 1);
    var vm = MakeNode(defaultText: longText, displayType: "Bark");
    Assert.Single(vm.BarkWarnings);
    Assert.Equal("Bark_Warning_TextTooLong", vm.BarkWarnings[0]);
}
```

Also add the using for `BarkConstants` at the top of the file if not already present:

```csharp
using DialogEditor.Core.Analytics;
```

- [ ] **Step 3: Run to verify tests fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeViewModelTests"
```

Expected: the five new tests fail with `NodeViewModel` not having `IsBark` or `BarkWarnings`.

- [ ] **Step 4: Add IsBark and BarkWarnings to NodeViewModel.cs**

Open `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs`.

Add the `using` at the top:

```csharp
using DialogEditor.Core.Analytics;
```

After the `HideSpeaker` property block (around line 122), add:

```csharp
public bool IsBark => _displayType == "Bark";

public IReadOnlyList<string> BarkWarnings
{
    get
    {
        if (!IsBark) return [];
        var warnings = new List<string>();
        if (_defaultText.Length > BarkConstants.TextLengthWarningThreshold)
            warnings.Add(Loc.Get("Bark_Warning_TextTooLong"));
        return warnings;
    }
}
```

- [ ] **Step 5: Notify IsBark and BarkWarnings when DisplayType changes**

In the `DisplayType` property setter's apply lambda (around line 78–79), it currently reads:

```csharp
v => { _displayType = v; OnPropertyChanged(nameof(DisplayType)); });
```

Replace with:

```csharp
v => { _displayType = v;
       OnPropertyChanged(nameof(DisplayType));
       OnPropertyChanged(nameof(IsBark));
       OnPropertyChanged(nameof(BarkWarnings)); });
```

- [ ] **Step 6: Notify BarkWarnings when DefaultText changes**

In the `DefaultText` property setter's apply lambda (around line 64–65), it currently reads:

```csharp
v => { _defaultText = v; OnPropertyChanged(nameof(DefaultText)); OnPropertyChanged(nameof(TextPreview)); });
```

Replace with:

```csharp
v => { _defaultText = v;
       OnPropertyChanged(nameof(DefaultText));
       OnPropertyChanged(nameof(TextPreview));
       OnPropertyChanged(nameof(BarkWarnings)); });
```

- [ ] **Step 7: Run to verify tests pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeViewModelTests"
```

Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/NodeViewModel.cs
git add DialogEditor.Tests/ViewModels/NodeViewModelTests.cs
git commit -m "feat: NodeViewModel.IsBark and BarkWarnings (text length)"
```

---

## Task 5: NodeDetailViewModel — merged BarkWarnings

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`

- [ ] **Step 1: Write the two failing tests — append to NodeDetailViewModelTests.cs before the closing brace**

The existing `MakeConn()` helper (around line 238) creates a connection with no target owner. Add an overload that accepts `isPlayerChoice`:

```csharp
private static ConnectionViewModel MakeConn(bool targetIsPlayerChoice = false)
{
    var src = new ConnectorViewModel();
    var tgt = new ConnectorViewModel();
    if (targetIsPlayerChoice)
    {
        var node = new ConversationNode(
            99, targetIsPlayerChoice, SpeakerCategory.Player,
            "", "", [], [], [], "Conversation", "None");
        tgt.Owner = new NodeViewModel(node, new StringEntry(99, "", ""));
    }
    return new ConnectionViewModel(src, tgt);
}
```

Then add the two tests:

```csharp
// ── BarkWarnings (player-choice child) ───────────────────────────

[Fact]
public void BarkWarnings_EmptyWhenNoPlayerChoiceChildren()
{
    _vm.Load(MakeNode(displayType: "Bark"));
    _vm.RefreshLinks([MakeConn(targetIsPlayerChoice: false)]);
    Assert.Empty(_vm.BarkWarnings);
}

[Fact]
public void BarkWarnings_PlayerChoiceWarning_WhenChildIsPlayerChoice()
{
    _vm.Load(MakeNode(displayType: "Bark"));
    _vm.RefreshLinks([MakeConn(targetIsPlayerChoice: true)]);
    Assert.Single(_vm.BarkWarnings);
    Assert.Equal("Bark_Warning_PlayerChoiceChild", _vm.BarkWarnings[0]);
}
```

Also add the using at the top if not already present:

```csharp
using DialogEditor.Core.Analytics;
```

- [ ] **Step 2: Run to verify tests fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeDetailViewModelTests"
```

Expected: the two new tests fail because `NodeDetailViewModel` has no `BarkWarnings`.

- [ ] **Step 3: Add BarkWarnings to NodeDetailViewModel.cs**

Open `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`.

After the `HideSpeaker` property block (around line 110), add:

```csharp
public IReadOnlyList<string> BarkWarnings
{
    get
    {
        var warnings = new List<string>(_node?.BarkWarnings ?? []);
        if (_node?.IsBark == true
            && _links.Any(l => l.Target.Owner?.IsPlayerChoice == true))
        {
            warnings.Add(Loc.Get("Bark_Warning_PlayerChoiceChild"));
        }
        return warnings;
    }
}
```

- [ ] **Step 4: Notify BarkWarnings in NotifyAllProxies()**

In `NotifyAllProxies()` (around line 225–249), add at the end of the method body:

```csharp
OnPropertyChanged(nameof(BarkWarnings));
```

- [ ] **Step 5: Notify BarkWarnings when links change**

The `RefreshLinks` method (around line 296) currently reads:

```csharp
public void RefreshLinks(IEnumerable<ConnectionViewModel> connections)
    => Links = connections.ToList();
```

Replace with:

```csharp
public void RefreshLinks(IEnumerable<ConnectionViewModel> connections)
{
    Links = connections.ToList();
    OnPropertyChanged(nameof(BarkWarnings));
}
```

- [ ] **Step 6: Run to verify tests pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeDetailViewModelTests"
```

Expected: all tests pass.

- [ ] **Step 7: Run full test suite**

```
dotnet test DialogEditor.Tests
```

Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs
git add DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs
git commit -m "feat: NodeDetailViewModel.BarkWarnings (merged text length + player-choice child)"
```

---

## Task 6: Warning UI in NodeDetailView.axaml

**Files:**
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml`

No automated tests — verified by running the app and toggling DisplayType to "Bark" on a long-text node or one with player-choice children.

- [ ] **Step 1: Add the warning box below the DisplayType ComboBox**

In `DialogEditor.Avalonia/Views/NodeDetailView.axaml`, find the DisplayType block (around line 128–132):

```xml
<TextBlock Classes="field-label" Text="{StaticResource PropertyRow_DisplayType}"/>
<ComboBox Classes="detail-combo"
          ItemsSource="{x:Static vm:NodeDetailViewModel.DisplayTypeOptions}"
          SelectedItem="{Binding DisplayType, Mode=TwoWay}"
          ToolTip.Tip="{StaticResource ToolTip_DisplayType}"/>
```

Insert the warning box immediately after the ComboBox:

```xml
<Border Background="#2A2000" BorderBrush="#7A5C00" BorderThickness="1"
        CornerRadius="2" Padding="6,4" Margin="0,0,0,4"
        IsVisible="{Binding BarkWarnings.Count, Converter={StaticResource CountToVis}}"
        ToolTip.Tip="{StaticResource ToolTip_BarkWarnings}">
    <ItemsControl ItemsSource="{Binding BarkWarnings}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <TextBlock Text="{Binding}" Foreground="#E8C050"
                           FontSize="10" TextWrapping="Wrap"/>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</Border>
```

- [ ] **Step 2: Add ToolTip_BarkWarnings string to Strings.axaml**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, find the `ToolTip_DisplayType` entry and add immediately after it:

```xml
    <sys:String x:Key="ToolTip_BarkWarnings">Bark-specific validation warnings. These do not block saving — they help writers avoid common mistakes.</sys:String>
```

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/Views/NodeDetailView.axaml
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: bark warning box in node detail panel"
```

---

## Task 7: NodeColorConverter

**Files:**
- Create: `DialogEditor.Avalonia/Converters/NodeColorConverter.cs`
- Modify: `DialogEditor.Avalonia/App.axaml`

`NodeColorConverter` is an Avalonia `IMultiValueConverter` — the test project does not reference Avalonia, so it is verified by running the app (Task 8).

- [ ] **Step 1: Create NodeColorConverter.cs**

```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// IMultiValueConverter — values[0] = SpeakerCategory, values[1] = DisplayType string.
/// ConverterParameter: "body" = card body, "footer" = card footer, omit = header.
/// Returns amber tones when DisplayType is "Bark"; speaker-category tones otherwise.
/// </summary>
public sealed class NodeColorConverter : IMultiValueConverter
{
    // Bark palette
    private static readonly ISolidColorBrush BarkHeader = new SolidColorBrush(Color.FromRgb(0x7A, 0x5C, 0x00));
    private static readonly ISolidColorBrush BarkBody   = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xDC));
    private static readonly ISolidColorBrush BarkFooter = new SolidColorBrush(Color.FromRgb(0xE8, 0xD0, 0x80));

    // Conversation palette — mirrors SpeakerCategoryToBrushConverter
    private static readonly ISolidColorBrush NpcHeader      = new SolidColorBrush(Color.FromRgb(0x7b, 0x24, 0x1c));
    private static readonly ISolidColorBrush PlayerHeader   = new SolidColorBrush(Color.FromRgb(0x1a, 0x52, 0x76));
    private static readonly ISolidColorBrush NarratorHeader = new SolidColorBrush(Color.FromRgb(0x0e, 0x66, 0x55));
    private static readonly ISolidColorBrush ScriptHeader   = new SolidColorBrush(Color.FromRgb(0x2c, 0x3e, 0x50));

    private static readonly ISolidColorBrush NpcBody      = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xD0));
    private static readonly ISolidColorBrush PlayerBody   = new SolidColorBrush(Color.FromRgb(0xD5, 0xE8, 0xF5));
    private static readonly ISolidColorBrush NarratorBody = new SolidColorBrush(Color.FromRgb(0xD5, 0xF0, 0xE8));
    private static readonly ISolidColorBrush ScriptBody   = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    private static readonly ISolidColorBrush NpcFooter      = new SolidColorBrush(Color.FromRgb(0xE8, 0xE0, 0xB0));
    private static readonly ISolidColorBrush PlayerFooter   = new SolidColorBrush(Color.FromRgb(0xB0, 0xCD, 0xE8));
    private static readonly ISolidColorBrush NarratorFooter = new SolidColorBrush(Color.FromRgb(0xB0, 0xE0, 0xD5));
    private static readonly ISolidColorBrush ScriptFooter   = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var cat         = values.Count > 0 && values[0] is SpeakerCategory c ? c : SpeakerCategory.Npc;
        var displayType = values.Count > 1 ? values[1] as string ?? string.Empty : string.Empty;
        var zone        = parameter as string;

        if (displayType == "Bark")
        {
            return zone switch
            {
                "body"   => BarkBody,
                "footer" => BarkFooter,
                _        => BarkHeader
            };
        }

        return zone switch
        {
            "body" => cat switch
            {
                SpeakerCategory.Player   => PlayerBody,
                SpeakerCategory.Narrator => NarratorBody,
                SpeakerCategory.Script   => ScriptBody,
                _                        => NpcBody
            },
            "footer" => cat switch
            {
                SpeakerCategory.Player   => PlayerFooter,
                SpeakerCategory.Narrator => NarratorFooter,
                SpeakerCategory.Script   => ScriptFooter,
                _                        => NpcFooter
            },
            _ => cat switch
            {
                SpeakerCategory.Player   => PlayerHeader,
                SpeakerCategory.Narrator => NarratorHeader,
                SpeakerCategory.Script   => ScriptHeader,
                _                        => NpcHeader
            }
        };
    }
}
```

- [ ] **Step 2: Register NodeColorConverter in App.axaml**

In `DialogEditor.Avalonia/App.axaml`, find the existing converter registrations (around line 86–98) and add after `<converters:SpeakerCategoryToBrushConverter x:Key="SpeakerCategoryToBrush"/>`:

```xml
            <converters:NodeColorConverter x:Key="NodeColorConverter"/>
```

- [ ] **Step 3: Run full test suite to verify no regressions**

```
dotnet test DialogEditor.Tests
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Converters/NodeColorConverter.cs
git add DialogEditor.Avalonia/App.axaml
git commit -m "feat: NodeColorConverter — amber palette for bark nodes"
```

---

## Task 8: ConversationView.axaml — wire NodeColorConverter and speaker dot

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml`

No automated tests — verified by running the app and confirming bark nodes show amber and conversation nodes show their speaker colors.

- [ ] **Step 1: Replace the node template's three color bindings with NodeColorConverter MultiBindings, and add the speaker-identity dot**

In `DialogEditor.Avalonia/Views/ConversationView.axaml`, the node content template starts at line 141. Replace the entire `<Grid Width="200" ...>` block:

```xml
<Grid Width="200" RowDefinitions="Auto,Auto,Auto"
      Opacity="{Binding IsSearchMatch, Converter={StaticResource SearchMatchOpacity}}">
    <Border Grid.Row="0"
            Background="{Binding SpeakerCategory, Converter={StaticResource SpeakerCategoryToBrush}}"
            CornerRadius="2,2,0,0">
        <TextBlock Text="{Binding Title}"
                   Foreground="White" FontWeight="Bold"
                   FontSize="10" Padding="6,3"
                   TextTrimming="CharacterEllipsis"/>
    </Border>
    <TextBlock Grid.Row="1"
               Text="{Binding TextPreview}"
               TextWrapping="Wrap"
               Padding="6,4" FontSize="10"
               Foreground="#333" MaxHeight="70"
               Background="{Binding SpeakerCategory,
                   Converter={StaticResource SpeakerCategoryToBrush},
                   ConverterParameter=body}"/>
    <Border Grid.Row="2" Padding="6,2"
            Background="{Binding SpeakerCategory,
                Converter={StaticResource SpeakerCategoryToBrush},
                ConverterParameter=footer}">
        <TextBlock Text="{Binding FooterText}" FontSize="9" Foreground="#666"/>
    </Border>
</Grid>
```

With:

```xml
<Grid Width="200" RowDefinitions="Auto,Auto,Auto"
      Opacity="{Binding IsSearchMatch, Converter={StaticResource SearchMatchOpacity}}">

    <!-- Header — amber when Bark, speaker-color when Conversation -->
    <Border Grid.Row="0" CornerRadius="2,2,0,0">
        <Border.Background>
            <MultiBinding Converter="{StaticResource NodeColorConverter}">
                <Binding Path="SpeakerCategory"/>
                <Binding Path="DisplayType"/>
            </MultiBinding>
        </Border.Background>
        <StackPanel Orientation="Horizontal">
            <!-- Speaker-identity dot: always present, only informative for bark nodes -->
            <Ellipse Width="6" Height="6" Margin="6,0,4,0" VerticalAlignment="Center"
                     Fill="{Binding SpeakerCategory, Converter={StaticResource SpeakerCategoryToBrush}}"
                     ToolTip.Tip="{Binding SpeakerCategory}"/>
            <TextBlock Text="{Binding Title}"
                       Foreground="White" FontWeight="Bold"
                       FontSize="10" Padding="0,3,6,3"
                       TextTrimming="CharacterEllipsis"/>
        </StackPanel>
    </Border>

    <!-- Body -->
    <Border Grid.Row="1">
        <Border.Background>
            <MultiBinding Converter="{StaticResource NodeColorConverter}" ConverterParameter="body">
                <Binding Path="SpeakerCategory"/>
                <Binding Path="DisplayType"/>
            </MultiBinding>
        </Border.Background>
        <TextBlock Text="{Binding TextPreview}"
                   TextWrapping="Wrap"
                   Padding="6,4" FontSize="10"
                   Foreground="#333" MaxHeight="70"/>
    </Border>

    <!-- Footer -->
    <Border Grid.Row="2" Padding="6,2">
        <Border.Background>
            <MultiBinding Converter="{StaticResource NodeColorConverter}" ConverterParameter="footer">
                <Binding Path="SpeakerCategory"/>
                <Binding Path="DisplayType"/>
            </MultiBinding>
        </Border.Background>
        <TextBlock Text="{Binding FooterText}" FontSize="9" Foreground="#666"/>
    </Border>

</Grid>
```

- [ ] **Step 2: Run full test suite**

```
dotnet test DialogEditor.Tests
```

Expected: all tests pass.

- [ ] **Step 3: Build and run the app**

```
dotnet run --project DialogEditor.Avalonia
```

Verify visually:
- Open a conversation with bark nodes (`DisplayType = "Bark"`). Their canvas cards should show dark gold headers, warm cream bodies, amber footers, and a small speaker-category dot in the header.
- Conversation nodes should look exactly as before.
- Selecting a bark node and opening the detail panel: if the text exceeds 150 characters, the amber warning box appears below the DisplayType ComboBox.
- Run Flow Analytics: bark nodes with long text or player-choice children appear in the issues list with the correct label and amber severity bar.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConversationView.axaml
git commit -m "feat: bark nodes render with amber color scheme on canvas"
```
