# Path-Based Writing Stats Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Playthrough stats" section to Flow Analytics — longest/shortest playthrough, words-per-speaker, and per-opening-choice content + longest-read rows, with a Default/Female two-reading model.

**Architecture:** A new pure `PathStatsService` in `DialogEditor.Core.Analytics` does a back-edge-cut DAG walk of the open conversation's graph with two per-node weight functions (Default and Female word counts), gated at a 10% total-word difference. `FlowAnalyticsViewModel` calls it in `Refresh`, formats reading times, resolves speaker names, and exposes new collections that a new window section binds to. No new window/menu/wiring.

**Tech Stack:** C# / .NET, Avalonia, CommunityToolkit.Mvvm, xUnit. Spec: `docs/superpowers/specs/2026-07-13-path-based-writing-stats-design.md`.

## Global Constraints

- **TDD:** red → green → refactor; no implementation before a failing test.
- **Localisation:** no user-visible string hard-coded in C#/XAML; every label/tooltip/summary is a `{DynamicResource}` / `Loc.*` key in `Strings.axaml`.
- **Tooltips + automation:** every interactive control carries `ToolTip.Tip` mirrored to `AutomationProperties.HelpText`, and `AutomationProperties.Name` where the label isn't self-describing. Enforced by `AutomationNameTests`/`AutomationHelpTextTests`.
- **Window icon:** the edited window keeps `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- **Error handling:** the service is pure and throws nothing user-facing; no bare `catch {}`.
- **No stray hex / named colours / static string or fontsize resources:** reuse existing `Brush.*` and `FontSize.*` tokens only.
- **Tests run serially.**
- **Constants:** reading speed **200 wpm**; female significance threshold **10%** (0.10). Hard-coded, no UI knob.
- **Conventions (shared with FlowAnalysisService):** root is node 0; reachability from root.

---

## File Structure

- Create `DialogEditor.Core/Analytics/PathStatsModels.cs` — report records.
- Create `DialogEditor.Core/Analytics/PathStatsService.cs` — the pure analysis.
- Create `DialogEditor.ViewModels/ViewModels/PathStatsFormat.cs` — reading-time formatting helper.
- Modify `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs` — path-stats collections + row VMs.
- Modify `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml` — the Playthrough stats section.
- Modify `DialogEditor.Avalonia/Resources/Strings.axaml` — new keys.
- Tests: `DialogEditor.Tests/Analytics/PathStatsServiceTests.cs`, `DialogEditor.Tests/ViewModels/PathStatsFormatTests.cs`, extend `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs`.

---

## Task 1: `PathStatsService` + models

**Files:**
- Create: `DialogEditor.Core/Analytics/PathStatsModels.cs`
- Create: `DialogEditor.Core/Analytics/PathStatsService.cs`
- Test: `DialogEditor.Tests/Analytics/PathStatsServiceTests.cs`

**Interfaces:**
- Consumes: `ConversationEditSnapshot` (`.Nodes`), `NodeEditSnapshot` (`.NodeId`, `.DefaultText`, `.FemaleText`, `.IsPlayerChoice`, `.SpeakerGuid`, `.SpeakerCategory`, `.Links`), `LinkEditSnapshot` (`.ToNodeId`), `SpeakerCategory`.
- Produces:
  - `record SpeakerWordCount(string SpeakerGuid, SpeakerCategory Category, int DefaultWords, int FemaleWords)`
  - `record BranchStat(int ChoiceNodeId, string ChoiceText, int DefaultContentWords, int DefaultLongestWords, int FemaleContentWords, int FemaleLongestWords)`
  - `record PathStatsReport(bool HasSignificantFemaleVariant, int DefaultTotalWords, int FemaleTotalWords, int DefaultLongestWords, int DefaultShortestWords, int FemaleLongestWords, int FemaleShortestWords, IReadOnlyList<SpeakerWordCount> WordsPerSpeaker, IReadOnlyList<BranchStat> Branches)`
  - `static PathStatsReport PathStatsService.Analyze(ConversationEditSnapshot snapshot)`

- [ ] **Step 1: Write the models file**

Create `DialogEditor.Core/Analytics/PathStatsModels.cs`:

```csharp
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Analytics;

/// Total words spoken by one speaker across the conversation, under each reading.
public record SpeakerWordCount(string SpeakerGuid, SpeakerCategory Category, int DefaultWords, int FemaleWords);

/// Stats for one top-level player choice: how much content lives down it, and the
/// longest single read through it, under each reading. Measured from the choice onward.
public record BranchStat(
    int    ChoiceNodeId,
    string ChoiceText,
    int    DefaultContentWords,
    int    DefaultLongestWords,
    int    FemaleContentWords,
    int    FemaleLongestWords);

/// Playthrough-oriented stats for one conversation. Female figures are meaningful only
/// when HasSignificantFemaleVariant is true (else they ~equal the default figures).
public record PathStatsReport(
    bool HasSignificantFemaleVariant,
    int  DefaultTotalWords,
    int  FemaleTotalWords,
    int  DefaultLongestWords,
    int  DefaultShortestWords,
    int  FemaleLongestWords,
    int  FemaleShortestWords,
    IReadOnlyList<SpeakerWordCount> WordsPerSpeaker,
    IReadOnlyList<BranchStat>       Branches);
```

- [ ] **Step 2: Write the failing tests**

Create `DialogEditor.Tests/Analytics/PathStatsServiceTests.cs`:

```csharp
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Analytics;

public class PathStatsServiceTests
{
    private static NodeEditSnapshot Node(
        int id, string defaultText = "", string femaleText = "",
        bool isPlayerChoice = false, string speaker = "",
        SpeakerCategory category = SpeakerCategory.Npc,
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, isPlayerChoice, category, speaker, "", defaultText, femaleText,
            "Conversation", "None", "", "", "", false, false, links ?? [], [], []);

    private static LinkEditSnapshot Link(int from, int to) => new(from, to, 1f, "", false);
    private static ConversationEditSnapshot Snap(params NodeEditSnapshot[] n) => new(n);

    [Fact]
    public void LongestAndShortest_TwoEndings()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 2)]),
            Node(1, "long",  isPlayerChoice: true, links: [Link(1, 3)]),
            Node(3, "aaa bbb ccc ddd"),           // 4 words
            Node(2, "short", isPlayerChoice: true)));

        Assert.Equal(6, report.DefaultLongestWords);   // start+long+4
        Assert.Equal(2, report.DefaultShortestWords);  // start+short
    }

    [Fact]
    public void HubLoop_Terminates_AndCountsNodeOnce()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a", links: [Link(0, 1)]),
            Node(1, "b", links: [Link(1, 2)]),
            Node(2, "c", links: [Link(2, 0)])));       // back-edge to ancestor 0

        Assert.Equal(3, report.DefaultLongestWords);   // a+b+c, loop cut
    }

    [Fact]
    public void PerOpeningChoice_ContentAndLongest()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 4)]),
            Node(1, "A", isPlayerChoice: true, links: [Link(1, 3)]),
            Node(3, "x y z"),                          // 3 words
            Node(4, "B", isPlayerChoice: true, links: [Link(4, 5)]),
            Node(5, "p")));                            // 1 word

        Assert.Equal(2, report.Branches.Count);
        var a = report.Branches.Single(b => b.ChoiceNodeId == 1);
        Assert.Equal(4, a.DefaultContentWords);        // A(1)+x y z(3)
        Assert.Equal(4, a.DefaultLongestWords);
        var b = report.Branches.Single(x => x.ChoiceNodeId == 4);
        Assert.Equal(2, b.DefaultContentWords);        // B(1)+p(1)
        Assert.Equal(2, b.DefaultLongestWords);
    }

    [Fact]
    public void WordsPerSpeaker_GroupedByGuid()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "aa bb", speaker: "npc1", links: [Link(0, 1)]),
            Node(1, "cc", speaker: "player", category: SpeakerCategory.Player, links: [Link(1, 2)]),
            Node(2, "dd", speaker: "npc1")));

        Assert.Equal(3, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "npc1").DefaultWords);
        Assert.Equal(1, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "player").DefaultWords);
    }

    [Fact]
    public void FemaleGate_NotSignificant_WhenNoFemaleText()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a b", links: [Link(0, 1)]),
            Node(1, "c d")));

        Assert.False(report.HasSignificantFemaleVariant);
        Assert.Equal(report.DefaultTotalWords, report.FemaleTotalWords);
    }

    [Fact]
    public void FemaleGate_Significant_WhenFemaleDiffersOver10Percent()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a", links: [Link(0, 1)]),
            Node(1, "b", femaleText: "one two three four five")));   // 5 vs 1

        Assert.True(report.HasSignificantFemaleVariant);
        Assert.Equal(2, report.DefaultTotalWords);   // a + b
        Assert.Equal(6, report.FemaleTotalWords);    // a + five
    }

    [Fact]
    public void EmptySnapshot_EmptyReport()
    {
        var report = PathStatsService.Analyze(Snap());
        Assert.Equal(0, report.DefaultTotalWords);
        Assert.Empty(report.WordsPerSpeaker);
        Assert.Empty(report.Branches);
    }

    [Fact]
    public void NoRoot_HeaderZero_ButSpeakersCounted()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(5, "hello world", speaker: "x")));   // no node 0

        Assert.Equal(0, report.DefaultLongestWords);
        Assert.Empty(report.Branches);
        Assert.Equal(2, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "x").DefaultWords);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~PathStatsServiceTests`
Expected: FAIL — `PathStatsService` does not exist.

- [ ] **Step 4: Implement the service**

Create `DialogEditor.Core/Analytics/PathStatsService.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Analytics;

/// <summary>
/// Playthrough-oriented stats over one conversation's graph. Pure and IO-free.
/// Cycles are broken to a DAG (back-edges to a DFS ancestor are dropped), so longest/
/// shortest playthroughs are well-defined and O(V+E). Every metric is computed under two
/// per-node weight functions — Default text words, and Female text words (falling back to
/// Default where a node has no female text) — with a 10% total-difference significance gate.
/// Spec: docs/superpowers/specs/2026-07-13-path-based-writing-stats-design.md
/// </summary>
public static class PathStatsService
{
    private const double FemaleSignificanceThreshold = 0.10;

    public static PathStatsReport Analyze(ConversationEditSnapshot snapshot)
    {
        var nodes = snapshot.Nodes;
        if (nodes.Count == 0)
            return new PathStatsReport(false, 0, 0, 0, 0, 0, 0, [], []);

        var nodeById = nodes.ToDictionary(n => n.NodeId);

        static int Words(string? t) =>
            string.IsNullOrEmpty(t) ? 0 : t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int Def(NodeEditSnapshot n) => Words(n.DefaultText);
        int Fem(NodeEditSnapshot n) =>
            string.IsNullOrWhiteSpace(n.FemaleText) ? Words(n.DefaultText) : Words(n.FemaleText);

        // Totals + significance over ALL nodes (structure-independent).
        var defaultTotal = nodes.Sum(Def);
        var femaleTotal  = nodes.Sum(Fem);
        var significant  = defaultTotal > 0 &&
            Math.Abs(femaleTotal - defaultTotal) / (double)defaultTotal > FemaleSignificanceThreshold;

        var wordsPerSpeaker = nodes
            .GroupBy(n => n.SpeakerGuid)
            .Select(g => new SpeakerWordCount(g.Key, g.First().SpeakerCategory, g.Sum(Def), g.Sum(Fem)))
            .OrderByDescending(s => s.DefaultWords)
            .ToList();

        if (!nodeById.ContainsKey(0))
            return new PathStatsReport(significant, defaultTotal, femaleTotal, 0, 0, 0, 0,
                wordsPerSpeaker, []);

        // ── Break to a DAG (drop back-edges to a DFS ancestor) ────────────
        var dag     = new Dictionary<int, List<int>>();
        var onStack = new HashSet<int>();
        var visited = new HashSet<int>();
        void Dfs(int u)
        {
            visited.Add(u);
            onStack.Add(u);
            dag[u] = [];
            foreach (var link in nodeById[u].Links)
            {
                var v = link.ToNodeId;
                if (!nodeById.ContainsKey(v)) continue;   // dangling
                if (onStack.Contains(v)) continue;         // back-edge → drop
                dag[u].Add(v);
                if (!visited.Contains(v)) Dfs(v);
            }
            onStack.Remove(u);
        }
        Dfs(0);

        // Memoised longest/shortest weighted path on the DAG (one memo per weight fn).
        var longMemo  = new Dictionary<(int, bool), int>();
        var shortMemo = new Dictionary<(int, bool), int>();

        int Longest(int u, bool female)
        {
            if (longMemo.TryGetValue((u, female), out var cached)) return cached;
            var w = female ? Fem(nodeById[u]) : Def(nodeById[u]);
            var best = w;
            if (dag.TryGetValue(u, out var outs) && outs.Count > 0)
                best = w + outs.Max(v => Longest(v, female));
            longMemo[(u, female)] = best;
            return best;
        }
        int Shortest(int u, bool female)
        {
            if (shortMemo.TryGetValue((u, female), out var cached)) return cached;
            var w = female ? Fem(nodeById[u]) : Def(nodeById[u]);
            var best = w;
            if (dag.TryGetValue(u, out var outs) && outs.Count > 0)
                best = w + outs.Min(v => Shortest(v, female));
            shortMemo[(u, female)] = best;
            return best;
        }

        // Reachable-set content sum on the FULL graph (cycle-safe via visited set).
        int ReachableSum(int start, bool female)
        {
            var seen  = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            var sum = 0;
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                sum += female ? Fem(nodeById[u]) : Def(nodeById[u]);
                foreach (var link in nodeById[u].Links)
                    if (nodeById.ContainsKey(link.ToNodeId) && seen.Add(link.ToNodeId))
                        queue.Enqueue(link.ToNodeId);
            }
            return sum;
        }

        // Opening choices: root's direct link targets that are player choices.
        var branches = new List<BranchStat>();
        foreach (var link in nodeById[0].Links)
        {
            if (!nodeById.TryGetValue(link.ToNodeId, out var c) || !c.IsPlayerChoice) continue;
            branches.Add(new BranchStat(
                c.NodeId, c.DefaultText ?? "",
                ReachableSum(c.NodeId, female: false), Longest(c.NodeId, female: false),
                ReachableSum(c.NodeId, female: true),  Longest(c.NodeId, female: true)));
        }
        branches = branches.OrderBy(b => b.ChoiceNodeId).ToList();

        return new PathStatsReport(
            significant, defaultTotal, femaleTotal,
            Longest(0, false),  Shortest(0, false),
            Longest(0, true),   Shortest(0, true),
            wordsPerSpeaker, branches);
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~PathStatsServiceTests`
Expected: PASS (8).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Core/Analytics/PathStatsModels.cs DialogEditor.Core/Analytics/PathStatsService.cs \
        DialogEditor.Tests/Analytics/PathStatsServiceTests.cs
git commit -m "feat(path-stats): PathStatsService (DAG longest/shortest, per-choice, female gate)"
```

---

## Task 2: reading-time helper + `FlowAnalyticsViewModel` extensions

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/PathStatsFormat.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/PathStatsFormatTests.cs`, extend `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs`

**Interfaces:**
- Consumes: `PathStatsService.Analyze`, `PathStatsReport`/`BranchStat`/`SpeakerWordCount` (Task 1); `SpeakerNameService.Resolve`; `Loc`.
- Produces:
  - `static class PathStatsFormat { static string ReadingTime(int words); }`
  - On `FlowAnalyticsViewModel`: `bool HasPathStats`, `bool HasSignificantFemaleVariant`, `string LongestPlaythroughText`, `string ShortestPlaythroughText`, `string TotalContentText`, `ObservableCollection<SpeakerWordRowViewModel> WordsPerSpeaker`, `ObservableCollection<PathBranchRowViewModel> Branches`.
  - `PathBranchRowViewModel` with `ChoiceText`, `DefaultText2` (content), `DefaultLongestText`, `FemaleContentText`, `FemaleLongestText`, `NavigateCommand`.
  - `SpeakerWordRowViewModel` with `Display`.

- [ ] **Step 1: Write the reading-time test**

Create `DialogEditor.Tests/ViewModels/PathStatsFormatTests.cs`:

```csharp
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.ViewModels;

public class PathStatsFormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(200, "1:00")]   // 200 words @ 200 wpm = 1 min
    [InlineData(350, "1:45")]   // 350/200 min = 1.75 min = 1:45
    [InlineData(100, "0:30")]
    public void ReadingTime_FormatsMinutesSeconds(int words, string expected)
    {
        Assert.Equal(expected, PathStatsFormat.ReadingTime(words));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~PathStatsFormatTests`
Expected: FAIL — `PathStatsFormat` does not exist.

- [ ] **Step 3: Implement the helper**

Create `DialogEditor.ViewModels/ViewModels/PathStatsFormat.cs`:

```csharp
namespace DialogEditor.ViewModels;

/// Reading-time formatting for path stats: words ÷ 200 wpm, shown m:ss.
public static class PathStatsFormat
{
    private const int WordsPerMinute = 200;

    public static string ReadingTime(int words)
    {
        var totalSeconds = (int)Math.Round(words / (double)WordsPerMinute * 60);
        return $"{totalSeconds / 60}:{totalSeconds % 60:D2}";
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~PathStatsFormatTests`
Expected: PASS (4).

- [ ] **Step 5: Add the Loc keys**

Add to `DialogEditor.Avalonia/Resources/Strings.axaml` (near the other `FlowAnalytics_*` keys):

```xml
    <!-- Playthrough stats -->
    <sys:String x:Key="PathStats_Header">PLAYTHROUGH STATS</sys:String>
    <sys:String x:Key="PathStats_Longest">Longest playthrough</sys:String>
    <sys:String x:Key="PathStats_Shortest">Shortest playthrough</sys:String>
    <sys:String x:Key="PathStats_Total">Total content</sys:String>
    <sys:String x:Key="PathStats_WordsPerSpeaker">Words per speaker</sys:String>
    <sys:String x:Key="PathStats_OpeningChoices">By opening choice</sys:String>
    <sys:String x:Key="PathStats_NoOpeningChoices">No opening player choices.</sys:String>
    <sys:String x:Key="PathStats_WordsTime">{0} words · {1}</sys:String>
    <sys:String x:Key="PathStats_DefaultFemale">{0}   ♀ {1}</sys:String>
    <sys:String x:Key="PathStats_SpeakerRow">{0}: {1}</sys:String>
    <sys:String x:Key="PathStats_SpeakerRowFemale">{0}: {1}   ♀ {2}</sys:String>
    <sys:String x:Key="PathStats_BranchContent">content {0}</sys:String>
    <sys:String x:Key="PathStats_BranchLongest">longest {0}</sys:String>
    <sys:String x:Key="PathStats_Cat_Player">Player</sys:String>
    <sys:String x:Key="PathStats_Cat_Npc">NPC</sys:String>
    <sys:String x:Key="PathStats_Cat_Narrator">Narrator</sys:String>
    <sys:String x:Key="PathStats_Cat_Script">Script</sys:String>
    <sys:String x:Key="ToolTip_PathStats_Navigate">Jump to this opening choice's node.</sys:String>
    <sys:String x:Key="ToolTip_PathStats">Playthrough-oriented stats: the longest and shortest single read, words per speaker, and how much content sits down each opening choice. Female figures appear when the female variant differs by more than 10%.</sys:String>
```

- [ ] **Step 6: Write the failing VM tests (extend the existing file)**

Add to `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs` (the `MakeNode` helper there lacks `isPlayerChoice`/`femaleText`/`speaker` params — add a second helper for path tests rather than editing the shared one):

```csharp
    private static NodeEditSnapshot PathNode(
        int id, string defaultText = "", string femaleText = "",
        bool isPlayerChoice = false, string speaker = "",
        SpeakerCategory category = SpeakerCategory.Npc,
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, isPlayerChoice, category, speaker, "", defaultText, femaleText,
            "Conversation", "None", "", "", "", false, false, links ?? [], [], []);

    [Fact]
    public void Refresh_PopulatesBranchesAndSpeakers()
    {
        var snapshot = new ConversationEditSnapshot([
            PathNode(0, "start", speaker: "npc1", links: [Link(0, 1)]),
            PathNode(1, "reply", isPlayerChoice: true, speaker: "player",
                     category: SpeakerCategory.Player, links: [Link(1, 2)]),
            PathNode(2, "aa bb cc", speaker: "npc1")
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.True(vm.HasPathStats);
        Assert.Single(vm.Branches);
        Assert.NotEmpty(vm.WordsPerSpeaker);
    }

    [Fact]
    public void Refresh_FemaleColumns_GatedBySignificance()
    {
        // Female text far longer than default → significant.
        var significant = new ConversationEditSnapshot([
            PathNode(0, "a", links: [Link(0, 1)]),
            PathNode(1, "b", femaleText: "one two three four five", isPlayerChoice: true)
        ]);
        var vm = new FlowAnalyticsViewModel(() => significant, _ => { });
        vm.RefreshCommand.Execute(null);
        Assert.True(vm.HasSignificantFemaleVariant);

        // No female text → not significant.
        var plain = new ConversationEditSnapshot([
            PathNode(0, "a", links: [Link(0, 1)]),
            PathNode(1, "b", isPlayerChoice: true)
        ]);
        var vm2 = new FlowAnalyticsViewModel(() => plain, _ => { });
        vm2.RefreshCommand.Execute(null);
        Assert.False(vm2.HasSignificantFemaleVariant);
    }

    [Fact]
    public void BranchRow_Navigate_CallsCallbackWithChoiceNode()
    {
        var navigatedId = -1;
        var snapshot = new ConversationEditSnapshot([
            PathNode(0, "start", links: [Link(0, 7)]),
            PathNode(7, "reply", isPlayerChoice: true)
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, id => navigatedId = id);
        vm.RefreshCommand.Execute(null);

        vm.Branches[0].NavigateCommand.Execute(null);

        Assert.Equal(7, navigatedId);
    }
```

- [ ] **Step 7: Run to verify failure**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~FlowAnalyticsViewModelTests`
Expected: FAIL — `HasPathStats` / `Branches` don't exist.

- [ ] **Step 8: Implement the VM extensions**

In `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`:

(a) add row VM classes at the top (beside `TokenIssueRowViewModel`):

```csharp
/// One "By opening choice" row.
public sealed partial class PathBranchRowViewModel : ObservableObject
{
    private readonly Action _navigate;

    public string ChoiceText         { get; }
    public string DefaultContentText { get; }
    public string DefaultLongestText { get; }
    public string FemaleContentText  { get; }
    public string FemaleLongestText  { get; }

    public PathBranchRowViewModel(string choiceText, string defaultContent, string defaultLongest,
        string femaleContent, string femaleLongest, Action navigate)
    {
        ChoiceText         = choiceText;
        DefaultContentText = defaultContent;
        DefaultLongestText = defaultLongest;
        FemaleContentText  = femaleContent;
        FemaleLongestText  = femaleLongest;
        _navigate          = navigate;
    }

    [RelayCommand] private void Navigate() => _navigate();
}

/// One "Words per speaker" row.
public sealed class SpeakerWordRowViewModel
{
    public string Display { get; }
    public SpeakerWordRowViewModel(string display) => Display = display;
}
```

(b) add the collections + observable props to `FlowAnalyticsViewModel` (beside `Issues`/`TokenIssues`):

```csharp
    public ObservableCollection<PathBranchRowViewModel> Branches       { get; } = [];
    public ObservableCollection<SpeakerWordRowViewModel> WordsPerSpeaker { get; } = [];

    [ObservableProperty] private bool   _hasPathStats;
    [ObservableProperty] private bool   _hasSignificantFemaleVariant;
    [ObservableProperty] private string _longestPlaythroughText  = string.Empty;
    [ObservableProperty] private string _shortestPlaythroughText = string.Empty;
    [ObservableProperty] private string _totalContentText        = string.Empty;
```

(c) at the END of `Refresh()` (after the token-validation block, still inside the method — `snapshot` is in scope and non-null there), add:

```csharp
        RefreshPathStats(snapshot);
```

(d) add the `RefreshPathStats` method:

```csharp
    private void RefreshPathStats(ConversationEditSnapshot snapshot)
    {
        var report = PathStatsService.Analyze(snapshot);
        HasSignificantFemaleVariant = report.HasSignificantFemaleVariant;

        LongestPlaythroughText  = WordsTimePair(report.DefaultLongestWords,  report.FemaleLongestWords);
        ShortestPlaythroughText = WordsTimePair(report.DefaultShortestWords, report.FemaleShortestWords);
        TotalContentText        = WordsTimePair(report.DefaultTotalWords,    report.FemaleTotalWords);

        WordsPerSpeaker.Clear();
        foreach (var s in report.WordsPerSpeaker)
        {
            var name = ResolveSpeakerName(s);
            var display = HasSignificantFemaleVariant
                ? Loc.Format("PathStats_SpeakerRowFemale", name, s.DefaultWords, s.FemaleWords)
                : Loc.Format("PathStats_SpeakerRow", name, s.DefaultWords);
            WordsPerSpeaker.Add(new SpeakerWordRowViewModel(display));
        }

        Branches.Clear();
        foreach (var b in report.Branches)
        {
            var choice = Truncate(b.ChoiceText, 50);
            Branches.Add(new PathBranchRowViewModel(
                choice,
                Loc.Format("PathStats_BranchContent", WordsTime(b.DefaultContentWords)),
                Loc.Format("PathStats_BranchLongest", WordsTime(b.DefaultLongestWords)),
                Loc.Format("PathStats_BranchContent", WordsTime(b.FemaleContentWords)),
                Loc.Format("PathStats_BranchLongest", WordsTime(b.FemaleLongestWords)),
                () => _navigateToNode(b.ChoiceNodeId)));
        }

        HasPathStats = report.DefaultTotalWords > 0 || report.WordsPerSpeaker.Count > 0;
    }

    private string WordsTime(int words) =>
        Loc.Format("PathStats_WordsTime", words, PathStatsFormat.ReadingTime(words));

    private string WordsTimePair(int defaultWords, int femaleWords) =>
        HasSignificantFemaleVariant
            ? Loc.Format("PathStats_DefaultFemale", WordsTime(defaultWords), WordsTime(femaleWords))
            : WordsTime(defaultWords);

    private static string ResolveSpeakerName(SpeakerWordCount s)
    {
        var resolved = SpeakerNameService.Resolve(s.SpeakerGuid);
        if (!string.IsNullOrEmpty(resolved)) return resolved;
        return s.Category switch
        {
            SpeakerCategory.Player   => Loc.Get("PathStats_Cat_Player"),
            SpeakerCategory.Narrator => Loc.Get("PathStats_Cat_Narrator"),
            SpeakerCategory.Script   => Loc.Get("PathStats_Cat_Script"),
            _                        => Loc.Get("PathStats_Cat_Npc"),
        };
    }
```

Reuse the existing private `Truncate` helper (already in the file, used by the issue snippets). Add `using DialogEditor.Core.Analytics;` and `using DialogEditor.ViewModels.Services;` (for `SpeakerNameService`) if not already present. `SpeakerCategory` resolves via the existing `using DialogEditor.Core.Models;`.

- [ ] **Step 9: Run to verify pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~FlowAnalyticsViewModelTests`
Expected: PASS (existing + 3 new).

- [ ] **Step 10: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/PathStatsFormat.cs \
        DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs \
        DialogEditor.Avalonia/Resources/Strings.axaml \
        DialogEditor.Tests/ViewModels/PathStatsFormatTests.cs \
        DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs
git commit -m "feat(path-stats): FlowAnalyticsViewModel playthrough stats + reading-time format"
```

---

## Task 3: Flow Analytics window section

**Files:**
- Modify: `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`
- Test: `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs` already covers the VM; add a lightweight window smoke assertion is optional (the window has no code-behind logic). Skip a new window test unless the build reveals a binding gap.

**Interfaces:**
- Consumes: the Task-2 VM members (`HasPathStats`, `HasSignificantFemaleVariant`, `LongestPlaythroughText`, `ShortestPlaythroughText`, `TotalContentText`, `WordsPerSpeaker`, `Branches`, each row's `ChoiceText`/`DefaultContentText`/`DefaultLongestText`/`FemaleContentText`/`FemaleLongestText`/`NavigateCommand`).

- [ ] **Step 1: Insert the Playthrough stats block**

In `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`, inside the Grid.Row 0 statistics `StackPanel`, insert this block **between** the stats `Separator` (`Margin="0,2,0,2"`) and the issues header (`<TextBlock Classes="section-header" Text="{Binding StatusText}"/>`):

```xml
            <!-- ── Playthrough stats ─────────────────────────────────────── -->
            <StackPanel Spacing="4" IsVisible="{Binding HasPathStats}">
                <TextBlock Classes="section-header"
                           Text="{DynamicResource PathStats_Header}"
                           ToolTip.Tip="{DynamicResource ToolTip_PathStats}"
                           AutomationProperties.HelpText="{DynamicResource ToolTip_PathStats}"/>

                <Grid ColumnDefinitions="*,*,*">
                    <StackPanel Grid.Column="0">
                        <TextBlock Classes="stat-label" Text="{DynamicResource PathStats_Longest}"/>
                        <TextBlock Foreground="{DynamicResource Brush.Text.Primary}"
                                   FontSize="{DynamicResource FontSize.Body}"
                                   Text="{Binding LongestPlaythroughText}" TextWrapping="Wrap"/>
                    </StackPanel>
                    <StackPanel Grid.Column="1">
                        <TextBlock Classes="stat-label" Text="{DynamicResource PathStats_Shortest}"/>
                        <TextBlock Foreground="{DynamicResource Brush.Text.Primary}"
                                   FontSize="{DynamicResource FontSize.Body}"
                                   Text="{Binding ShortestPlaythroughText}" TextWrapping="Wrap"/>
                    </StackPanel>
                    <StackPanel Grid.Column="2">
                        <TextBlock Classes="stat-label" Text="{DynamicResource PathStats_Total}"/>
                        <TextBlock Foreground="{DynamicResource Brush.Text.Primary}"
                                   FontSize="{DynamicResource FontSize.Body}"
                                   Text="{Binding TotalContentText}" TextWrapping="Wrap"/>
                    </StackPanel>
                </Grid>

                <TextBlock Classes="stat-label" Text="{DynamicResource PathStats_WordsPerSpeaker}" Margin="0,4,0,0"/>
                <ItemsControl ItemsSource="{Binding WordsPerSpeaker}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="vm:SpeakerWordRowViewModel">
                            <TextBlock Text="{Binding Display}"
                                       Foreground="{DynamicResource Brush.Text.Secondary}"
                                       FontSize="{DynamicResource FontSize.Label}"/>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <TextBlock Classes="stat-label" Text="{DynamicResource PathStats_OpeningChoices}" Margin="0,4,0,0"/>
                <ScrollViewer MaxHeight="140">
                    <ItemsControl ItemsSource="{Binding Branches}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate DataType="vm:PathBranchRowViewModel">
                                <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,2">
                                    <StackPanel Grid.Column="0" VerticalAlignment="Center">
                                        <TextBlock Text="{Binding ChoiceText}"
                                                   Foreground="{DynamicResource Brush.Text.Primary}"
                                                   FontSize="{DynamicResource FontSize.Label}"
                                                   TextTrimming="CharacterEllipsis"/>
                                        <StackPanel Orientation="Horizontal" Spacing="10">
                                            <TextBlock Text="{Binding DefaultContentText}"
                                                       Foreground="{DynamicResource Brush.Text.Muted}"
                                                       FontSize="{DynamicResource FontSize.Small}"/>
                                            <TextBlock Text="{Binding DefaultLongestText}"
                                                       Foreground="{DynamicResource Brush.Text.Muted}"
                                                       FontSize="{DynamicResource FontSize.Small}"/>
                                        </StackPanel>
                                        <StackPanel Orientation="Horizontal" Spacing="10"
                                                    IsVisible="{Binding $parent[ItemsControl].DataContext.HasSignificantFemaleVariant}">
                                            <TextBlock Text="{Binding FemaleContentText}"
                                                       Foreground="{DynamicResource Brush.Text.Tertiary}"
                                                       FontSize="{DynamicResource FontSize.Small}"/>
                                            <TextBlock Text="{Binding FemaleLongestText}"
                                                       Foreground="{DynamicResource Brush.Text.Tertiary}"
                                                       FontSize="{DynamicResource FontSize.Small}"/>
                                        </StackPanel>
                                    </StackPanel>
                                    <Button Grid.Column="2" Content="→"
                                            Command="{Binding NavigateCommand}"
                                            Background="Transparent" BorderThickness="0"
                                            Foreground="{DynamicResource Brush.Text.Muted}"
                                            FontSize="{DynamicResource FontSize.Subtitle}"
                                            Padding="8,0" Margin="4,0,0,0" VerticalAlignment="Center"
                                            ToolTip.Tip="{DynamicResource ToolTip_PathStats_Navigate}"
                                            AutomationProperties.HelpText="{DynamicResource ToolTip_PathStats_Navigate}"
                                            AutomationProperties.Name="{DynamicResource AutomationName_GoToNode}"/>
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>

                <Separator Background="{DynamicResource Brush.Border.Default}" Height="1" Margin="0,2,0,2"/>
            </StackPanel>
```

Note: `$parent[ItemsControl].DataContext.HasSignificantFemaleVariant` reaches the VM (the `ItemsControl`'s DataContext is the `FlowAnalyticsViewModel`) — the window is `x:CompileBindings="False"`, so this resolves at runtime. `AutomationName_GoToNode` already exists (used by the issue rows).

- [ ] **Step 2: Build the app**

Run: `dotnet build DialogEditor.Avalonia`
Expected: Build succeeded. If AVLN errors mention `SpeakerWordRowViewModel`/`PathBranchRowViewModel` not found, confirm they are `public` and in the `DialogEditor.ViewModels` namespace (the `vm:` alias).

- [ ] **Step 3: Full test suite (structural enforcers)**

Run: `dotnet test DialogEditor.Tests`
Expected: all pass, including `AutomationHelpTextTests`, `AutomationNameTests`, `NoStrayHexTests`, `NoNamedColourForegroundTests`, `NoStaticStringResourceTests`, `NoStaticFontSizeResourceTests`, `FocusHintBarPresenceTests`. Fix any offending markup (a missing tooltip/help-text, a literal colour/fontsize) rather than weakening the test.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml
git commit -m "feat(path-stats): Playthrough stats section in the Flow Analytics window"
```

---

## Task 4: Live verification + Gaps.md

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Drive the app**

Use the `running-the-app` skill. Open a conversation with branching player choices (a shipped PoE2 conversation, or a scratch project with a hand-built branchy conversation), then `Test ▸ Flow Analytics` (F7) → Refresh and verify:
1. The **Playthrough stats** section shows longest / shortest / total content with `N words · m:ss`.
2. **Words per speaker** lists each character with their word total.
3. **By opening choice** lists each opening player choice with content + longest, and the **→** button jumps to that node.
4. If the conversation has a materially different female variant, the female figures appear; otherwise they don't.
Screenshot the section.

- [ ] **Step 2: Update `Gaps.md`**

In the **Smaller Writer/UX Backlog** section, replace the Path-based-writing-stats bullet with a `✓ Implemented (2026-07-13)` entry: a Playthrough stats section in Flow Analytics (pure `PathStatsService`) — back-edge-cut DAG longest/shortest playthrough, words-per-speaker, and per-opening-choice content + longest-read rows with jump-to-node, plus a Default/Female two-reading model gated at a 10% word difference. Cite the spec.

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark path-based writing stats implemented"
```

---

## Self-Review

**Spec coverage:**
- Flow Analytics home, new section, per-conversation snapshot → Tasks 2–3. ✓
- `PathStatsService` in Core, back-edge-cut DAG, longest/shortest, per-opening-choice content + longest, words-per-speaker → Task 1. ✓
- Two readings (Default/Female) + 10% significance gate, female-aware words-per-speaker → Task 1 (`Fem`, `significant`, `SpeakerWordCount.FemaleWords`). ✓
- 200 wpm reading time `m:ss` → Task 2 (`PathStatsFormat`). ✓
- Words = Default split rule; player-choice text counts → Task 1 (`Words`/`Def`). ✓
- Speaker names resolved in VM via `SpeakerNameService`, category fallback → Task 2 (`ResolveSpeakerName`). ✓
- Conditional Female columns in the UI → Task 3 (`IsVisible` on `HasSignificantFemaleVariant`). ✓
- Navigation reuses `_navigateToNode` → Task 2 branch rows. ✓
- Edge cases (empty, no root, unreachable) → Task 1 tests. ✓
- Localisation / tooltips / automation → Tasks 2–3. ✓
- Testing (service, format helper, VM, structural) → Tasks 1–3. ✓
- Live verification → Task 4. ✓

**Placeholder scan:** none — all code shown in full.

**Type consistency:** `PathStatsReport`/`BranchStat`/`SpeakerWordCount` fields (Task 1) match their use in Task 2's `RefreshPathStats` and the tests. `PathStatsFormat.ReadingTime` (Task 2 Step 3) matches its test (Step 1) and its VM use. VM members (`HasPathStats`, `HasSignificantFemaleVariant`, `LongestPlaythroughText`, `ShortestPlaythroughText`, `TotalContentText`, `WordsPerSpeaker`, `Branches`) and the row-VM properties (`ChoiceText`, `DefaultContentText`, `DefaultLongestText`, `FemaleContentText`, `FemaleLongestText`, `NavigateCommand`, `Display`) are consistent across Task 2 (definition), Task 3 (XAML bindings), and the tests.

**Deviations from spec (noted):** the "Total content" figure (the header's third stat) uses the whole-conversation total words (Default/Female) rather than a distinct "total playthrough" — the spec lists "total words" as a header item, so this matches. Branch longest/content are measured from the choice onward (root's shared words excluded), as the spec's per-branch section describes; the overall longest/shortest include the root. Both documented in the service remarks.
