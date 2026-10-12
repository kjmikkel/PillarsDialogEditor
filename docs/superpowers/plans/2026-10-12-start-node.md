# Conversations Without a Start Node Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Warn clearly when a conversation has nodes but no node 0. For conversations created in the editor, add an undoable "make this the start node" fix that renumbers the chosen node to 0.

**Architecture:**
- **Core:**
  - A `StartNode` helper decides whether a conversation has its start node.
  - Flow Analytics and Path Stats report `HasStartNode` instead of flagging every node.
- **ViewModels:**
  - One `MakeStartNodeCommand` on the canvas undo stack renumbers the node in place. It moves the node comment and any staged VO files, and records an ID remap.
  - `MainWindowViewModel` applies the recorded remaps to the other languages' translations when it folds the canvas into the project.
- **Avalonia:** a warning strip under the canvas toolbar, with a pick mode, and a node context-menu item.

**Tech Stack:** C# / .NET 10, Avalonia 11, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-12-start-node-design.md`

## Global Constraints

- **Running the tests:** always `dotnet test DialogEditor.Tests --filter "Category!=Gui"`, never a bare `dotnet test`, which launches the GUI. Build without `-warnaserror`.
- **Strict red/green TDD.** Every test is run and seen to fail before the implementation is written.
- **No hard-coded user-visible text** in C# or XAML. Every string goes in `DialogEditor.Avalonia/Resources/Strings.axaml` and is used through `Loc.Get` / `Loc.Format` or `{DynamicResource}`.
- **Every new interactive control** has a `ToolTip.Tip` and a UIA name: `AutomationProperties.Name`, or a menu `Header`.
- **Caught exceptions** in production code are logged with `AppLog.Error` / `AppLog.Warn`. No bare `catch { }`.
- **Production comments** write "issue 172", never "#172": `NoStrayHexTests` flags `#NNN`.
- **Editing files from Python:** preserve the UTF-8 BOM and CRLF line endings. Read the bytes, strip the BOM, normalise to `\n`, edit, then restore both.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Out of scope** (spec): references from other conversations to the old ID are counted, never rewritten.

## Review Focus

1. **A stale translation already stored under key 0** (left by an earlier, deleted node 0). The remap must replace it, not leave two entries for node 0. Test: Task 4, `Apply_DropsStaleEntriesAlreadyAtTheTarget`.
2. **Renumbering, then switching conversation and discarding the changes.** The pending remaps must not leak into the next conversation's save. Test: Task 5, `Clear_DropsPendingRemaps`.
3. **Undo, then Redo, with staged VO.** The files must follow the node each way, and a failure during Undo or Redo must not lose the command from the undo history. Test: Task 5, `Vo_UndoRedo_MovesFilesBackAndForth`.
4. **Pick mode left on after node 0 reappears some other way** (Undo of a delete, for example). The mode must end. Test: Task 6, `PickMode_EndsWhenNode0Appears`.
5. **Save, Undo, save.** Translations must move back to the old ID. Test: Task 8, `Save_Undo_Save_MovesTranslationsBack`.

**Narrowed from the spec, decided while planning:**
- **The no-start-node error in Flow Analytics is not a row in the issue list.** It is an error line at the top of the issues section. `FlowIssue` needs a node, and this error has none.
- **The reference scanner only reads the project's stored patches.** The only conversation open on the canvas is the one being renumbered, so every other conversation's state is in `_project`.
- **The end-to-end check stops at the patch.** It applies the saved patch with `PatchApplier` to the empty baseline. `PatchInstaller` writes exactly what `PatchApplier` and the serializers produce, and both are already covered for new conversations (issue 162).

---

### Task 1: Flow Analytics knows when the start node is missing

**Files:**
- Create: `DialogEditor.Core/Analytics/StartNode.cs`
- Modify: `DialogEditor.Core/Analytics/FlowAnalysisModels.cs` (`FlowAnalysisReport`)
- Modify: `DialogEditor.Core/Analytics/FlowAnalysisService.cs`
- Test: `DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs`, `DialogEditor.Tests/Analytics/StartNodeTests.cs`

**Interfaces:**
- Produces:
  - `StartNode.Id` (`const int`, 0);
  - `StartNode.IsPresent(IEnumerable<int> nodeIds) : bool`;
  - `FlowAnalysisReport.HasStartNode { get; init; } = true`.

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Analytics/StartNodeTests.cs`:

```csharp
using DialogEditor.Core.Analytics;

namespace DialogEditor.Tests.Analytics;

/// Issue 172: both games start a conversation at node 0.
public class StartNodeTests
{
    [Fact]
    public void Present_WhenNode0Exists() => Assert.True(StartNode.IsPresent([3, 0, 7]));

    [Fact]
    public void Missing_WhenNodesExistButNoneIsNode0() => Assert.False(StartNode.IsPresent([1, 2]));

    // An empty conversation's first node will be node 0 (NodeIdAllocator), so nothing is missing.
    [Fact]
    public void Present_ForAnEmptyConversation() => Assert.True(StartNode.IsPresent([]));
}
```

In `FlowAnalysisServiceTests.cs`, replace `Analyze_NoRootNode_AllNodesUnreachable` with:

```csharp
    // ── Edge case: no start node (issue 172) ──────────────────────────────

    [Fact]
    public void Analyze_NoNode0_ReportsNoStartNode_AndNoUnreachable()
    {
        var snapshot = Snapshot(
            MakeNode(1, defaultText: "a", links: [Link(1, 2)]),
            MakeNode(2, defaultText: "b"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.False(report.HasStartNode);
        Assert.DoesNotContain(report.Issues, i => i.Kind == FlowIssueKind.Unreachable);
    }

    [Fact]
    public void Analyze_NoNode0_OtherChecksStillRun()
    {
        var snapshot = Snapshot(
            MakeNode(1, links: [Link(1, 2)]),                    // empty text
            MakeNode(2, isPlayerChoice: true, defaultText: "b")); // player dead end

        var kinds = FlowAnalysisService.Analyze(snapshot).Issues.Select(i => i.Kind).ToList();

        Assert.Contains(FlowIssueKind.EmptyText, kinds);
        Assert.Contains(FlowIssueKind.PlayerDeadEnd, kinds);
        Assert.Contains(FlowIssueKind.NoIncomingLinks, kinds);   // node 1: the would-be start
    }

    [Fact]
    public void Analyze_WithNode0_HasStartNode() =>
        Assert.True(FlowAnalysisService.Analyze(Snapshot(MakeNode(0, defaultText: "a"))).HasStartNode);

    [Fact]
    public void Analyze_Empty_HasStartNode() =>
        Assert.True(FlowAnalysisService.Analyze(Snapshot()).HasStartNode);
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&(FullyQualifiedName~StartNodeTests|FullyQualifiedName~FlowAnalysisServiceTests)"`
Expected: a build error. `StartNode` and `HasStartNode` don't exist yet.

- [ ] **Step 3: Implement**

`DialogEditor.Core/Analytics/StartNode.cs`:

```csharp
namespace DialogEditor.Core.Analytics;

/// Both games start a conversation at node 0 unless a script names another node: PoE1's
/// and Deadfire's StartConversation default nodeID to 0 (issues 162, 172).
public static class StartNode
{
    public const int Id = 0;

    /// False only when the conversation has nodes and none of them is node 0. An empty
    /// conversation is fine: its first node will be node 0 (NodeIdAllocator).
    public static bool IsPresent(IEnumerable<int> nodeIds)
    {
        var any = false;
        foreach (var id in nodeIds)
        {
            if (id == Id) return true;
            any = true;
        }
        return !any;
    }
}
```

In `FlowAnalysisModels.cs`, change `FlowAnalysisReport` to:

```csharp
public record FlowAnalysisReport(
    FlowStatistics           Statistics,
    IReadOnlyList<FlowIssue> Issues)
{
    /// False when the conversation has nodes but no node 0 (issue 172). Unreachable is then
    /// not reported: walking from a start node that doesn't exist would flag every node.
    public bool HasStartNode { get; init; } = true;
}
```

In `FlowAnalysisService.Analyze`:
- After the early return for an empty snapshot, add:

  ```csharp
          var hasStartNode = StartNode.IsPresent(nodes.Select(n => n.NodeId));
  ```

- In the BFS block, replace the literal `0` with `StartNode.Id`. The comment becomes `// ── BFS from the start node (StartNode.Id) ──`.
- Change the Unreachable check to:

  ```csharp
              if (hasStartNode && !reachable.Contains(node.NodeId))
                  issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.Unreachable));
  ```

- Change `node.NodeId != 0` in the NoIncomingLinks check to `node.NodeId != StartNode.Id`.
- Change the final return to:

  ```csharp
          return new FlowAnalysisReport(stats, issues) { HasStartNode = hasStartNode };
  ```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the same command as Step 2.
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/Analytics DialogEditor.Tests/Analytics
git commit -m "feat: Flow Analytics reports a missing start node instead of flagging every node (#172)"
```

---

### Task 2: Path Stats knows when the start node is missing

**Files:**
- Modify: `DialogEditor.Core/Analytics/PathStatsModels.cs` (`PathStatsReport`)
- Modify: `DialogEditor.Core/Analytics/PathStatsService.cs`
- Test: `DialogEditor.Tests/Analytics/PathStatsServiceTests.cs`

**Interfaces:**
- Consumes: `StartNode.Id` (Task 1).
- Produces: `PathStatsReport.HasStartNode { get; init; } = true`.

- [ ] **Step 1: Write the failing tests** (append to `PathStatsServiceTests`)

```csharp
    // ── Issue 172: no start node ─────────────────────────────────────────

    [Fact]
    public void NoNode0_HasNoStartNode_ButStillCountsWords()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(1, "one two", links: [Link(1, 2)]),
            Node(2, "three")));

        Assert.False(report.HasStartNode);
        Assert.Equal(3, report.DefaultTotalWords);
        Assert.NotEmpty(report.WordsPerSpeaker);
    }

    [Fact]
    public void WithNode0_HasStartNode() =>
        Assert.True(PathStatsService.Analyze(Snap(Node(0, "a"))).HasStartNode);

    [Fact]
    public void Empty_HasStartNode() =>
        Assert.True(PathStatsService.Analyze(Snap()).HasStartNode);
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~PathStatsServiceTests"`
Expected: a build error. `HasStartNode` doesn't exist yet.

- [ ] **Step 3: Implement**

In `PathStatsModels.cs`, give `PathStatsReport` a body:

```csharp
public record PathStatsReport(
    /* …existing positional parameters unchanged… */
    int                             ConversationsSpanned)
{
    /// False when the root conversation has nodes but no node 0 (issue 172): there is no
    /// playthrough to measure, though the word totals still count every line.
    public bool HasStartNode { get; init; } = true;
}
```

In `PathStatsService.Analyze(MultiConversationGraph graph)`:
- Replace `new NodeRef(graph.RootConversation, 0)` with `new NodeRef(graph.RootConversation, StartNode.Id)`.
- Change the early return guarded by `if (!nodeById.ContainsKey(root))` to:

  ```csharp
          if (!nodeById.ContainsKey(root))
              return new PathStatsReport(significant, defaultTotal, femaleTotal, 0, 0, 0, 0,
                  wordsPerSpeaker, [], [], graph.Unfollowed, spanned) { HasStartNode = false };
  ```

- Update the doc comment that mentions node 0 to name `StartNode.Id`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the same command as Step 2.
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/Analytics DialogEditor.Tests/Analytics
git commit -m "feat: Path Stats reports a missing start node (#172)"
```

---

### Task 3: The Flow Analytics window shows the error

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`
- Modify: `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs`

**Interfaces:**
- Consumes: `FlowAnalysisReport.HasStartNode` and `PathStatsReport.HasStartNode`.
- Produces: `FlowAnalyticsViewModel.HasNoStartNode` (bool, observable) and `FlowAnalyticsViewModel.PathStatsNoStartNode` (bool, observable).

- [ ] **Step 1: Write the failing tests** (append to `FlowAnalyticsViewModelTests`)

```csharp
    // ── Issue 172 ─────────────────────────────────────────────────────────

    private static ConversationEditSnapshot NoNode0Snapshot() => new([
        MakeNode(1, defaultText: "Hello", links: [Link(1, 2)]),
        MakeNode(2, defaultText: "World")
    ]);

    [Fact]
    public void Refresh_NoNode0_ShowsNoStartNode_AndNoUnreachableRows()
    {
        var vm = new FlowAnalyticsViewModel(() => NoNode0Snapshot(), _ => { });
        vm.RefreshCommand.Execute(null);

        Assert.True(vm.HasNoStartNode);
        Assert.True(vm.PathStatsNoStartNode);
        Assert.DoesNotContain(vm.Issues, i => i.Kind == FlowIssueKind.Unreachable);
    }

    [Fact]
    public void Refresh_NoNode0_CountsTheErrorInTheStatus()
    {
        // A 1 <-> 2 cycle with text has no other issue, so only the no-start-node error can
        // turn "no issues" into "issues".
        var cycle = new ConversationEditSnapshot([
            MakeNode(1, defaultText: "Hello", links: [Link(1, 2)]),
            MakeNode(2, defaultText: "World", links: [Link(2, 1)])
        ]);
        var vm = new FlowAnalyticsViewModel(() => cycle, _ => { });
        vm.RefreshCommand.Execute(null);

        Assert.Equal("FlowAnalytics_Issues", vm.StatusText);   // stub: the key, not "NoIssues"
    }

    [Fact]
    public void Refresh_WithNode0_HidesNoStartNode()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });
        vm.RefreshCommand.Execute(null);

        Assert.False(vm.HasNoStartNode);
        Assert.False(vm.PathStatsNoStartNode);
    }
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~FlowAnalyticsViewModelTests"`
Expected: a build error. `HasNoStartNode` and `PathStatsNoStartNode` don't exist yet.

- [ ] **Step 3: Implement the view model**

In `FlowAnalyticsViewModel`, next to `_hasData`:

```csharp
    /// The conversation has nodes but no node 0 (issue 172): one error replaces the
    /// Unreachable flag every node would otherwise get.
    [ObservableProperty] private bool _hasNoStartNode;
```

Next to `_hasPathStats`:

```csharp
    [ObservableProperty] private bool _pathStatsNoStartNode;
```

In `Refresh()`:
- After `HasData = true;` add `HasNoStartNode = !report.HasStartNode;`.
- Replace the issue-count block with:

  ```csharp
          var issueCount = report.Issues.Count + (report.HasStartNode ? 0 : 1);
  ```

In `RefreshPathStats`, before `HasPathStats = …`, add:

```csharp
        PathStatsNoStartNode = !report.HasStartNode;
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the same command as Step 2.
Expected: all PASS.

- [ ] **Step 5: Add the strings and the markup**

In `Strings.axaml`, next to `FlowAnalytics_Issue_Unreachable`:

```xml
    <sys:String x:Key="FlowAnalytics_Issue_NoStartNode">No start node: the game starts this conversation at node 0, which doesn't exist.</sys:String>
    <sys:String x:Key="ToolTip_FlowAnalytics_NoStartNode">Both games start a conversation at node 0. For a conversation you created, use "Choose start node…" above the canvas to make one of its nodes node 0. For a game conversation, undo the deletion of node 0, or remove the deletion from the patch.</sys:String>
    <sys:String x:Key="PathStats_NoStartNode">No start node, so there are no playthroughs to measure. The word totals still count every line.</sys:String>
```

In `FlowAnalyticsWindow.axaml`, directly under the Issues header `<TextBlock Classes="section-header" Text="{Binding StatusText}"/>`:

```xml
            <!-- Issue 172: one conversation-level error instead of "Unreachable" on every node. -->
            <Border Background="{DynamicResource Brush.Validation.Background}" BorderBrush="{DynamicResource Brush.Validation.Border}" BorderThickness="1"
                    CornerRadius="2" Padding="6,4" Margin="0,2,0,2"
                    IsVisible="{Binding HasNoStartNode}"
                    ToolTip.Tip="{DynamicResource ToolTip_FlowAnalytics_NoStartNode}"
                    AutomationProperties.Name="{DynamicResource FlowAnalytics_Issue_NoStartNode}"
                    AutomationProperties.HelpText="{DynamicResource ToolTip_FlowAnalytics_NoStartNode}">
                <TextBlock Text="{DynamicResource FlowAnalytics_Issue_NoStartNode}"
                           Foreground="{DynamicResource Brush.Validation.Text}"
                           FontSize="{DynamicResource FontSize.Small}" TextWrapping="Wrap"/>
            </Border>
```

Inside the Path Stats panel, as the first child of `<StackPanel Spacing="4" IsVisible="{Binding HasPathStats}">`:

```xml
                <TextBlock Text="{DynamicResource PathStats_NoStartNode}"
                           IsVisible="{Binding PathStatsNoStartNode}"
                           Foreground="{DynamicResource Brush.Validation.Text}"
                           FontSize="{DynamicResource FontSize.Small}" TextWrapping="Wrap"/>
```

Also wrap the Longest/Shortest `<Grid ColumnDefinitions="*,*,*">` so it's hidden when there is no start node: add `IsVisible="{Binding !PathStatsNoStartNode}"` to that `Grid`.

- [ ] **Step 6: Build, then run the localisation and house tests**

Run: `dotnet build DialogEditor.Avalonia` and then `dotnet test DialogEditor.Tests --filter "Category!=Gui&(FullyQualifiedName~Locali|FullyQualifiedName~NoHardcoded|FullyQualifiedName~FlowAnalytics)"`
Expected: the build succeeds and the tests PASS.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.ViewModels DialogEditor.Avalonia DialogEditor.Tests
git commit -m "feat: Flow Analytics window shows the missing start node (#172)"
```

---

### Task 4: Re-keying translations after a renumber

**Files:**
- Create: `DialogEditor.Patch/NodeIdRemap.cs`
- Test: `DialogEditor.Tests/Patch/NodeIdRemapTests.cs`

**Interfaces:**
- Produces: `NodeIdRemap.Apply(IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> translations, IReadOnlyList<(int From, int To)> remaps) : IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>`

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/Patch/NodeIdRemapTests.cs` (if the folder holding Patch tests has a different name, put the file there and match its namespace):

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

/// Issue 172: a renumbered node's translations in the other languages live only in the
/// stored patch, and the save drops entries whose node is gone, so they must be re-keyed.
public class NodeIdRemapTests
{
    private static Dictionary<string, IReadOnlyList<NodeTranslation>> Fr(params NodeTranslation[] t) =>
        new() { ["fr"] = t };

    [Fact]
    public void Apply_MovesTheEntryToTheNewId()
    {
        var result = NodeIdRemap.Apply(Fr(new NodeTranslation(4, "salut", "")), [(4, 0)]);
        Assert.Equal(0, Assert.Single(result["fr"]).NodeId);
    }

    [Fact]
    public void Apply_RunsRemapsInOrder()   // renumber, then Undo, before a save
    {
        var result = NodeIdRemap.Apply(Fr(new NodeTranslation(4, "salut", "")), [(4, 0), (0, 4)]);
        Assert.Equal(4, Assert.Single(result["fr"]).NodeId);
    }

    [Fact]
    public void Apply_DropsStaleEntriesAlreadyAtTheTarget()
    {
        // A translation left over from a deleted node 0 must not survive beside the moved one.
        var result = NodeIdRemap.Apply(
            Fr(new NodeTranslation(0, "old", ""), new NodeTranslation(4, "salut", "")), [(4, 0)]);
        var only = Assert.Single(result["fr"]);
        Assert.Equal((0, "salut"), (only.NodeId, only.DefaultText));
    }

    [Fact]
    public void Apply_LeavesOtherEntriesAlone()
    {
        var result = NodeIdRemap.Apply(
            Fr(new NodeTranslation(4, "salut", ""), new NodeTranslation(5, "ça va", "")), [(4, 0)]);
        Assert.Contains(result["fr"], t => t.NodeId == 5 && t.DefaultText == "ça va");
    }

    [Fact]
    public void Apply_NoRemaps_ReturnsTheSameEntries()
    {
        var input = Fr(new NodeTranslation(4, "salut", ""));
        Assert.Same(input, NodeIdRemap.Apply(input, []));
    }
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~NodeIdRemapTests"`
Expected: a build error. `NodeIdRemap` doesn't exist yet.

- [ ] **Step 3: Implement** `DialogEditor.Patch/NodeIdRemap.cs`

```csharp
using DialogEditor.Core.Editing;

namespace DialogEditor.Patch;

/// Re-keys stored translations after the canvas renumbered nodes (issue 172). The canvas
/// language's text moves with the node itself; the other languages exist only in the stored
/// patch, and FoldCanvasIntoProject drops entries whose node no longer exists.
public static class NodeIdRemap
{
    public static IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> Apply(
        IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> translations,
        IReadOnlyList<(int From, int To)> remaps)
    {
        if (remaps.Count == 0) return translations;

        var result = new Dictionary<string, IReadOnlyList<NodeTranslation>>();
        foreach (var (lang, entries) in translations)
        {
            var list = entries.ToList();
            foreach (var (from, to) in remaps)
            {
                // The target ID had no node before the renumber, so anything stored under it
                // is stale; drop it rather than keep two entries for one node.
                if (!list.Any(t => t.NodeId == from)) continue;
                list.RemoveAll(t => t.NodeId == to);
                list = [.. list.Select(t => t.NodeId == from ? t with { NodeId = to } : t)];
            }
            result[lang] = list;
        }
        return result;
    }
}
```

Note: the `continue` keeps a stale target entry when nothing moves onto it. That is right, because then nothing replaced it.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the same command as Step 2.
Expected: all 5 PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/NodeIdRemap.cs DialogEditor.Tests
git commit -m "feat: re-key stored translations after a node renumber (#172)"
```

---

### Task 5: The renumber edit (`MakeStartNodeCommand`)

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs` (`NodeId` setter)
- Create: `DialogEditor.ViewModels/Editing/MakeStartNodeCommand.cs`
- Create: `DialogEditor.ViewModels/Services/StartNodeVoFiles.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (`Undo_MakeStartNode`)
- Test: `DialogEditor.Tests/ViewModels/MakeStartNodeTests.cs`

**Interfaces:**
- Consumes: `StartNode.Id` and `StartNode.IsPresent` (Task 1).
- Produces:
  - `enum StartNodeOutcome { Made, NotAllowed, StartNodeExists, VoTargetExists, VoRenameFailed }` (namespace `DialogEditor.ViewModels`);
  - `sealed record StartNodeChangedEventArgs(StartNodeOutcome Outcome, int OldNodeId, string? OldVoPath, string Detail = "")`;
  - `ConversationViewModel`:
    - `bool HasStartNode` (observable);
    - `bool CanChooseStartNode` (observable, set by the main window);
    - `StartNodeOutcome MakeStartNode(NodeViewModel node)`;
    - `event EventHandler<StartNodeChangedEventArgs>? StartNodeAttempted`;
    - `internal IReadOnlyList<(int From, int To)> TakePendingRemaps()`;
  - `NodeViewModel.Renumber(int newId)` (internal).

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/ViewModels/MakeStartNodeTests.cs`:

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Issue 172: making a node the start node renumbers it to 0 as one undoable edit.
public class MakeStartNodeTests : IDisposable
{
    private const string NarratorGuid = "6a99a109-0000-0000-0000-000000000000";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"startnode_{Guid.NewGuid():N}");

    public MakeStartNodeTests()
    {
        Loc.Configure(new StubStringProvider());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private static ConversationNode Node(int id, params int[] to) =>
        new(id, false, SpeakerCategory.Npc, NarratorGuid, "", [.. to.Select(t => new NodeLink(id, t, []))],
            [], [], "Conversation", "None");

    /// "fresh": node 1 → node 2, as a pre-issue-162 new conversation.
    private ConversationViewModel Canvas(bool canChoose = true)
    {
        var vm = new ConversationViewModel(new StubDispatcher())
        {
            IsEditable  = true,
            ProjectPath = Path.Combine(_dir, "proj.dialogproject"),
        };
        vm.Load(new Conversation("fresh", [Node(1, 2), Node(2)],
            new StringTable([new StringEntry(1, "first", ""), new StringEntry(2, "second", "")])));
        vm.CanChooseStartNode = canChoose;
        return vm;
    }

    private static NodeViewModel ById(ConversationViewModel vm, int id) => vm.Nodes.Single(n => n.NodeId == id);

    [Fact]
    public void Load_WithoutNode0_HasNoStartNode() => Assert.False(Canvas().HasStartNode);

    [Fact]
    public void Make_RenumbersTheNode_AndTheLinksFollow()
    {
        var vm   = Canvas();
        var node = ById(vm, 1);

        Assert.Equal(StartNodeOutcome.Made, vm.MakeStartNode(node));

        Assert.Equal(0, node.NodeId);
        Assert.True(vm.HasStartNode);
        var snap = vm.BuildSnapshot();
        var n0   = snap.Nodes.Single(n => n.NodeId == 0);
        Assert.Equal("first", n0.DefaultText);
        Assert.Equal(2, Assert.Single(n0.Links).ToNodeId);
        Assert.Equal(0, Assert.Single(n0.Links).FromNodeId);
        Assert.DoesNotContain(snap.Nodes, n => n.NodeId == 1);
    }

    [Fact]
    public void Make_MovesTheNodeComment()
    {
        var vm = Canvas();
        vm.LoadNodeComments(new Dictionary<int, string> { [1] = "translator note" });

        vm.MakeStartNode(ById(vm, 1));

        Assert.Equal("translator note", vm.GetNodeComment(0));
        Assert.Equal("", vm.GetNodeComment(1));
    }

    [Fact]
    public void Make_IsOneUndoStep_AndRedoRepeatsIt()
    {
        var vm   = Canvas();
        var node = ById(vm, 1);
        vm.LoadNodeComments(new Dictionary<int, string> { [1] = "note" });
        vm.MakeStartNode(node);

        vm.Undo();
        Assert.Equal(1, node.NodeId);
        Assert.Equal("note", vm.GetNodeComment(1));
        Assert.False(vm.HasStartNode);
        Assert.False(vm.CanUndo);

        vm.Redo();
        Assert.Equal(0, node.NodeId);
        Assert.True(vm.HasStartNode);
    }

    [Fact]
    public void Make_RecordsRemaps_ForTheSave()
    {
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));
        vm.Undo();

        Assert.Equal(new[] { (1, 0), (0, 1) }, vm.TakePendingRemaps());
        Assert.Empty(vm.TakePendingRemaps());   // taking clears them
    }

    [Fact]
    public void Clear_DropsPendingRemaps()
    {
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));
        vm.Clear();
        Assert.Empty(vm.TakePendingRemaps());
    }

    [Fact]
    public void Make_RaisesStartNodeAttempted()
    {
        var vm = Canvas();
        StartNodeChangedEventArgs? seen = null;
        vm.StartNodeAttempted += (_, e) => seen = e;

        vm.MakeStartNode(ById(vm, 1));

        Assert.Equal(StartNodeOutcome.Made, seen!.Outcome);
        Assert.Equal(1, seen.OldNodeId);
        Assert.Equal(Path.Combine("narrator", "fresh_0001"), seen.OldVoPath);
    }

    [Fact]
    public void Make_RefusedForAShippedConversation()
    {
        var vm = Canvas(canChoose: false);
        Assert.Equal(StartNodeOutcome.NotAllowed, vm.MakeStartNode(ById(vm, 1)));
        Assert.Equal(1, ById(vm, 1).NodeId);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Make_RefusedWhenReadOnly()
    {
        var vm = Canvas();
        vm.IsEditable = false;
        Assert.Equal(StartNodeOutcome.NotAllowed, vm.MakeStartNode(ById(vm, 1)));
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Make_RefusedWhenNode0Exists()
    {
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));
        Assert.Equal(StartNodeOutcome.StartNodeExists, vm.MakeStartNode(ById(vm, 2)));
        Assert.Equal(2, ById(vm, 2).NodeId);
    }

    [Fact]
    public void HasStartNode_FollowsDeleteAndUndo()
    {
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));
        vm.DeleteNode(ById(vm, 0));
        Assert.False(vm.HasStartNode);
        vm.Undo();
        Assert.True(vm.HasStartNode);
    }

    // ── VO staged in _vo/ follows the node ────────────────────────────────

    private string Staged(string name) => Path.Combine(_dir, "_vo", "narrator", name);

    private void Stage(params string[] names)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "_vo", "narrator"));
        foreach (var n in names) File.WriteAllText(Staged(n), n);
    }

    [Fact]
    public void Vo_StagedFilesAreRenamed()
    {
        Stage("fresh_0001.wem", "fresh_0001_fem.wem");
        var vm = Canvas();

        vm.MakeStartNode(ById(vm, 1));

        Assert.True(File.Exists(Staged("fresh_0000.wem")));
        Assert.True(File.Exists(Staged("fresh_0000_fem.wem")));
        Assert.False(File.Exists(Staged("fresh_0001.wem")));
    }

    [Fact]
    public void Vo_UndoRedo_MovesFilesBackAndForth()
    {
        Stage("fresh_0001.wem");
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));

        vm.Undo();
        Assert.True(File.Exists(Staged("fresh_0001.wem")));
        Assert.False(File.Exists(Staged("fresh_0000.wem")));

        vm.Redo();
        Assert.True(File.Exists(Staged("fresh_0000.wem")));
        Assert.True(vm.CanUndo);
    }

    [Fact]
    public void Vo_TargetExists_RefusesAndChangesNothing()
    {
        Stage("fresh_0001.wem", "fresh_0000.wem");
        var vm = Canvas();

        Assert.Equal(StartNodeOutcome.VoTargetExists, vm.MakeStartNode(ById(vm, 1)));

        Assert.Equal(1, ById(vm, 1).NodeId);
        Assert.Equal("fresh_0001.wem", File.ReadAllText(Staged("fresh_0001.wem")));
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Vo_ExternalVoSet_RenamesNothing()
    {
        Stage("fresh_0001.wem");
        var vm = Canvas();
        ById(vm, 1).ExternalVO = "narrator/shared_line";

        vm.MakeStartNode(ById(vm, 1));

        Assert.True(File.Exists(Staged("fresh_0001.wem")));
    }

    [Fact]
    public void Vo_RenameFails_ChangesNothing()
    {
        Stage("fresh_0001.wem");
        var vm = Canvas();
        using (File.Open(Staged("fresh_0001.wem"), FileMode.Open, FileAccess.Read, FileShare.None))   // locked: the move fails
            Assert.Equal(StartNodeOutcome.VoRenameFailed, vm.MakeStartNode(ById(vm, 1)));

        Assert.Equal(1, ById(vm, 1).NodeId);
        Assert.True(File.Exists(Staged("fresh_0001.wem")));
        Assert.False(vm.CanUndo);
    }
}
```

Note: `Vo_ExternalVoSet_RenamesNothing` pushes the ExternalVO edit onto the undo stack. That's fine, because the test only checks the files.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~MakeStartNodeTests"`
Expected: a build error. `MakeStartNode`, `StartNodeOutcome` and the other new members don't exist yet.

- [ ] **Step 3: Implement**

**`NodeViewModel`:** change `public int NodeId { get; }` to `public int NodeId { get; private set; }`, with this comment above it:

```csharp
    // ── Identity ──────────────────────────────────────────────────────────
    // Fixed for the node's life except through MakeStartNodeCommand (issue 172), which
    // renumbers a conversation's chosen first node to 0.
```

Add, next to `ToSnapshot`:

```csharp
    internal void Renumber(int newId)
    {
        NodeId = newId;
        OnPropertyChanged(nameof(NodeId));
        OnPropertyChanged(nameof(Title));
    }
```

**`DialogEditor.ViewModels/Services/StartNodeVoFiles.cs`:**

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels.Services;

/// The staged _vo/ files that follow a node renumbered to the start node (issue 172).
/// Default VO names carry the node ID (<conversation>_<id:0000>.wem), so a renumbered node
/// would otherwise lose its staged audio. A node with ExternalVO names its file explicitly,
/// so it has nothing to move.
internal sealed class StartNodeVoFiles
{
    private readonly IReadOnlyList<(string From, string To)> _moves;

    private StartNodeVoFiles(IReadOnlyList<(string From, string To)> moves) => _moves = moves;

    /// Null when nothing is staged under the old name.
    public static StartNodeVoFiles? For(string? projectPath, NodeViewModel node,
                                        string conversation, int newId)
    {
        if (projectPath is null || !string.IsNullOrEmpty(node.ExternalVO)) return null;
        var oldRel = VoPathResolver.ExpectedRelativePath(node.SpeakerGuid, "", node.NodeId, conversation);
        var newRel = VoPathResolver.ExpectedRelativePath(node.SpeakerGuid, "", newId, conversation);
        if (oldRel is null || newRel is null) return null;

        var voDir = Path.Combine(Path.GetDirectoryName(projectPath)!, "_vo");
        var moves = new List<(string, string)>();
        foreach (var suffix in new[] { ".wem", "_fem.wem" })
        {
            var from = Path.Combine(voDir, oldRel + suffix);
            if (File.Exists(from)) moves.Add((from, Path.Combine(voDir, newRel + suffix)));
        }
        return moves.Count == 0 ? null : new StartNodeVoFiles(moves);
    }

    public string? ExistingTarget => _moves.Select(m => m.To).FirstOrDefault(File.Exists);

    public void Forward() => Move(_moves);
    public void Back()    => Move([.. _moves.Select(m => (m.To, m.From))]);

    /// All or nothing: a failed move puts back the files already moved, then rethrows.
    private static void Move(IReadOnlyList<(string From, string To)> moves)
    {
        var done = new List<(string From, string To)>();
        try
        {
            foreach (var m in moves)
            {
                File.Move(m.From, m.To);
                done.Add(m);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Renaming staged voice-over for the start node failed", ex);
            foreach (var (from, to) in Enumerable.Reverse(done))
            {
                try { File.Move(to, from); }
                catch (Exception undoEx) { AppLog.Error($"Could not move {to} back to {from}", undoEx); }
            }
            throw;
        }
    }
}
```

Remove the redundant `using DialogEditor.ViewModels.Services;` if the compiler flags it. Check the `AppLog.Error(string, Exception)` overload against existing call sites, such as `AppLog.Error("Save As: …", ex)`.

**`DialogEditor.ViewModels/Editing/MakeStartNodeCommand.cs`:**

```csharp
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels.Editing;

/// Renumbers a node to the start node, node 0 (issue 172). Links and layout follow the node
/// object; the comment, the staged VO files and the translation remap are moved here.
internal sealed class MakeStartNodeCommand(
    ConversationViewModel conversation, NodeViewModel node, int oldId, StartNodeVoFiles? vo)
    : IEditCommand
{
    private bool _ranOnce;

    public string Description => Loc.Format("Undo_MakeStartNode", oldId);

    public void Execute()
    {
        // The first run lets a VO failure escape, so UndoRedoStack never pushes the command
        // and MakeStartNode reports it. A Redo must not throw: the stack has already popped
        // the command, so it is logged and the model change goes ahead.
        if (!_ranOnce) vo?.Forward();
        else           TryMove(() => vo?.Forward());
        _ranOnce = true;
        conversation.ApplyRenumber(node, oldId, StartNode.Id);
    }

    public void Undo()
    {
        TryMove(() => vo?.Back());
        conversation.ApplyRenumber(node, StartNode.Id, oldId);
    }

    private static void TryMove(Action move)
    {
        try { move(); }
        catch (Exception ex) { AppLog.Warn($"Start node: voice-over left under its other name: {ex.Message}"); }
    }
}
```

(`StartNodeVoFiles.Move` has already logged the error with `AppLog.Error`. The warning here records that the model went ahead anyway.)

**`ConversationViewModel`:** add the types at the end of the file, or in `StartNodeOutcome.cs` next to `ConnectModeEventArgs` if that's where similar types live:

```csharp
public enum StartNodeOutcome { Made, NotAllowed, StartNodeExists, VoTargetExists, VoRenameFailed }

public sealed record StartNodeChangedEventArgs(
    StartNodeOutcome Outcome, int OldNodeId, string? OldVoPath, string Detail = "");
```

Add the members. Put them under a `// ── Start node (issue 172) ──` region after the structural edit methods:

```csharp
    /// False only when the conversation has nodes and none is node 0: the game would have
    /// nowhere to start it (issue 172).
    [ObservableProperty] private bool _hasStartNode = true;

    /// Set by MainWindowViewModel: true for conversations created in this project. Renumbering
    /// a game conversation's node would turn it into a delete plus an add in the patch.
    [ObservableProperty] private bool _canChooseStartNode;

    public event EventHandler<StartNodeChangedEventArgs>? StartNodeAttempted;

    // ID changes not yet folded into the stored patch's translations (NodeIdRemap).
    private readonly List<(int From, int To)> _pendingRemaps = [];

    internal IReadOnlyList<(int From, int To)> TakePendingRemaps()
    {
        var taken = _pendingRemaps.ToList();
        _pendingRemaps.Clear();
        return taken;
    }

    private void RefreshStartNode() => HasStartNode = StartNode.IsPresent(Nodes.Select(n => n.NodeId));

    public StartNodeOutcome MakeStartNode(NodeViewModel node)
    {
        var oldId    = node.NodeId;
        var oldVo    = string.IsNullOrEmpty(node.ExternalVO)
            ? VoPathResolver.ExpectedRelativePath(node.SpeakerGuid, "", oldId, ConversationName)
            : null;
        StartNodeOutcome Report(StartNodeOutcome outcome, string detail = "")
        {
            StartNodeAttempted?.Invoke(this, new StartNodeChangedEventArgs(outcome, oldId, oldVo, detail));
            return outcome;
        }

        if (!IsEditable || !CanChooseStartNode || !Nodes.Contains(node))
            return Report(StartNodeOutcome.NotAllowed);
        if (HasStartNode)
            return Report(StartNodeOutcome.StartNodeExists);

        var vo = StartNodeVoFiles.For(ProjectPath, node, ConversationName, StartNode.Id);
        if (vo?.ExistingTarget is { } taken)
            return Report(StartNodeOutcome.VoTargetExists, taken);

        try
        {
            _undoStack.Execute(new MakeStartNodeCommand(this, node, oldId, vo));
        }
        catch (Exception ex)   // StartNodeVoFiles has logged it and put the files back
        {
            return Report(StartNodeOutcome.VoRenameFailed, ex.Message);
        }
        IsModified = true;
        RefreshUndoRedo();
        return Report(StartNodeOutcome.Made);
    }

    internal void ApplyRenumber(NodeViewModel node, int from, int to)
    {
        node.Renumber(to);
        if (_nodeComments.Remove(from, out var comment)) _nodeComments[to] = comment;
        _pendingRemaps.Add((from, to));
        RefreshStartNode();
        IsModified = true;
    }
```

Then:
- In the constructor, next to the existing `_undoStack.CommandExecuted += …`, add `Nodes.CollectionChanged += (_, _) => RefreshStartNode();`.
- In `Clear()`, add `_pendingRemaps.Clear();` and `CanChooseStartNode = false;`.
- Add the needed `using`s: `DialogEditor.Core.Analytics`, `DialogEditor.ViewModels.Services` and `DialogEditor.ViewModels.Editing`.
- Make sure `Undo()` and `Redo()` also call `RefreshUndoRedo()`; they already do.

In `Strings.axaml`, next to `Undo_AddNode`:

```xml
    <sys:String x:Key="Undo_MakeStartNode">Make node {0} the start node</sys:String>
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the same command as Step 2.
Expected: all PASS. If `Vo_RenameFailed_ChangesNothing` doesn't fail its move on this platform because the lock isn't honoured, record a ruling and replace the lock with a read-only target directory.

- [ ] **Step 5: Run the canvas test suites**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~ConversationViewModel"`
Expected: PASS. The `Nodes.CollectionChanged` hook must not break the existing tests.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests
git commit -m "feat: make a node the start node as one undoable edit (#172)"
```

---

### Task 6: Pick mode and the context-menu command

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/MakeStartNodeTests.cs` (append)

**Interfaces:**
- Consumes: `MakeStartNode`, `HasStartNode` and `CanChooseStartNode` (Task 5).
- Produces:
  - `ConversationViewModel.IsChoosingStartNode` (observable);
  - `ShowStartNodeWarning` (bool);
  - `StartNodeWarningText` (string);
  - `ChooseStartNodeCommand`, `CancelChooseStartNodeCommand` and `MakeStartNodeCmdCommand` (generated relay commands);
  - `CanMakeStartNode` (bool, for menu visibility).

- [ ] **Step 1: Write the failing tests** (append to `MakeStartNodeTests`)

```csharp
    // ── Pick mode ─────────────────────────────────────────────────────────

    [Fact]
    public void PickMode_SelectingANode_MakesItTheStart_AndEndsTheMode()
    {
        var vm = Canvas();
        vm.ChooseStartNodeCommand.Execute(null);
        Assert.True(vm.IsChoosingStartNode);

        vm.SelectedNode = ById(vm, 2);

        Assert.Equal(0, vm.SelectedNode!.NodeId);
        Assert.False(vm.IsChoosingStartNode);
    }

    [Fact]
    public void PickMode_Cancel_ChangesNothing()
    {
        var vm = Canvas();
        vm.ChooseStartNodeCommand.Execute(null);
        vm.CancelChooseStartNodeCommand.Execute(null);

        Assert.False(vm.IsChoosingStartNode);
        vm.SelectedNode = ById(vm, 2);
        Assert.Equal(2, ById(vm, 2).NodeId);
    }

    [Fact]
    public void PickMode_UnavailableForAShippedConversation()
    {
        var vm = Canvas(canChoose: false);
        Assert.False(vm.ChooseStartNodeCommand.CanExecute(null));
        Assert.False(vm.CanMakeStartNode);
    }

    [Fact]
    public void PickMode_EndsWhenNode0Appears()
    {
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));
        vm.DeleteNode(ById(vm, 0));
        vm.ChooseStartNodeCommand.Execute(null);

        vm.Undo();   // the delete is undone: node 0 is back

        Assert.False(vm.IsChoosingStartNode);
    }

    [Fact]
    public void WarningText_FollowsTheState()
    {
        var vm = Canvas();
        Assert.True(vm.ShowStartNodeWarning);
        Assert.Equal("Canvas_NoStartNode_New", vm.StartNodeWarningText);

        vm.ChooseStartNodeCommand.Execute(null);
        Assert.Equal("Canvas_ChooseStartNode_Prompt", vm.StartNodeWarningText);

        Assert.Equal("Canvas_NoStartNode_Game", Canvas(canChoose: false).StartNodeWarningText);
    }

    [Fact]
    public void ContextMenuCommand_MakesTheStartNode()
    {
        var vm = Canvas();
        Assert.True(vm.CanMakeStartNode);
        vm.MakeStartNodeCmdCommand.Execute(ById(vm, 2));
        Assert.Equal(0, vm.Nodes.Single(n => n.DefaultText == "second").NodeId);
        Assert.False(vm.CanMakeStartNode);
    }
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~MakeStartNodeTests"`
Expected: a build error. `ChooseStartNodeCommand` and the other new members don't exist yet.

- [ ] **Step 3: Implement**

In `ConversationViewModel`, extend the start-node region:

```csharp
    // Pick mode: the warning strip's "Choose start node…" arms it; selecting a node then
    // makes that node the start node (issue 172).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartNodeWarningText))]
    private bool _isChoosingStartNode;

    public bool ShowStartNodeWarning => !HasStartNode;

    /// For the right-click item: the fix is on offer.
    public bool CanMakeStartNode => IsEditable && CanChooseStartNode && !HasStartNode;

    public string StartNodeWarningText =>
        IsChoosingStartNode ? Loc.Get("Canvas_ChooseStartNode_Prompt")
        : CanChooseStartNode ? Loc.Get("Canvas_NoStartNode_New")
        : Loc.Get("Canvas_NoStartNode_Game");

    [RelayCommand(CanExecute = nameof(CanMakeStartNode))]
    private void ChooseStartNode() => IsChoosingStartNode = true;

    [RelayCommand]
    private void CancelChooseStartNode() => IsChoosingStartNode = false;

    [RelayCommand(CanExecute = nameof(CanMakeStartNode))]
    private void MakeStartNodeCmd(NodeViewModel? node)
    {
        if (node is not null) MakeStartNode(node);
    }

    partial void OnHasStartNodeChanged(bool value)
    {
        if (value) IsChoosingStartNode = false;
        StartNodeStateChanged();
    }

    partial void OnCanChooseStartNodeChanged(bool value) => StartNodeStateChanged();

    private void StartNodeStateChanged()
    {
        OnPropertyChanged(nameof(ShowStartNodeWarning));
        OnPropertyChanged(nameof(CanMakeStartNode));
        OnPropertyChanged(nameof(StartNodeWarningText));
        ChooseStartNodeCommand.NotifyCanExecuteChanged();
        MakeStartNodeCmdCommand.NotifyCanExecuteChanged();
    }
```

Then:
- Add `[NotifyPropertyChangedFor(nameof(CanMakeStartNode))]`, `[NotifyCanExecuteChangedFor(nameof(ChooseStartNodeCommand))]` and `[NotifyCanExecuteChangedFor(nameof(MakeStartNodeCmdCommand))]` to `_isEditable`.
- In `OnSelectedNodeChanged`, add this first:

  ```csharp
          if (IsChoosingStartNode && value is not null)
          {
              IsChoosingStartNode = false;
              MakeStartNode(value);
          }
  ```

- In `Clear()`, add `IsChoosingStartNode = false;`.

In `Strings.axaml`:

```xml
    <sys:String x:Key="Canvas_NoStartNode_New">This conversation has no node 0, so the game can't start it from its first line.</sys:String>
    <sys:String x:Key="Canvas_NoStartNode_Game">Node 0 was removed, so the game can't start this conversation. Undo the deletion, or remove it from the patch.</sys:String>
    <sys:String x:Key="Canvas_ChooseStartNode_Prompt">Click the node that should start the conversation. Esc cancels.</sys:String>
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the same command as Step 2.
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests
git commit -m "feat: choose the start node by clicking it, or from the node menu (#172)"
```

---

### Task 7: Counting references elsewhere in the project

**Files:**
- Create: `DialogEditor.Patch/StartNodeReferenceScanner.cs`
- Test: `DialogEditor.Tests/Patch/StartNodeReferenceScannerTests.cs`

**Interfaces:**
- Consumes: `NewConversationId.For(string)` (issue 162, in `DialogEditor.Core.GameData`) and `ConversationJumpVerbs.IsJump`.
- Produces: `StartNodeReferenceScanner.Count(DialogProject project, string conversation, int oldId, string? oldVoPath) : int`

**Matching rules** (the editor has no script catalogue here, so they are deliberately simple):
- **A script call counts** when `ConversationJumpVerbs.IsJump(script.DisplayName)` is true, and both of these hold:
  - one of its parameters names the conversation:
    - its file name without extension equals `conversation`, ignoring case. This matches both a bare name and PoE1's `…/name.conversation` path.
    - or it equals `NewConversationId.For(conversation)` as a GUID, which is how Deadfire names it;
  - another parameter equals `oldId` as an invariant-culture integer.
- **An ExternalVO counts** when it equals `oldVoPath` once both are normalised: `\` becomes `/`, comparison ignores case, and any `.wem` is trimmed.
- **Scanned:** every patch except `conversation`'s own. That covers:
  - `AddedNodes`: their `Scripts` and `ExternalVO`;
  - `ModifiedNodes`: their `UpdatedScripts`, and `FieldChanges["ExternalVO"].To`, which is JSON-encoded, so deserialise it with `System.Text.Json.JsonSerializer.Deserialize<string>`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

/// Issue 172: after a renumber, the status bar says how many references elsewhere in the
/// project still name the old node ID. They are counted, never rewritten.
public class StartNodeReferenceScannerTests
{
    private static ScriptCall Jump(params string[] p) =>
        new("Void StartConversation(Guid, Guid, Int32)", p, ScriptCategory.Enter);

    private static NodeEditSnapshot Added(int id, IReadOnlyList<ScriptCall>? scripts = null, string externalVo = "") =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", externalVo,
            false, false, [], [], scripts ?? []);

    private static DialogProject With(string conv, params NodeEditSnapshot[] nodes) =>
        DialogProject.Empty("p").WithPatch(
            new ConversationPatch(conv, ConversationPatch.CurrentSchemaVersion, nodes, [], []));

    [Fact]
    public void Counts_AJumpByName_WithTheOldId() =>
        Assert.Equal(1, StartNodeReferenceScanner.Count(
            With("other", Added(5, [Jump("oei_player", "fresh", "1")])), "fresh", 1, null));

    [Fact]
    public void Counts_AJumpByDeadfireId() =>
        Assert.Equal(1, StartNodeReferenceScanner.Count(
            With("other", Added(5, [Jump("oei_player", NewConversationId.For("fresh").ToString(), "1")])),
            "fresh", 1, null));

    [Fact]
    public void Counts_APoe1Path() =>
        Assert.Equal(1, StartNodeReferenceScanner.Count(
            With("other", Added(5, [Jump("oei_player", "Assets/Data/Conversations/x/Fresh.conversation", "1")])),
            "fresh", 1, null));

    [Fact]
    public void Ignores_OtherConversations_AndOtherIds()
    {
        var project = With("other",
            Added(5, [Jump("oei_player", "elsewhere", "1")]),
            Added(6, [Jump("oei_player", "fresh", "2")]));
        Assert.Equal(0, StartNodeReferenceScanner.Count(project, "fresh", 1, null));
    }

    [Fact]
    public void Ignores_TheConversationItself() =>
        Assert.Equal(0, StartNodeReferenceScanner.Count(
            With("fresh", Added(5, [Jump("oei_player", "fresh", "1")])), "fresh", 1, null));

    [Fact]
    public void Counts_AnExternalVoAlias() =>
        Assert.Equal(1, StartNodeReferenceScanner.Count(
            With("other", Added(5, externalVo: "Narrator\\fresh_0001")), "fresh", 1, "narrator/fresh_0001"));
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~StartNodeReferenceScannerTests"`
Expected: a build error. `StartNodeReferenceScanner` doesn't exist yet.

- [ ] **Step 3: Implement** `DialogEditor.Patch/StartNodeReferenceScanner.cs`

```csharp
using System.Globalization;
using System.Text.Json;
using DialogEditor.Core.Analytics;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

/// Counts references in the project's other conversations that still name a node's old ID
/// after it became the start node (issue 172). Counted for the status bar, never rewritten.
public static class StartNodeReferenceScanner
{
    public static int Count(DialogProject project, string conversation, int oldId, string? oldVoPath)
    {
        var guid = NewConversationId.For(conversation).ToString();
        var id   = oldId.ToString(CultureInfo.InvariantCulture);
        var vo   = Normalise(oldVoPath);
        var count = 0;

        foreach (var (name, patch) in project.Patches)
        {
            if (string.Equals(name, conversation, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var node in patch.AddedNodes)
            {
                count += node.Scripts.Count(s => Names(s, conversation, guid, id));
                if (vo is not null && Normalise(node.ExternalVO) == vo) count++;
            }
            foreach (var mod in patch.ModifiedNodes)
            {
                count += (mod.UpdatedScripts ?? []).Count(s => Names(s, conversation, guid, id));
                if (vo is not null && mod.FieldChanges.TryGetValue("ExternalVO", out var change)
                    && Normalise(JsonSerializer.Deserialize<string>(change.To)) == vo)
                    count++;
            }
        }
        return count;
    }

    private static bool Names(ScriptCall s, string conversation, string guid, string id) =>
        ConversationJumpVerbs.IsJump(s.DisplayName)
        && s.Parameters.Any(p =>
               string.Equals(Path.GetFileNameWithoutExtension(p.Replace('\\', '/')), conversation,
                             StringComparison.OrdinalIgnoreCase)
               || string.Equals(p, guid, StringComparison.OrdinalIgnoreCase))
        && s.Parameters.Contains(id);

    private static string? Normalise(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Replace('\\', '/').ToLowerInvariant();
        return p.EndsWith(".wem", StringComparison.Ordinal) ? p[..^4] : p;
    }
}
```

If `DialogProject.Patches` isn't an `IReadOnlyDictionary<string, ConversationPatch>`, adapt the loop to its real shape and record a ruling. If the `FieldChanges` key for ExternalVO isn't `"ExternalVO"`, take the name `DiffEngine` uses (`grep -n ExternalVO DialogEditor.Patch/DiffEngine.cs`).

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the same command as Step 2.
Expected: all 6 PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/StartNodeReferenceScanner.cs DialogEditor.Tests
git commit -m "feat: count references that still name a renumbered start node (#172)"
```

---

### Task 8: The main window wires it up (load flag, save remap, status)

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelStartNodeTests.cs`

**Interfaces:**
- Consumes:
  - `ConversationViewModel.CanChooseStartNode`, `MakeStartNode`, `StartNodeAttempted` and `TakePendingRemaps()` (Tasks 5–6);
  - `NodeIdRemap.Apply` (Task 4);
  - `StartNodeReferenceScanner.Count` (Task 7).
- Produces: the status keys `Status_StartNodeMade`, `Status_StartNodeMadeWithReferences`, `Status_StartNodeNotAllowed`, `Status_StartNodeExists`, `Status_StartNodeVoTargetExists` and `Status_StartNodeVoRenameFailed`.

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/ViewModels/MainWindowViewModelStartNodeTests.cs`. It follows the helpers in `MainWindowViewModelPersistenceTests`, copied here so the file stands alone:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 172 through the main window: which conversations offer the fix, and that a save
/// keeps the renumbered node's translations in every language.
public class MainWindowViewModelStartNodeTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly string _settingsPath;

    public MainWindowViewModelStartNodeTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_start_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static ConversationNode MakeNode(int id, params int[] to) =>
        new(id, false, SpeakerCategory.Npc, "spk", "lst", [.. to.Select(t => new NodeLink(id, t, []))],
            [], [], "Conversation", "None");

    private static void InjectProvider(MainWindowViewModel vm, IGameDataProvider provider) =>
        typeof(MainWindowViewModel)
            .GetField("_provider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, provider);

    private static Task LoadProjectAsync(MainWindowViewModel vm, string path) =>
        (Task)typeof(MainWindowViewModel)
            .GetMethod("LoadProjectAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [path, false])!;

    private static void Select(MainWindowViewModel vm, ConversationFile file) =>
        typeof(MainWindowViewModel)
            .GetMethod("OnConversationSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [file]);

    /// A pre-issue-162 new conversation "fresh": node 1 → node 2, with French for node 1.
    private async Task<(MainWindowViewModel Vm, string Path)> OpenFresh()
    {
        var provider = new FakeGameDataProvider("poe2", "en");
        var created  = ConversationSnapshotBuilder.Build(new Conversation("fresh",
            [MakeNode(1, 2), MakeNode(2)],
            new StringTable([new StringEntry(1, "first", ""), new StringEntry(2, "second", "")])));
        var patch = DiffEngine.Diff("fresh", new ConversationEditSnapshot([]), created, "en");
        patch = patch with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>(patch.Translations)
            {
                ["fr"] = [new NodeTranslation(1, "premier", "")],
            },
        };
        var path = Path.Combine(Path.GetTempPath(), $"start_{Guid.NewGuid():N}.dialogproject");
        _tempFiles.Add(path);
        DialogProjectSerializer.SaveToFile(path,
            DialogProject.Empty("Start").WithNewConversation("fresh").WithPatch(patch));

        var vm = MakeVm();
        InjectProvider(vm, provider);
        await LoadProjectAsync(vm, path);
        Select(vm, provider.BuildNewConversationFile("fresh"));
        return (vm, path);
    }

    private static NodeViewModel ById(MainWindowViewModel vm, int id) => vm.Canvas.Nodes.Single(n => n.NodeId == id);

    [Fact]
    public async Task NewConversation_OffersTheFix()
    {
        var (vm, _) = await OpenFresh();
        Assert.True(vm.Canvas.CanChooseStartNode);
        Assert.True(vm.Canvas.ShowStartNodeWarning);
    }

    [Fact]
    public async Task Make_SetsTheStatus()
    {
        var (vm, _) = await OpenFresh();
        vm.Canvas.MakeStartNode(ById(vm, 1));
        Assert.Equal("Status_StartNodeMade", vm.StatusText);
    }

    [Fact]
    public async Task Save_KeepsOtherLanguages_UnderTheNewId()
    {
        var (vm, path) = await OpenFresh();
        vm.Canvas.MakeStartNode(ById(vm, 1));
        vm.SaveProjectCommand.Execute(null);

        var saved = DialogProjectSerializer.LoadFromFile(path).Patches["fresh"];
        Assert.Equal("premier", saved.Translations["fr"].Single(t => t.NodeId == 0).DefaultText);
        Assert.Equal("first",   saved.Translations["en"].Single(t => t.NodeId == 0).DefaultText);

        // End to end: the saved patch builds a conversation that starts at node 0.
        var built = PatchApplier.Apply(new ConversationEditSnapshot([]), saved);
        var start = built.Nodes.Single(n => n.NodeId == 0);
        Assert.Equal(2, Assert.Single(start.Links).ToNodeId);
        Assert.DoesNotContain(built.Nodes, n => n.NodeId == 1);
    }

    [Fact]
    public async Task Save_Undo_Save_MovesTranslationsBack()
    {
        var (vm, path) = await OpenFresh();
        vm.Canvas.MakeStartNode(ById(vm, 1));
        vm.SaveProjectCommand.Execute(null);
        vm.Canvas.Undo();
        vm.SaveProjectCommand.Execute(null);

        var saved = DialogProjectSerializer.LoadFromFile(path).Patches["fresh"];
        Assert.Equal("premier", saved.Translations["fr"].Single(t => t.NodeId == 1).DefaultText);
        Assert.DoesNotContain(saved.Translations["fr"], t => t.NodeId == 0);
    }
}
```

Add one test to `MainWindowViewModelPersistenceTests`, with its helpers in scope, checking that a shipped conversation doesn't offer the fix:

```csharp
    [Fact]
    public async Task ShippedConversation_DoesNotOfferTheStartNodeFix()   // issue 172
    {
        var (vm, provider, _) = await OpenProjectWithGreetingPatch(GreetingTextPatch("edited line"));
        SelectConversation(vm, provider.BuildNewConversationFile("greeting"));
        Assert.False(vm.Canvas.CanChooseStartNode);
    }
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&(FullyQualifiedName~MainWindowViewModelStartNodeTests|FullyQualifiedName~ShippedConversation_DoesNotOfferTheStartNodeFix)"`
Expected: FAIL.
- `NewConversation_OffersTheFix`: `CanChooseStartNode` is false.
- `Make_SetsTheStatus`: the status is not set.
- `Save_KeepsOtherLanguages_UnderTheNewId`: the French entry is dropped.
- `Save_Undo_Save_…`: fails as well.
- The shipped-conversation test may already pass. That is expected, because false is the default; it is a guard.

- [ ] **Step 3: Implement**

In `MainWindowViewModel`:

1. **`LoadNewConversation`**, after the `Canvas.Load(...)` branches:

   ```csharp
           // Issue 172: only a conversation the project created can be renumbered; in a game
           // conversation the patch would see a delete plus an add.
           Canvas.CanChooseStartNode = true;
   ```

   **`LoadConversationFile`**: `Canvas.Load` → `Clear()` already resets the flag to false, so nothing is needed there.

2. **`FoldCanvasIntoProject`**: replace the `CarryingTranslationsFrom` call with:

   ```csharp
           // A start-node renumber (issue 172) moved nodes the stored translations still key
           // by their old IDs; re-key them before carrying them over, or the stale-data filter
           // below drops them.
           var prior   = _project!.Patches.GetValueOrDefault(_currentFile.Name);
           var remaps  = Canvas.TakePendingRemaps();
           if (prior is not null && remaps.Count > 0)
               prior = prior with { Translations = NodeIdRemap.Apply(prior.Translations, remaps) };
           patch = patch.CarryingTranslationsFrom(prior, _provider.Language);
   ```

3. **The status message.** In the constructor, next to `Canvas.ConnectModeChanged += …`:

   ```csharp
           Canvas.StartNodeAttempted += (_, e) => StatusText = e.Outcome switch
           {
               StartNodeOutcome.Made => StartNodeReferences(e) is var refs and > 0
                   ? Loc.Format("Status_StartNodeMadeWithReferences", e.OldNodeId, refs)
                   : Loc.Format("Status_StartNodeMade", e.OldNodeId),
               StartNodeOutcome.StartNodeExists => Loc.Get("Status_StartNodeExists"),
               StartNodeOutcome.VoTargetExists  => Loc.Format("Status_StartNodeVoTargetExists", e.OldNodeId, e.Detail),
               StartNodeOutcome.VoRenameFailed  => Loc.Format("Status_StartNodeVoRenameFailed", e.OldNodeId, e.Detail),
               _                                => Loc.Get("Status_StartNodeNotAllowed"),
           };
   ```

   And add this method:

   ```csharp
       private int StartNodeReferences(StartNodeChangedEventArgs e) =>
           _project is null ? 0
           : StartNodeReferenceScanner.Count(_project, Canvas.ConversationName, e.OldNodeId, e.OldVoPath);
   ```

In `Strings.axaml`, next to `Status_ConnectMode_Cancelled`:

```xml
    <sys:String x:Key="Status_StartNodeMade">Node {0} is now node 0, the start node.</sys:String>
    <sys:String x:Key="Status_StartNodeMadeWithReferences">Node {0} is now node 0, the start node. {1} reference(s) in other conversations still name node {0}.</sys:String>
    <sys:String x:Key="Status_StartNodeNotAllowed">Only a conversation created in this project can have a new start node.</sys:String>
    <sys:String x:Key="Status_StartNodeExists">This conversation already has a node 0.</sys:String>
    <sys:String x:Key="Status_StartNodeVoTargetExists">Node {0} wasn't changed: its voice-over can't be renamed, because {1} already exists.</sys:String>
    <sys:String x:Key="Status_StartNodeVoRenameFailed">Node {0} wasn't changed: its voice-over couldn't be renamed ({1}).</sys:String>
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the same command as Step 2.
Expected: all PASS.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: PASS, apart from known skips.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests
git commit -m "feat: new conversations offer the start-node fix, and saves keep its translations (#172)"
```

---

### Task 9: The canvas warning strip and the node menu item

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml`
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml.cs` (Esc)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

**Interfaces:**
- Consumes: `ShowStartNodeWarning`, `StartNodeWarningText`, `IsChoosingStartNode`, `CanMakeStartNode`, `ChooseStartNodeCommand`, `CancelChooseStartNodeCommand` and `MakeStartNodeCmdCommand` (Task 6).

- [ ] **Step 1: Add the strings**

```xml
    <sys:String x:Key="Canvas_ChooseStartNode">Choose start node…</sys:String>
    <sys:String x:Key="ToolTip_ChooseStartNode">Pick the node the game should start this conversation with. After clicking this, click a node on the canvas: it becomes node 0, its links, text, comment and staged voice-over follow it, and Undo reverses it.</sys:String>
    <sys:String x:Key="Canvas_CancelChooseStartNode">Cancel</sys:String>
    <sys:String x:Key="ToolTip_CancelChooseStartNode">Stop choosing a start node without changing anything (Esc does the same).</sys:String>
    <sys:String x:Key="ToolTip_StartNodeWarning">Both games start a conversation at node 0 unless a script names another node. Without a node 0 the conversation can't be started from its first line.</sys:String>
    <sys:String x:Key="AutomationName_StartNodeWarning">No start node warning</sys:String>
    <sys:String x:Key="Menu_MakeStartNode">Make this the start node</sys:String>
    <sys:String x:Key="ToolTip_MakeStartNode">Renumber this node to node 0, where the game starts the conversation. Links, text, the node comment and staged voice-over follow it. Undo reverses it.</sys:String>
```

- [ ] **Step 2: Add the strip**

In `ConversationView.axaml`:
- Change the root grid to `<Grid RowDefinitions="Auto,Auto,*">`.
- Move the canvas `Grid` from `Grid.Row="1"` to `Grid.Row="2"`.
- Insert, between the toolbar `Border` and the canvas:

```xml
        <!-- Issue 172: the game starts every conversation at node 0. Shown while this one has
             nodes but no node 0; for a conversation the project created it offers the fix. -->
        <Border Grid.Row="1" Padding="8,4" BorderThickness="0,0,0,1"
                Background="{DynamicResource Brush.Validation.Background}"
                BorderBrush="{DynamicResource Brush.Validation.Border}"
                IsVisible="{Binding ShowStartNodeWarning}"
                ToolTip.Tip="{DynamicResource ToolTip_StartNodeWarning}"
                AutomationProperties.Name="{DynamicResource AutomationName_StartNodeWarning}"
                AutomationProperties.HelpText="{DynamicResource ToolTip_StartNodeWarning}">
            <DockPanel>
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="6">
                    <Button Content="{DynamicResource Canvas_ChooseStartNode}"
                            Command="{Binding ChooseStartNodeCommand}"
                            IsVisible="{Binding CanMakeStartNode}"
                            ToolTip.Tip="{DynamicResource ToolTip_ChooseStartNode}"
                            AutomationProperties.Name="{DynamicResource Canvas_ChooseStartNode}"
                            AutomationProperties.HelpText="{DynamicResource ToolTip_ChooseStartNode}"/>
                    <Button Content="{DynamicResource Canvas_CancelChooseStartNode}"
                            Command="{Binding CancelChooseStartNodeCommand}"
                            IsVisible="{Binding IsChoosingStartNode}"
                            ToolTip.Tip="{DynamicResource ToolTip_CancelChooseStartNode}"
                            AutomationProperties.Name="{DynamicResource Canvas_CancelChooseStartNode}"
                            AutomationProperties.HelpText="{DynamicResource ToolTip_CancelChooseStartNode}"/>
                </StackPanel>
                <TextBlock Text="{Binding StartNodeWarningText}" VerticalAlignment="Center"
                           Foreground="{DynamicResource Brush.Validation.Text}" TextWrapping="Wrap"/>
            </DockPanel>
        </Border>
```

Hiding "Choose start node…" while picking is optional, because it is harmless. Leave it bound to `CanMakeStartNode` only.

- [ ] **Step 3: Add the context-menu item**

In the node `ContextMenu`, after the `NodeMenu_ConnectToNode` item:

```xml
                                    <ctrl:AccessibleMenuItem Header="{DynamicResource Menu_MakeStartNode}"
                                              AutomationProperties.AutomationId="NodeMenu_MakeStartNode"
                                              Command="{Binding $parent[UserControl].DataContext.MakeStartNodeCmdCommand}"
                                              CommandParameter="{Binding}"
                                              IsVisible="{Binding $parent[UserControl].DataContext.CanMakeStartNode}"
                                              ToolTip.Tip="{DynamicResource ToolTip_MakeStartNode}"
                                              AutomationProperties.HelpText="{DynamicResource ToolTip_MakeStartNode}"/>
```

- [ ] **Step 4: Make Esc cancel picking**

In `ConversationView.axaml.cs`, in the key `switch`, add this before `Key.Escape when none && vm.IsConnecting`:

```csharp
            Key.Escape when none && vm.IsChoosingStartNode => CancelChoosingStartNode(vm),
```

And add the helper:

```csharp
    private static bool CancelChoosingStartNode(ConversationViewModel vm)
    {
        vm.CancelChooseStartNodeCommand.Execute(null);
        return true;
    }
```

- [ ] **Step 5: Build and run the house tests**

Run: `dotnet build DialogEditor.Avalonia` and then `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: the build succeeds, and the suite PASSES. That includes the localisation, tooltip and no-hard-coded-string tests.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia
git commit -m "feat: canvas warning strip and node menu item for a missing start node (#172)"
```

---

### Task 10: GUI verification

- [ ] **Step 1: Run the app against a pre-issue-162 shaped conversation**

Use the `running-the-app` skill and `tools/ui-automation/DriveApp.ps1`. Open or create a project with a new conversation, then use the canvas to create a node, add a second node linked from it, and delete the first. The conversation now has no node 0. Then check:
- the warning strip is visible, and the UIA name "No start node warning" is found;
- the "Choose start node…" button is found by its UIA name, and invoking it changes the strip text to the pick prompt;
- right-clicking a node shows "Make this the start node";
- invoking that item hides the strip.

Record what was verified, and anything that couldn't be, in the final report. If UIA can't find a control by name, that is a defect to fix in the app (CLAUDE.md, UI Automation Support).

- [ ] **Step 2: Commit any fixes it needed**, each with a test where one is possible.
