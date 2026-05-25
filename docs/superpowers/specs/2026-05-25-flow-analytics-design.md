# Visual Flow Analytics — Design Spec

**Date:** 2026-05-25  
**Status:** Approved

---

## Overview

A non-modal Flow Analytics window that reports statistics and flags structural issues for the currently open conversation. Triggered on demand (Refresh button or any save operation). Clicking a flagged issue navigates the canvas to that node.

---

## Architecture

```
DialogEditor.Core/Analytics/
  FlowAnalysisModels.cs    — FlowStatistics, FlowIssue, FlowIssueKind, FlowAnalysisReport
  FlowAnalysisService.cs   — static Analyze(ConversationEditSnapshot) → FlowAnalysisReport

DialogEditor.ViewModels/ViewModels/
  FlowAnalyticsViewModel.cs — RefreshCommand, Issues list, Statistics, navigate callback

DialogEditor.Avalonia/Views/
  FlowAnalyticsWindow.axaml
  FlowAnalyticsWindow.axaml.cs

DialogEditor.Tests/Core/
  FlowAnalysisServiceTests.cs

DialogEditor.Tests/ViewModels/
  FlowAnalyticsViewModelTests.cs
```

The service lives in `DialogEditor.Core` because it depends only on `ConversationEditSnapshot` — no file I/O or provider access required. This keeps it purely functional and straightforward to test.

---

## Data Models

```csharp
public record FlowStatistics(
    int    TotalNodes,
    int    WordCount,
    int    MaxDepth,
    int    PlayerCount,
    int    NpcCount,
    int    NarratorCount,
    int    ScriptCount,
    double AvgLinksPerNode,
    int    ConditionalLinkCount,
    int    TotalLinkCount);

public enum FlowIssueKind
{
    Unreachable,      // node not reachable from root (NodeId 0)
    PlayerDeadEnd,    // player-choice node with no outgoing links
    EmptyText,        // non-script node where both DefaultText and FemaleText are blank
    NoIncomingLinks   // non-root node with no incoming links
}

public record FlowIssue(int NodeId, FlowIssueKind Kind);

public record FlowAnalysisReport(
    FlowStatistics           Statistics,
    IReadOnlyList<FlowIssue> Issues);
```

`FlowIssue` carries no `Description` string — the ViewModel derives the display label from `FlowIssueKind` via string resource lookup, and the node text snippet from the snapshot.

---

## Analysis Service

```csharp
public static class FlowAnalysisService
{
    public static FlowAnalysisReport Analyze(ConversationEditSnapshot snapshot) { ... }
}
```

**Algorithm:**

1. BFS from `NodeId == 0`, building:
   - `reachable` — set of all reachable node IDs (handles cycles via visited check)
   - `nodesWithIncoming` — set of all node IDs appearing as `ToNodeId` on any link
   - `depth[nodeId]` — BFS level for each reachable node

2. Single pass over all nodes to collect statistics and detect issues:
   - `TotalNodes` — count of all nodes
   - `WordCount` — sum of whitespace-delimited words in `DefaultText` across all nodes
   - `MaxDepth` — maximum value in `depth` map
   - `PlayerCount / NpcCount / NarratorCount / ScriptCount` — by `SpeakerCategory`
   - `TotalLinkCount` — sum of `node.Links.Count` across all nodes
   - `AvgLinksPerNode` — `TotalLinkCount / TotalNodes` (0.0 if no nodes)
   - `ConditionalLinkCount` — links where `HasConditions == true`

3. Issue detection:
   - **Unreachable** — node ID not in `reachable`
   - **PlayerDeadEnd** — `IsPlayerChoice == true` and `Links.Count == 0`
   - **EmptyText** — `SpeakerCategory != Script` and `DefaultText` is blank and `FemaleText` is blank
   - **NoIncomingLinks** — `NodeId != 0` and node ID not in `nodesWithIncoming`

Issues are sorted by `NodeId` ascending in the returned report.

---

## ViewModel

```csharp
public partial class FlowAnalyticsViewModel : ObservableObject
{
    private readonly Func<ConversationEditSnapshot?> _getSnapshot;
    private readonly Action<int>                     _navigateToNode;

    [ObservableProperty] private FlowStatistics?                         _statistics;
    [ObservableProperty] private ObservableCollection<FlowIssueViewModel> _issues = [];
    [ObservableProperty] private string                                   _statusText    = string.Empty;
    [ObservableProperty] private string                                   _lastAnalysed  = string.Empty;
    [ObservableProperty] private bool                                     _hasData;

    [RelayCommand]
    private void Refresh() { ... }
}
```

`_getSnapshot` is a `Func` so the VM always gets a fresh snapshot at refresh time (the conversation may have been edited since the window was opened).

`FlowIssueViewModel` wraps a `FlowIssue`, exposes `NodeId`, `KindLabel` (string resource lookup), `NodeSnippet` (truncated `DefaultText` from snapshot), and `NavigateCommand` which calls `_navigateToNode(NodeId)`.

---

## Navigation

`MainWindow.axaml.cs` provides the navigate callback when opening the window:

```csharp
var analyticsVm = new FlowAnalyticsViewModel(
    () => vm.Canvas.BuildSnapshot(),
    nodeId =>
    {
        var node = vm.Canvas.Nodes.FirstOrDefault(n => n.NodeId == nodeId);
        if (node != null) CanvasView.ScrollToNode(node);
    });
```

`ConversationView.ScrollToNode(NodeViewModel node)` is a new method that:
1. Sets `vm.Canvas.SelectedNode = node`
2. Scrolls the canvas `ScrollViewer` to bring the node into view

---

## Save Trigger

`MainWindowViewModel` gains a new `ConversationSaved` event (same pattern as `TestModeEntered`/`TestModeExited`). `MainWindow.axaml.cs` subscribes when the analytics window opens and calls `analyticsVm.Refresh()`. It unsubscribes when the window closes.

---

## Window Layout

Non-modal window, same `Show()` / `Activate()` / `Closed` pattern as `BatchReplaceWindow`.

```
┌─ Flow Analytics — conversation_name ───────────────────────────┐
│  STATISTICS                                                      │
│  Nodes  42     Words   1,204    Max depth  14                   │
│  Player 10     NPC     28       Narrator    3                   │
│  Script  1     Avg links/node  1.8    Conditional links 12(32%) │
│ ─────────────────────────────────────────────────────────────── │
│  ISSUES — 3 found                                               │
│  ▌ UNREACHABLE   Node 17 — "I've heard enough."         →       │
│  ▌ EMPTY TEXT    Node 31 — (NPC, no text)               →       │
│  ▌ DEAD END      Node 8  — "What do you know about…"   →       │
│                                                                  │
│  Last analysed: just now                       [⟳ Refresh]      │
└──────────────────────────────────────────────────────────────-─-┘
```

- Before first Refresh: show `FlowAnalytics_NoData` prompt instead of stats/issues
- Issue rows: red left-border for `Unreachable`, amber for all others
- `→` navigate button invokes `NavigateCommand`

---

## String Keys

```
FlowAnalytics_Title
FlowAnalytics_Statistics
FlowAnalytics_Issues              — "ISSUES — {0} found"
FlowAnalytics_NoIssues            — "No issues found"
FlowAnalytics_NoData              — "Open a conversation and click Refresh"
FlowAnalytics_LastAnalysed        — "Last analysed: {0}"
FlowAnalytics_Refresh

FlowAnalytics_Issue_Unreachable        — "Unreachable"
FlowAnalytics_Issue_PlayerDeadEnd      — "Dead end"
FlowAnalytics_Issue_EmptyText          — "Empty text"
FlowAnalytics_Issue_NoIncomingLinks    — "No incoming links"

FlowAnalytics_Stat_Nodes
FlowAnalytics_Stat_Words
FlowAnalytics_Stat_MaxDepth
FlowAnalytics_Stat_Player
FlowAnalytics_Stat_NPC
FlowAnalytics_Stat_Narrator
FlowAnalytics_Stat_Script
FlowAnalytics_Stat_AvgLinks
FlowAnalytics_Stat_ConditionalLinks

Menu_FlowAnalytics
ToolTip_FlowAnalytics
ToolTip_FlowAnalytics_Refresh
ToolTip_FlowAnalytics_Navigate
```

---

## Tests

### `FlowAnalysisServiceTests`

- `Analyze_EmptySnapshot_ReturnsZeroStats`
- `Analyze_ReachableNodes_NotFlagged`
- `Analyze_NodeNotReachableFromRoot_FlagsUnreachable`
- `Analyze_NpcDeadEnd_NotFlagged`
- `Analyze_PlayerDeadEnd_FlagsDeadEnd`
- `Analyze_EmptyTextOnNpcNode_FlagsEmptyText`
- `Analyze_EmptyTextOnScriptNode_NotFlagged`
- `Analyze_NodeWithNoIncomingLinks_Flagged`
- `Analyze_RootNode_NoIncomingLinks_NotFlagged`
- `Analyze_MaxDepth_CorrectForLinearChain`
- `Analyze_MaxDepth_CorrectForBranchingGraph`
- `Analyze_WordCount_SumsDefaultText`
- `Analyze_ConditionalLinks_CountedCorrectly`
- `Analyze_CycleInGraph_DoesNotInfiniteLoop`

### `FlowAnalyticsViewModelTests`

- `InitialState_StatisticsIsNull_IssuesEmpty`
- `Refresh_PopulatesStatisticsAndIssues`
- `Refresh_NoIssues_IssuesEmpty`
- `Navigate_CallsCallbackWithCorrectNodeId`

---

## TDD Order

1. `FlowAnalysisServiceTests` → `FlowAnalysisService`
2. `FlowAnalyticsViewModelTests` → `FlowAnalyticsViewModel`
3. Wire up Avalonia window, string keys, menu item, save trigger (no unit tests)
