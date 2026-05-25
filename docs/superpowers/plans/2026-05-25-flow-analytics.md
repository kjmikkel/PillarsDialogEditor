# Visual Flow Analytics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a non-modal Flow Analytics window that reports statistics and flags structural issues for the currently open conversation, with click-to-navigate to flagged nodes.

**Architecture:** A pure static `FlowAnalysisService` in `DialogEditor.Core/Analytics/` performs BFS traversal on a `ConversationEditSnapshot` and returns a `FlowAnalysisReport` (statistics + issue list). A `FlowAnalyticsViewModel` in `DialogEditor.ViewModels` wraps the report, exposes a `RefreshCommand`, and calls a navigate callback when the user clicks an issue. The Avalonia window is wired into `MainWindow` following the same non-modal singleton pattern as `BatchReplaceWindow`.

**Tech Stack:** C# / .NET 8, CommunityToolkit.Mvvm (ObservableObject, RelayCommand, ObservableProperty), Avalonia UI, xUnit

---

## File Map

| Action | File |
|--------|------|
| **Create** | `DialogEditor.Core/Analytics/FlowAnalysisModels.cs` |
| **Create** | `DialogEditor.Core/Analytics/FlowAnalysisService.cs` |
| **Create** | `DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs` |
| **Create** | `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs` |
| **Create** | `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs` |
| **Modify** | `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` — add `ConversationSaved` event, fire in `SaveProject()` |
| **Modify** | `DialogEditor.Avalonia/Views/ConversationView.axaml.cs` — add `ScrollToNode(NodeViewModel)` |
| **Create** | `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml` |
| **Create** | `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml.cs` |
| **Modify** | `DialogEditor.Avalonia/Resources/Strings.axaml` — add analytics string keys |
| **Modify** | `DialogEditor.Avalonia/Views/MainWindow.axaml` — add menu item |
| **Modify** | `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` — add window field, click handler, save subscription |

---

## Task 1: Core Models

**Files:**
- Create: `DialogEditor.Core/Analytics/FlowAnalysisModels.cs`

- [ ] **Step 1: Create the models file**

```csharp
// DialogEditor.Core/Analytics/FlowAnalysisModels.cs
namespace DialogEditor.Core.Analytics;

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
    Unreachable,
    PlayerDeadEnd,
    EmptyText,
    NoIncomingLinks
}

public record FlowIssue(int NodeId, FlowIssueKind Kind);

public record FlowAnalysisReport(
    FlowStatistics           Statistics,
    IReadOnlyList<FlowIssue> Issues);
```

- [ ] **Step 2: Build to verify it compiles**

```
dotnet build DialogEditor.Core --no-restore -v q
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Core/Analytics/FlowAnalysisModels.cs
git commit -m "feat: add FlowAnalysis core models"
```

---

## Task 2: Analysis Service — Statistics and Reachability

**Files:**
- Create: `DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs`
- Create: `DialogEditor.Core/Analytics/FlowAnalysisService.cs`

The service does one BFS from `NodeId == 0` to track reachable nodes and BFS depth, then one pass over all nodes to accumulate statistics and detect issues.

- [ ] **Step 1: Create the test file with stat and reachability tests**

```csharp
// DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Analytics;

public class FlowAnalysisServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static NodeEditSnapshot MakeNode(
        int id,
        SpeakerCategory category   = SpeakerCategory.Npc,
        bool isPlayerChoice        = false,
        string defaultText         = "",
        string femaleText          = "",
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, isPlayerChoice, category,
            "", "", defaultText, femaleText,
            "Conversation", "None", "", "", "", false, false,
            links ?? [], [], []);

    private static LinkEditSnapshot Link(int from, int to, bool hasConditions = false) =>
        new(from, to, 1f, "", hasConditions);

    private static ConversationEditSnapshot Snapshot(params NodeEditSnapshot[] nodes) =>
        new(nodes);

    // ── Statistics ────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_EmptySnapshot_ReturnsZeroStats()
    {
        var report = FlowAnalysisService.Analyze(Snapshot());

        Assert.Equal(0, report.Statistics.TotalNodes);
        Assert.Equal(0, report.Statistics.WordCount);
        Assert.Equal(0, report.Statistics.MaxDepth);
        Assert.Equal(0.0, report.Statistics.AvgLinksPerNode);
    }

    [Fact]
    public void Analyze_WordCount_SumsDefaultText()
    {
        var snapshot = Snapshot(
            MakeNode(0, defaultText: "Hello world"),
            MakeNode(1, defaultText: "Three words here"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(7, report.Statistics.WordCount);
    }

    [Fact]
    public void Analyze_NodeTypeCounts_CorrectBySpeakerCategory()
    {
        var snapshot = Snapshot(
            MakeNode(0, SpeakerCategory.Npc),
            MakeNode(1, SpeakerCategory.Player),
            MakeNode(2, SpeakerCategory.Narrator),
            MakeNode(3, SpeakerCategory.Script));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(1, report.Statistics.NpcCount);
        Assert.Equal(1, report.Statistics.PlayerCount);
        Assert.Equal(1, report.Statistics.NarratorCount);
        Assert.Equal(1, report.Statistics.ScriptCount);
    }

    [Fact]
    public void Analyze_MaxDepth_CorrectForLinearChain()
    {
        // 0 → 1 → 2 → 3
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, links: [Link(1, 2)]),
            MakeNode(2, links: [Link(2, 3)]),
            MakeNode(3));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(3, report.Statistics.MaxDepth);
    }

    [Fact]
    public void Analyze_MaxDepth_CorrectForBranchingGraph()
    {
        // 0 → 1 → 3
        // 0 → 2
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1), Link(0, 2)]),
            MakeNode(1, links: [Link(1, 3)]),
            MakeNode(2),
            MakeNode(3));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(2, report.Statistics.MaxDepth);
    }

    [Fact]
    public void Analyze_AvgLinksPerNode_CorrectForMixedGraph()
    {
        // 0 has 2 links, 1 has 1 link, 2 has 0 links → total 3 links / 3 nodes = 1.0
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1), Link(0, 2)]),
            MakeNode(1, links: [Link(1, 2)]),
            MakeNode(2));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(1.0, report.Statistics.AvgLinksPerNode, precision: 5);
        Assert.Equal(3, report.Statistics.TotalLinkCount);
    }

    [Fact]
    public void Analyze_ConditionalLinks_CountedCorrectly()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1, hasConditions: true), Link(0, 2, hasConditions: false)]),
            MakeNode(1),
            MakeNode(2));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(1, report.Statistics.ConditionalLinkCount);
        Assert.Equal(2, report.Statistics.TotalLinkCount);
    }

    // ── Reachability ──────────────────────────────────────────────────────

    [Fact]
    public void Analyze_AllNodesReachable_NoUnreachableIssues()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.Unreachable));
    }

    [Fact]
    public void Analyze_NodeNotReachableFromRoot_FlagsUnreachable()
    {
        // Node 2 is not linked from anywhere
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1),
            MakeNode(2));

        var report = FlowAnalysisService.Analyze(snapshot);

        var issue = Assert.Single(report.Issues.Where(i => i.Kind == FlowIssueKind.Unreachable));
        Assert.Equal(2, issue.NodeId);
    }

    [Fact]
    public void Analyze_CycleInGraph_DoesNotInfiniteLoop()
    {
        // 0 → 1 → 2 → 1 (cycle)
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, links: [Link(1, 2)]),
            MakeNode(2, links: [Link(2, 1)]));

        // Should complete without hanging
        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.Unreachable));
    }
}
```

- [ ] **Step 2: Run to verify tests fail**

```
dotnet test DialogEditor.Tests --no-build --filter "FullyQualifiedName~FlowAnalysisServiceTests" -v q
```
Expected: build error — `FlowAnalysisService` does not exist yet.

- [ ] **Step 3: Create the service with a stub that compiles**

```csharp
// DialogEditor.Core/Analytics/FlowAnalysisService.cs
using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Analytics;

public static class FlowAnalysisService
{
    public static FlowAnalysisReport Analyze(ConversationEditSnapshot snapshot)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 4: Build and run to confirm tests now fail (not error)**

```
dotnet build DialogEditor.Core --no-restore -v q
dotnet test DialogEditor.Tests --no-build --filter "FullyQualifiedName~FlowAnalysisServiceTests" -v q
```
Expected: tests FAIL with `NotImplementedException`.

- [ ] **Step 5: Implement the full service**

```csharp
// DialogEditor.Core/Analytics/FlowAnalysisService.cs
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Analytics;

public static class FlowAnalysisService
{
    public static FlowAnalysisReport Analyze(ConversationEditSnapshot snapshot)
    {
        var nodes = snapshot.Nodes;
        if (nodes.Count == 0)
            return new FlowAnalysisReport(
                new FlowStatistics(0, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
                []);

        // ── Build adjacency and incoming-link sets ────────────────────────
        var linksByFrom    = nodes.ToDictionary(n => n.NodeId, n => n.Links);
        var nodesWithIncoming = new HashSet<int>();
        var totalLinks     = 0;
        var conditionalLinks = 0;

        foreach (var node in nodes)
        {
            foreach (var link in node.Links)
            {
                nodesWithIncoming.Add(link.ToNodeId);
                totalLinks++;
                if (link.HasConditions) conditionalLinks++;
            }
        }

        // ── BFS from root (NodeId 0) ──────────────────────────────────────
        var reachable = new HashSet<int>();
        var depth     = new Dictionary<int, int>();
        var queue     = new Queue<int>();

        if (linksByFrom.ContainsKey(0) || nodes.Any(n => n.NodeId == 0))
        {
            queue.Enqueue(0);
            reachable.Add(0);
            depth[0] = 0;
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!linksByFrom.TryGetValue(current, out var links)) continue;
            foreach (var link in links)
            {
                if (reachable.Add(link.ToNodeId))
                {
                    depth[link.ToNodeId] = depth[current] + 1;
                    queue.Enqueue(link.ToNodeId);
                }
            }
        }

        // ── Single pass: statistics + issues ─────────────────────────────
        var issues   = new List<FlowIssue>();
        var wordCount     = 0;
        var playerCount   = 0;
        var npcCount      = 0;
        var narratorCount = 0;
        var scriptCount   = 0;

        foreach (var node in nodes)
        {
            // Statistics
            if (!string.IsNullOrEmpty(node.DefaultText))
                wordCount += node.DefaultText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            switch (node.SpeakerCategory)
            {
                case SpeakerCategory.Player:   playerCount++;   break;
                case SpeakerCategory.Npc:      npcCount++;      break;
                case SpeakerCategory.Narrator: narratorCount++; break;
                case SpeakerCategory.Script:   scriptCount++;   break;
            }

            // Issues
            if (!reachable.Contains(node.NodeId))
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.Unreachable));

            if (node.IsPlayerChoice && node.Links.Count == 0)
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.PlayerDeadEnd));

            if (node.SpeakerCategory != SpeakerCategory.Script
                && string.IsNullOrWhiteSpace(node.DefaultText)
                && string.IsNullOrWhiteSpace(node.FemaleText))
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.EmptyText));

            if (node.NodeId != 0 && !nodesWithIncoming.Contains(node.NodeId))
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.NoIncomingLinks));
        }

        var maxDepth = depth.Count > 0 ? depth.Values.Max() : 0;
        var avgLinks = nodes.Count > 0 ? (double)totalLinks / nodes.Count : 0.0;

        var stats = new FlowStatistics(
            TotalNodes:          nodes.Count,
            WordCount:           wordCount,
            MaxDepth:            maxDepth,
            PlayerCount:         playerCount,
            NpcCount:            npcCount,
            NarratorCount:       narratorCount,
            ScriptCount:         scriptCount,
            AvgLinksPerNode:     avgLinks,
            ConditionalLinkCount: conditionalLinks,
            TotalLinkCount:      totalLinks);

        issues.Sort((a, b) => a.NodeId.CompareTo(b.NodeId));
        return new FlowAnalysisReport(stats, issues);
    }
}
```

- [ ] **Step 6: Run stat and reachability tests**

```
dotnet test DialogEditor.Tests --no-build --filter "FullyQualifiedName~FlowAnalysisServiceTests" -v q
```
Expected: all tests in this file PASS.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Core/Analytics/FlowAnalysisService.cs
git add DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs
git commit -m "feat: add FlowAnalysisService with stats and reachability"
```

---

## Task 3: Analysis Service — Issue Detection Tests

**Files:**
- Modify: `DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs`

Add the remaining issue-detection tests to the same file.

- [ ] **Step 1: Add issue detection tests**

Append these test methods inside the `FlowAnalysisServiceTests` class (after the reachability tests):

```csharp
    // ── Issue: PlayerDeadEnd ──────────────────────────────────────────────

    [Fact]
    public void Analyze_NpcDeadEnd_NotFlagged()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Npc)); // NPC with no outgoing links — intentional end

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.PlayerDeadEnd));
    }

    [Fact]
    public void Analyze_PlayerDeadEnd_FlagsDeadEnd()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Player, isPlayerChoice: true)); // no outgoing links

        var report = FlowAnalysisService.Analyze(snapshot);

        var issue = Assert.Single(report.Issues.Where(i => i.Kind == FlowIssueKind.PlayerDeadEnd));
        Assert.Equal(1, issue.NodeId);
    }

    // ── Issue: EmptyText ──────────────────────────────────────────────────

    [Fact]
    public void Analyze_EmptyTextOnNpcNode_FlagsEmptyText()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Npc, defaultText: "", femaleText: ""));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Contains(report.Issues, i => i.Kind == FlowIssueKind.EmptyText && i.NodeId == 1);
    }

    [Fact]
    public void Analyze_EmptyTextOnScriptNode_NotFlagged()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Script, defaultText: "", femaleText: ""));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.EmptyText));
    }

    [Fact]
    public void Analyze_NonEmptyTextOnNpcNode_NotFlagged()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Npc, defaultText: "Hello"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.EmptyText && i.NodeId == 1));
    }

    // ── Issue: NoIncomingLinks ────────────────────────────────────────────

    [Fact]
    public void Analyze_NodeWithNoIncomingLinks_Flagged()
    {
        // Node 2 has no incoming links (neither 0 nor 1 points to it)
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1),
            MakeNode(2, links: [Link(2, 1)])); // has outgoing but no incoming

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Contains(report.Issues, i => i.Kind == FlowIssueKind.NoIncomingLinks && i.NodeId == 2);
    }

    [Fact]
    public void Analyze_RootNode_NoIncomingLinks_NotFlagged()
    {
        var snapshot = Snapshot(MakeNode(0));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.NoIncomingLinks));
    }

    // ── Issue ordering ────────────────────────────────────────────────────

    [Fact]
    public void Analyze_Issues_SortedByNodeId()
    {
        var snapshot = Snapshot(
            MakeNode(0),   // unreachable: none (it's root); empty text: yes
            MakeNode(5),   // unreachable: yes (not linked from root)
            MakeNode(3));  // unreachable: yes; no incoming: yes

        var report = FlowAnalysisService.Analyze(snapshot);

        var ids = report.Issues.Select(i => i.NodeId).ToList();
        Assert.Equal(ids.OrderBy(x => x).ToList(), ids);
    }
```

- [ ] **Step 2: Run all service tests**

```
dotnet test DialogEditor.Tests --no-build --filter "FullyQualifiedName~FlowAnalysisServiceTests" -v q
```
Expected: all tests PASS.

- [ ] **Step 3: Run full suite to confirm no regressions**

```
dotnet test DialogEditor.Tests --no-build -v q
```
Expected: all tests PASS.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs
git commit -m "test: add issue-detection tests for FlowAnalysisService"
```

---

## Task 4: Flow Analytics ViewModel

**Files:**
- Create: `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs`
- Create: `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`

- [ ] **Step 1: Write the failing VM tests**

```csharp
// DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.ViewModels;

public class FlowAnalyticsViewModelTests
{
    private static NodeEditSnapshot MakeNode(
        int id,
        SpeakerCategory category = SpeakerCategory.Npc,
        string defaultText = "",
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, false, category, "", "", defaultText, "",
            "Conversation", "None", "", "", "", false, false,
            links ?? [], [], []);

    private static LinkEditSnapshot Link(int from, int to) =>
        new(from, to, 1f, "", false);

    private static ConversationEditSnapshot SimpleSnapshot() => new([
        MakeNode(0, defaultText: "Hello", links: [Link(0, 1)]),
        MakeNode(1, defaultText: "World")
    ]);

    [Fact]
    public void InitialState_StatisticsIsNull_IssuesEmpty()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });

        Assert.Null(vm.Statistics);
        Assert.Empty(vm.Issues);
    }

    [Fact]
    public void Refresh_PopulatesStatisticsAndIssues()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.NotNull(vm.Statistics);
        Assert.Equal(2, vm.Statistics!.TotalNodes);
    }

    [Fact]
    public void Refresh_NoIssues_IssuesEmpty()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.Empty(vm.Issues);
    }

    [Fact]
    public void Refresh_WithIssues_PopulatesIssueViewModels()
    {
        // Node 2 is unreachable
        var snapshot = new ConversationEditSnapshot([
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1),
            MakeNode(2)
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.Single(vm.Issues);
        Assert.Equal(2, vm.Issues[0].NodeId);
        Assert.Equal(FlowIssueKind.Unreachable, vm.Issues[0].Kind);
    }

    [Fact]
    public void Refresh_NullSnapshot_DoesNotCrash()
    {
        var vm = new FlowAnalyticsViewModel(() => null, _ => { });

        vm.RefreshCommand.Execute(null); // should not throw
    }

    [Fact]
    public void Navigate_CallsCallbackWithCorrectNodeId()
    {
        var navigatedId = -1;
        var snapshot = new ConversationEditSnapshot([
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1),
            MakeNode(2)  // unreachable
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, id => navigatedId = id);
        vm.RefreshCommand.Execute(null);

        vm.Issues[0].NavigateCommand.Execute(null);

        Assert.Equal(2, navigatedId);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests --no-build --filter "FullyQualifiedName~FlowAnalyticsViewModelTests" -v q
```
Expected: build error — `FlowAnalyticsViewModel` not yet defined.

- [ ] **Step 3: Implement the ViewModel**

```csharp
// DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

public partial class FlowIssueViewModel : ObservableObject
{
    private readonly Action<int> _navigate;

    public int           NodeId      { get; }
    public FlowIssueKind Kind        { get; }
    public string        NodeSnippet { get; }

    public FlowIssueViewModel(FlowIssue issue, string nodeSnippet, Action<int> navigate)
    {
        NodeId      = issue.NodeId;
        Kind        = issue.Kind;
        NodeSnippet = nodeSnippet;
        _navigate   = navigate;
    }

    [RelayCommand]
    private void Navigate() => _navigate(NodeId);
}

public partial class FlowAnalyticsViewModel : ObservableObject
{
    private readonly Func<ConversationEditSnapshot?> _getSnapshot;
    private readonly Action<int>                     _navigateToNode;

    [ObservableProperty] private FlowStatistics? _statistics;
    [ObservableProperty] private string          _statusText   = string.Empty;
    [ObservableProperty] private string          _lastAnalysed = string.Empty;
    [ObservableProperty] private bool            _hasData;

    public ObservableCollection<FlowIssueViewModel> Issues { get; } = [];

    public FlowAnalyticsViewModel(
        Func<ConversationEditSnapshot?> getSnapshot,
        Action<int>                     navigateToNode)
    {
        _getSnapshot    = getSnapshot;
        _navigateToNode = navigateToNode;
    }

    [RelayCommand]
    private void Refresh()
    {
        var snapshot = _getSnapshot();
        if (snapshot is null)
        {
            StatusText = Loc.Get("FlowAnalytics_NoData");
            return;
        }

        var report = FlowAnalysisService.Analyze(snapshot);

        Statistics = report.Statistics;
        HasData    = true;

        Issues.Clear();
        foreach (var issue in report.Issues)
        {
            var node    = snapshot.Nodes.FirstOrDefault(n => n.NodeId == issue.NodeId);
            var snippet = node is not null && !string.IsNullOrWhiteSpace(node.DefaultText)
                ? Truncate(node.DefaultText, 60)
                : $"({node?.SpeakerCategory.ToString().ToLower() ?? "unknown"}, no text)";
            Issues.Add(new FlowIssueViewModel(issue, snippet, _navigateToNode));
        }

        var issueCount = report.Issues.Count;
        StatusText  = issueCount > 0
            ? Loc.Format("FlowAnalytics_Issues", issueCount)
            : Loc.Get("FlowAnalytics_NoIssues");
        LastAnalysed = Loc.Format("FlowAnalytics_LastAnalysed",
            DateTime.Now.ToString("HH:mm:ss"));
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";
}
```

- [ ] **Step 4: Run VM tests**

```
dotnet test DialogEditor.Tests --no-build --filter "FullyQualifiedName~FlowAnalyticsViewModelTests" -v q
```
Expected: all tests PASS.

- [ ] **Step 5: Run full suite**

```
dotnet test DialogEditor.Tests --no-build -v q
```
Expected: all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs
git add DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs
git commit -m "feat: add FlowAnalyticsViewModel"
```

---

## Task 5: ConversationSaved Event

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add the `ConversationSaved` event and fire it in `SaveProject()`**

In `MainWindowViewModel.cs`, add the event alongside the existing ones (around line 567):

```csharp
    public event Action? TestModeEntered;
    public event Action? TestModeExited;
    public event Action? ConversationSaved;   // ← add this line
```

In `SaveProject()`, fire it after the successful save — after the `StatusText` assignment and before the `catch`:

```csharp
        AppLog.Info($"Project saved: {_projectPath}");
        StatusText = Loc.Format("Status_ProjectSaved", _project.Name);
        ConversationSaved?.Invoke();           // ← add this line
```

- [ ] **Step 2: Build to confirm it compiles**

```
dotnet build DialogEditor.ViewModels --no-restore -v q
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat: add ConversationSaved event to MainWindowViewModel"
```

---

## Task 6: ScrollToNode on ConversationView

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml.cs`

- [ ] **Step 1: Add `ScrollToNode` method**

In `ConversationView.axaml.cs`, add after `FocusEditor()`:

```csharp
    public void ScrollToNode(NodeViewModel node)
    {
        if (DataContext is not ConversationViewModel vm) return;
        vm.SelectedNode = node;
        Editor.BringIntoView(new global::Avalonia.Point(node.Location.X, node.Location.Y));
    }
```

- [ ] **Step 2: Build to confirm it compiles**

```
dotnet build DialogEditor.Avalonia --no-restore -v q
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConversationView.axaml.cs
git commit -m "feat: add ScrollToNode to ConversationView"
```

---

## Task 7: String Keys

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

- [ ] **Step 1: Add analytics string keys**

In `Strings.axaml`, add a new group after the existing BatchReplace keys:

```xml
    <!-- ── Flow Analytics ─────────────────────────────────────────────── -->
    <x:String x:Key="FlowAnalytics_Title">Flow Analytics</x:String>
    <x:String x:Key="FlowAnalytics_Statistics">STATISTICS</x:String>
    <x:String x:Key="FlowAnalytics_Issues">ISSUES — {0} found</x:String>
    <x:String x:Key="FlowAnalytics_NoIssues">No issues found</x:String>
    <x:String x:Key="FlowAnalytics_NoData">Open a conversation and click Refresh</x:String>
    <x:String x:Key="FlowAnalytics_LastAnalysed">Last analysed: {0}</x:String>
    <x:String x:Key="FlowAnalytics_Refresh">⟳ Refresh</x:String>

    <x:String x:Key="FlowAnalytics_Issue_Unreachable">Unreachable</x:String>
    <x:String x:Key="FlowAnalytics_Issue_PlayerDeadEnd">Dead end</x:String>
    <x:String x:Key="FlowAnalytics_Issue_EmptyText">Empty text</x:String>
    <x:String x:Key="FlowAnalytics_Issue_NoIncomingLinks">No incoming links</x:String>

    <x:String x:Key="FlowAnalytics_Stat_Nodes">Nodes</x:String>
    <x:String x:Key="FlowAnalytics_Stat_Words">Words</x:String>
    <x:String x:Key="FlowAnalytics_Stat_MaxDepth">Max depth</x:String>
    <x:String x:Key="FlowAnalytics_Stat_Player">Player</x:String>
    <x:String x:Key="FlowAnalytics_Stat_NPC">NPC</x:String>
    <x:String x:Key="FlowAnalytics_Stat_Narrator">Narrator</x:String>
    <x:String x:Key="FlowAnalytics_Stat_Script">Script</x:String>
    <x:String x:Key="FlowAnalytics_Stat_AvgLinks">Avg links/node</x:String>
    <x:String x:Key="FlowAnalytics_Stat_ConditionalLinks">Conditional links</x:String>

    <x:String x:Key="Menu_FlowAnalytics">Flow Analytics</x:String>
    <x:String x:Key="ToolTip_FlowAnalytics">Open the Flow Analytics window to view conversation statistics and detect structural issues.</x:String>
    <x:String x:Key="ToolTip_FlowAnalytics_Refresh">Re-analyse the current conversation and refresh all statistics and issues.</x:String>
    <x:String x:Key="ToolTip_FlowAnalytics_Navigate">Navigate the canvas to this node.</x:String>
```

- [ ] **Step 2: Build to confirm XAML compiles**

```
dotnet build DialogEditor.Avalonia --no-restore -v q
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: add Flow Analytics string keys"
```

---

## Task 8: Flow Analytics Window

**Files:**
- Create: `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml.cs`

- [ ] **Step 1: Create the XAML**

```xml
<!-- DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:analytics="clr-namespace:DialogEditor.Core.Analytics;assembly=DialogEditor.Core"
        x:Class="DialogEditor.Avalonia.Views.FlowAnalyticsWindow"
        Title="{StaticResource FlowAnalytics_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="560" Height="520" MinWidth="420" MinHeight="300"
        CanResize="True"
        ShowInTaskbar="False"
        Background="#1e1e1e"
        WindowStartupLocation="CenterOwner"
        x:CompileBindings="False">

    <Window.Styles>
        <Style Selector="TextBlock.stat-label">
            <Setter Property="Foreground"        Value="#888"/>
            <Setter Property="FontSize"          Value="11"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
        </Style>
        <Style Selector="TextBlock.stat-value">
            <Setter Property="Foreground"        Value="#e8e8e8"/>
            <Setter Property="FontSize"          Value="11"/>
            <Setter Property="HorizontalAlignment" Value="Right"/>
            <Setter Property="VerticalAlignment"   Value="Center"/>
        </Style>
    </Window.Styles>

    <Grid RowDefinitions="Auto,*,Auto" Margin="14,12,14,12">

        <!-- Statistics panel -->
        <Border Grid.Row="0" Background="#252525" CornerRadius="3" Padding="12,10"
                IsVisible="{Binding HasData}">
            <StackPanel Spacing="6">
                <TextBlock Text="{StaticResource FlowAnalytics_Statistics}"
                           Foreground="#888" FontSize="10" FontWeight="Bold"/>
                <Grid ColumnDefinitions="*,*,*" RowDefinitions="Auto,Auto,Auto">

                    <!-- Row 0: Nodes, Words, Max depth -->
                    <StackPanel Grid.Row="0" Grid.Column="0" Orientation="Horizontal" Spacing="6">
                        <TextBlock Classes="stat-label" Text="{StaticResource FlowAnalytics_Stat_Nodes}"/>
                        <TextBlock Classes="stat-value" Text="{Binding Statistics.TotalNodes}"/>
                    </StackPanel>
                    <StackPanel Grid.Row="0" Grid.Column="1" Orientation="Horizontal" Spacing="6">
                        <TextBlock Classes="stat-label" Text="{StaticResource FlowAnalytics_Stat_Words}"/>
                        <TextBlock Classes="stat-value" Text="{Binding Statistics.WordCount}"/>
                    </StackPanel>
                    <StackPanel Grid.Row="0" Grid.Column="2" Orientation="Horizontal" Spacing="6">
                        <TextBlock Classes="stat-label" Text="{StaticResource FlowAnalytics_Stat_MaxDepth}"/>
                        <TextBlock Classes="stat-value" Text="{Binding Statistics.MaxDepth}"/>
                    </StackPanel>

                    <!-- Row 1: Player, NPC, Narrator -->
                    <StackPanel Grid.Row="1" Grid.Column="0" Orientation="Horizontal" Spacing="6">
                        <TextBlock Classes="stat-label" Text="{StaticResource FlowAnalytics_Stat_Player}"/>
                        <TextBlock Classes="stat-value" Text="{Binding Statistics.PlayerCount}"/>
                    </StackPanel>
                    <StackPanel Grid.Row="1" Grid.Column="1" Orientation="Horizontal" Spacing="6">
                        <TextBlock Classes="stat-label" Text="{StaticResource FlowAnalytics_Stat_NPC}"/>
                        <TextBlock Classes="stat-value" Text="{Binding Statistics.NpcCount}"/>
                    </StackPanel>
                    <StackPanel Grid.Row="1" Grid.Column="2" Orientation="Horizontal" Spacing="6">
                        <TextBlock Classes="stat-label" Text="{StaticResource FlowAnalytics_Stat_Narrator}"/>
                        <TextBlock Classes="stat-value" Text="{Binding Statistics.NarratorCount}"/>
                    </StackPanel>

                    <!-- Row 2: Script, Avg links/node, Conditional links -->
                    <StackPanel Grid.Row="2" Grid.Column="0" Orientation="Horizontal" Spacing="6">
                        <TextBlock Classes="stat-label" Text="{StaticResource FlowAnalytics_Stat_Script}"/>
                        <TextBlock Classes="stat-value" Text="{Binding Statistics.ScriptCount}"/>
                    </StackPanel>
                    <StackPanel Grid.Row="2" Grid.Column="1" Orientation="Horizontal" Spacing="6">
                        <TextBlock Classes="stat-label" Text="{StaticResource FlowAnalytics_Stat_AvgLinks}"/>
                        <TextBlock Classes="stat-value" Text="{Binding Statistics.AvgLinksPerNode, StringFormat={}{0:F1}}"/>
                    </StackPanel>
                    <StackPanel Grid.Row="2" Grid.Column="2" Orientation="Horizontal" Spacing="6">
                        <TextBlock Classes="stat-label" Text="{StaticResource FlowAnalytics_Stat_ConditionalLinks}"/>
                        <TextBlock Classes="stat-value"
                                   Text="{Binding Statistics.ConditionalLinkCount}"/>
                    </StackPanel>

                </Grid>
            </StackPanel>
        </Border>

        <!-- No-data prompt -->
        <TextBlock Grid.Row="0"
                   Text="{StaticResource FlowAnalytics_NoData}"
                   Foreground="#555" FontSize="12"
                   HorizontalAlignment="Center" VerticalAlignment="Center"
                   IsVisible="{Binding HasData, Converter={StaticResource InverseBoolToVis}}"
                   Margin="0,40,0,0"/>

        <!-- Issues list -->
        <ScrollViewer Grid.Row="1" Margin="0,10,0,10"
                      IsVisible="{Binding HasData}">
            <StackPanel Spacing="2">
                <TextBlock Text="{Binding StatusText}"
                           Foreground="#888" FontSize="10" FontWeight="Bold"
                           Margin="0,0,0,6"/>
                <ItemsControl ItemsSource="{Binding Issues}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="vm:FlowIssueViewModel">
                            <Button Background="Transparent" BorderThickness="0"
                                    HorizontalContentAlignment="Stretch"
                                    Padding="0,1"
                                    Command="{Binding NavigateCommand}"
                                    ToolTip.Tip="{StaticResource ToolTip_FlowAnalytics_Navigate}">
                                <Border Background="#1a1a1a" CornerRadius="3" Padding="8,5">
                                    <Border.Styles>
                                        <Style Selector="Border" x:DataType="vm:FlowIssueViewModel">
                                            <Setter Property="BorderThickness" Value="3,0,0,0"/>
                                            <Setter Property="BorderBrush"     Value="#e09030"/>
                                        </Style>
                                    </Border.Styles>
                                    <Grid ColumnDefinitions="90,*">
                                        <TextBlock Grid.Column="0"
                                                   Text="{Binding KindLabel}"
                                                   FontSize="10" FontWeight="Bold"
                                                   VerticalAlignment="Center"/>
                                        <TextBlock Grid.Column="1"
                                                   Text="{Binding DisplayText}"
                                                   Foreground="#aaa" FontSize="11"
                                                   TextWrapping="Wrap"
                                                   VerticalAlignment="Center"/>
                                    </Grid>
                                </Border>
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </ScrollViewer>

        <!-- Footer -->
        <Grid Grid.Row="2" ColumnDefinitions="*,Auto">
            <TextBlock Grid.Column="0"
                       Text="{Binding LastAnalysed}"
                       Foreground="#555" FontSize="11"
                       VerticalAlignment="Center"
                       IsVisible="{Binding HasData}"/>
            <Button Grid.Column="1"
                    Content="{StaticResource FlowAnalytics_Refresh}"
                    Command="{Binding RefreshCommand}"
                    Background="#333" Foreground="#ccc"
                    BorderThickness="0" Padding="12,5" FontSize="11"
                    ToolTip.Tip="{StaticResource ToolTip_FlowAnalytics_Refresh}"/>
        </Grid>

    </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind**

```csharp
// DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml.cs
using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class FlowAnalyticsWindow : Window
{
    public FlowAnalyticsWindow() => InitializeComponent();

    public FlowAnalyticsWindow(FlowAnalyticsViewModel vm) : this()
    {
        DataContext = vm;
    }
}
```

- [ ] **Step 3: Add `KindLabel` and `DisplayText` to `FlowIssueViewModel`**

The XAML binds to `KindLabel` and `DisplayText`. Add these to `FlowIssueViewModel` in `FlowAnalyticsViewModel.cs`:

```csharp
public partial class FlowIssueViewModel : ObservableObject
{
    private readonly Action<int> _navigate;

    public int           NodeId      { get; }
    public FlowIssueKind Kind        { get; }
    public string        NodeSnippet { get; }

    public string KindLabel => Kind switch
    {
        FlowIssueKind.Unreachable      => Loc.Get("FlowAnalytics_Issue_Unreachable"),
        FlowIssueKind.PlayerDeadEnd    => Loc.Get("FlowAnalytics_Issue_PlayerDeadEnd"),
        FlowIssueKind.EmptyText        => Loc.Get("FlowAnalytics_Issue_EmptyText"),
        FlowIssueKind.NoIncomingLinks  => Loc.Get("FlowAnalytics_Issue_NoIncomingLinks"),
        _                              => Kind.ToString()
    };

    public string DisplayText => $"Node {NodeId} — {NodeSnippet}";

    public FlowIssueViewModel(FlowIssue issue, string nodeSnippet, Action<int> navigate)
    {
        NodeId      = issue.NodeId;
        Kind        = issue.Kind;
        NodeSnippet = nodeSnippet;
        _navigate   = navigate;
    }

    [RelayCommand]
    private void Navigate() => _navigate(NodeId);
}
```

Note: also add `using DialogEditor.ViewModels.Resources;` to the using block in `FlowAnalyticsViewModel.cs` if not already present.

- [ ] **Step 4: Build to verify**

```
dotnet build DialogEditor.Avalonia --no-restore -v q
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Run full test suite**

```
dotnet test DialogEditor.Tests --no-build -v q
```
Expected: all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml
git add DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml.cs
git add DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs
git commit -m "feat: add FlowAnalyticsWindow and update IssueViewModel with display properties"
```

---

## Task 9: MainWindow Wiring

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Add menu item to `MainWindow.axaml`**

In the `<MenuItem Header="{StaticResource Menu_Test}">` block, after the `Menu_Edit` menu item (around line 82), there is no Test menu analytics entry needed. Instead, add a new entry to the `Menu_Test` menu or more appropriately add it at the end of `Menu_Edit`. 

Actually, Flow Analytics fits best in the **View** menu — but no View menu exists. Add it to the `Menu_Test` block, which already holds analysis-adjacent actions. Add after the `<Separator/>` before `RestoreBackup`:

```xml
                    <MenuItem Header="{StaticResource Menu_Test}">
                        <MenuItem Header="{StaticResource Button_TestPatch}"
                                  Command="{Binding TestPatchCommand}"
                                  InputGesture="F5"/>
                        <MenuItem Header="{StaticResource TestMode_Restore}"
                                  Command="{Binding RestoreConversationCommand}"
                                  InputGesture="F6"/>
                        <Separator/>
                        <MenuItem Header="{StaticResource Menu_FlowAnalytics}"
                                  Click="FlowAnalytics_Click"
                                  InputGesture="F7"
                                  ToolTip.Tip="{StaticResource ToolTip_FlowAnalytics}"/>
                        <Separator/>
                        <MenuItem Header="{StaticResource Button_RestoreBackup}"
                                  Command="{Binding RestoreBackupCommand}"
                                  InputGesture="Ctrl+Shift+B"/>
                    </MenuItem>
```

- [ ] **Step 2: Add field and handler to `MainWindow.axaml.cs`**

Add `_flowAnalyticsWindow` alongside the other window fields (around line 22):

```csharp
    private FlowAnalyticsWindow?  _flowAnalyticsWindow;
```

Add the click handler after `BatchReplace_Click`:

```csharp
    private void FlowAnalytics_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;

        var analyticsVm = new FlowAnalyticsViewModel(
            () => vm.IsProjectOpen ? vm.Canvas.BuildSnapshot() : null,
            nodeId =>
            {
                var node = vm.Canvas.Nodes.FirstOrDefault(n => n.NodeId == nodeId);
                if (node is not null) CanvasView.ScrollToNode(node);
            });

        if (_flowAnalyticsWindow is null || !_flowAnalyticsWindow.IsVisible)
        {
            _flowAnalyticsWindow = new FlowAnalyticsWindow(analyticsVm);
            _flowAnalyticsWindow.Closed += (_, _) =>
            {
                vm.ConversationSaved -= OnConversationSavedRefreshAnalytics;
                _flowAnalyticsWindow = null;
            };
            vm.ConversationSaved += OnConversationSavedRefreshAnalytics;
        }
        else
        {
            _flowAnalyticsWindow.DataContext = analyticsVm;
        }
        _flowAnalyticsWindow.Show();
        _flowAnalyticsWindow.Activate();
    }

    private void OnConversationSavedRefreshAnalytics()
    {
        if (_flowAnalyticsWindow?.DataContext is FlowAnalyticsViewModel aVm)
            aVm.RefreshCommand.Execute(null);
    }
```

- [ ] **Step 3: Add `F7` key handler in `OnKeyDownTunnel`**

In the `switch` in `OnKeyDownTunnel`, add after the `F6` case:

```csharp
            case Key.F7 when e.KeyModifiers == KeyModifiers.None:
                FlowAnalytics_Click(null, null!);
                e.Handled = true;
                break;
```

Also add `F7` to the string resources. In `Strings.axaml`, the `Menu_FlowAnalytics` entry already exists. Verify the `InputGesture="F7"` in the XAML is sufficient — no additional string key needed for the gesture.

- [ ] **Step 4: Build the full solution**

```
dotnet build --no-restore -v q
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Run full test suite**

```
dotnet test --no-build -v q
```
Expected: all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat: wire FlowAnalyticsWindow into MainWindow (F7, Test menu)"
```

---

## Self-Review Checklist

- [x] **Models** (Task 1) — `FlowStatistics`, `FlowIssue`, `FlowIssueKind`, `FlowAnalysisReport` all defined before use.
- [x] **Service** (Tasks 2–3) — BFS handles cycles; all 4 issue types implemented and tested.
- [x] **ViewModel** (Task 4) — `FlowIssueViewModel.KindLabel` and `DisplayText` added in Task 8 Step 3 before the window XAML references them.
- [x] **`ConversationSaved` event** (Task 5) — fired in `SaveProject()`; both `Save()` and `SaveProject()` call `SaveProject()` so both paths trigger it.
- [x] **`ScrollToNode`** (Task 6) — uses same `BringIntoView` pattern as the existing `CenterOnRoot_Click`.
- [x] **String keys** (Task 7) — all keys referenced in XAML and `Loc.Get()` calls are defined.
- [x] **Navigation** (Task 9) — unsubscribes `ConversationSaved` on window close to prevent leaks.
- [x] **Spec coverage** — all spec requirements have corresponding tasks.
