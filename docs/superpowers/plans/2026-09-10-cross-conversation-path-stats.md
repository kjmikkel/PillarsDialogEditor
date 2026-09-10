# Cross-Conversation Path Stats Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Flow Analytics' Playthrough stats follow `StartConversation` handoffs into conversations the project patches, so playthrough figures describe the read the player experiences rather than one file's worth of it.

**Architecture:** A ViewModels-layer `ConversationJumpResolver` owns all I/O, patch application and GUID resolution, and emits a pure `MultiConversationGraph`. `PathStatsService` (in `DialogEditor.Core`, which has no project references and must stay IO-free) is generalised from `int` node keys to `NodeRef(Conversation, NodeId)` and consumes that graph. The existing single-conversation overload becomes a thin wrapper around a one-conversation, no-jump graph, so both modes run one code path.

**Tech Stack:** C# / .NET, Avalonia UI, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-10-cross-conversation-path-stats-design.md`

## Global Constraints

- **Strict red/green TDD.** Never write implementation code for a behaviour before a failing test exists for it (CLAUDE.md).
- **No hard-coded user-visible text** in C# or XAML. All strings are resource keys in `DialogEditor.Avalonia/Resources/Strings.axaml`, read via `Loc.Get` / `Loc.Format` (CLAUDE.md).
- **Every interactive control carries a `ToolTip.Tip`** explaining its purpose in plain language, mirrored to `AutomationProperties.HelpText` (CLAUDE.md).
- **No bare `catch { }` in production code.** Every caught exception is logged via `AppLog.Warn(...)` or `AppLog.Error(...)`; only `OperationCanceledException` may be swallowed silently (CLAUDE.md).
- **`CHANGELOG.md` is frozen.** Do not add or edit entries.
- **`DialogEditor.Tests` runs serially** because of `AppSettings`/`Loc` global-state races. Do not re-enable parallelism.
- **`DialogEditor.Core` has no project references.** Nothing in `DialogEditor.Core` may reference `DialogEditor.Patch` or `DialogEditor.ViewModels`.
- **Reference the issue as `#14`** in commit messages.
- **Test command:** `dotnet test DialogEditor.Tests --filter "<name>"`.
- **No colour-only encoding** — every tag, reason and severity must also be textual.

---

## File Structure

**Create:**
- `DialogEditor.Core/Analytics/ConversationJumpModels.cs` — `NodeRef`, `JumpEdge`, `UnfollowedReason`, `UnfollowedJump`, `MultiConversationGraph`, `ConversationJumpVerbs`. Pure transport types plus the shared verb list.
- `DialogEditor.ViewModels/Services/ConversationJumpResolver.cs` — all I/O, patch application, GUID resolution; emits `MultiConversationGraph`.
- `DialogEditor.Tests/Services/ConversationJumpResolverTests.cs`

**Modify:**
- `DialogEditor.Core/Analytics/PathStatsModels.cs` — `BranchStat`, `EndingStat`, `PathStatsReport` gain `NodeRef` / new fields.
- `DialogEditor.Core/Analytics/PathStatsService.cs` — `int` → `NodeRef` sweep; two edge sets; additive arithmetic.
- `DialogEditor.Core/Analytics/FlowAnalysisModels.cs` — new `FlowIssueKind`.
- `DialogEditor.Core/Analytics/FlowAnalysisService.cs` — raise the new issue.
- `DialogEditor.ViewModels/Services/AppSettings.cs` — `FollowConversationJumps`.
- `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs` — toggle, routing, cross-conversation navigation, row display.
- `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` — cache the conversation GUID map on folder open; wire the resolver.
- `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml` — toggle, spanned line, conversation tags, unfollowed section.
- `DialogEditor.Avalonia/Resources/Strings.axaml` — new `Loc` keys.
- Tests: `PathStatsServiceTests`, `FlowAnalysisServiceTests`, `FlowAnalyticsViewModelTests`, `FlowAnalyticsWindowTests`, `AppSettingsTests`.

---

### Task 1: Core types and the `NodeRef` sweep (no behaviour change)

The whole mechanical refactor, landing green with **zero** new behaviour. Jump edges exist in the type system but no code produces or consumes them yet. This task is complete when every pre-existing `PathStatsServiceTests` assertion still passes with only node-id accessor changes.

**Files:**
- Create: `DialogEditor.Core/Analytics/ConversationJumpModels.cs`
- Modify: `DialogEditor.Core/Analytics/PathStatsModels.cs`
- Modify: `DialogEditor.Core/Analytics/PathStatsService.cs`
- Test: `DialogEditor.Tests/Analytics/PathStatsServiceTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: `NodeRef(string Conversation, int NodeId)`; `JumpEdge(NodeRef From, NodeRef To)`; `UnfollowedReason { NotPatched, Unresolved, LoadFailed }`; `UnfollowedJump(NodeRef From, string TargetLabel, UnfollowedReason Reason)`; `MultiConversationGraph(string RootConversation, IReadOnlyDictionary<string, ConversationEditSnapshot> Conversations, IReadOnlyList<JumpEdge> Jumps, IReadOnlyList<UnfollowedJump> Unfollowed)`; `ConversationJumpVerbs.All`; `PathStatsService.Analyze(MultiConversationGraph)`; `BranchStat.Choice` (`NodeRef`); `EndingStat.Node` (`NodeRef`); `PathStatsReport.Unfollowed`, `PathStatsReport.ConversationsSpanned`.

- [ ] **Step 1: Write the failing equivalence test**

This is the load-bearing test of the whole approach — it pins that the two overloads are one implementation.

Add to `DialogEditor.Tests/Analytics/PathStatsServiceTests.cs`:

```csharp
[Fact]
public void SnapshotOverload_EqualsOneConversationGraph()
{
    var snap = Snap(
        Node(0, "start", links: [Link(0, 1), Link(0, 2)]),
        Node(1, "long", isPlayerChoice: true, links: [Link(1, 3)]),
        Node(3, "aaa bbb ccc ddd"),
        Node(2, "short", isPlayerChoice: true));

    var graph = new MultiConversationGraph(
        RootConversation: "",
        Conversations: new Dictionary<string, ConversationEditSnapshot> { [""] = snap },
        Jumps: [],
        Unfollowed: []);

    Assert.Equal(PathStatsService.Analyze(snap), PathStatsService.Analyze(graph));
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "SnapshotOverload_EqualsOneConversationGraph"`
Expected: FAIL — compile error, `MultiConversationGraph` does not exist.

- [ ] **Step 3: Create the pure types**

Create `DialogEditor.Core/Analytics/ConversationJumpModels.cs`:

```csharp
using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Analytics;

/// Identifies a node across conversations. The single-conversation overload of
/// PathStatsService.Analyze uses the empty string: a ConversationEditSnapshot carries
/// no name, and inventing one would be a lie.
public readonly record struct NodeRef(string Conversation, int NodeId);

/// A resolved StartConversation handoff: the node running the script, and the node it
/// starts. Kept as a flat edge list rather than an adjacency map so the resolver never
/// has to think about lookup shape; PathStatsService groups by From once.
public record JumpEdge(NodeRef From, NodeRef To);

public enum UnfollowedReason { NotPatched, Unresolved, LoadFailed }

/// A handoff that was found but not walked. Reported so a project-patched-only boundary
/// never silently drops content from a report. TargetLabel is display text, never an id.
public record UnfollowedJump(NodeRef From, string TargetLabel, UnfollowedReason Reason);

public record MultiConversationGraph(
    string RootConversation,
    IReadOnlyDictionary<string, ConversationEditSnapshot> Conversations,
    IReadOnlyList<JumpEdge>       Jumps,
    IReadOnlyList<UnfollowedJump> Unfollowed);

/// Script verbs that actually start another conversation.
///
/// Deliberately a whitelist rather than "any script with a Conversation lookup kind":
/// MarkConversationNodeAsRead(Guid, Int32) and ClearConversationNodeAsRead(Guid, Int32)
/// have an identical parameter shape — a Conversation lookup kind plus a "Conversation
/// Node ID" — and start nothing. Matching on parameter shape alone would report phantom
/// handoffs, silently and with no exception. Pinned by a test.
///
/// Lives in Core so FlowAnalysisService (detection only, no catalogue access) and
/// ConversationJumpResolver (detection plus resolution) cannot drift apart.
public static class ConversationJumpVerbs
{
    public static readonly IReadOnlyList<string> All =
        ["StartConversation", "StartConversationFacingListener"];

    public static bool IsJump(string displayName) =>
        All.Contains(displayName, StringComparer.Ordinal);
}
```

- [ ] **Step 4: Update the report records**

In `DialogEditor.Core/Analytics/PathStatsModels.cs`, change the node keys and add the two new report fields. Keep every existing doc comment.

```csharp
public record BranchStat(
    NodeRef Choice,
    string  ChoiceText,
    int     DefaultContentWords,
    int     DefaultLongestWords,
    int     FemaleContentWords,
    int     FemaleLongestWords,
    IReadOnlyList<BranchStat> SubBranches);

public record EndingStat(
    NodeRef Node,
    string  Text,
    int     DefaultLongestWords,
    int     DefaultShortestWords,
    int     FemaleLongestWords,
    int     FemaleShortestWords);

public record PathStatsReport(
    bool HasSignificantFemaleVariant,
    int  DefaultTotalWords,
    int  FemaleTotalWords,
    int  DefaultLongestWords,
    int  DefaultShortestWords,
    int  FemaleLongestWords,
    int  FemaleShortestWords,
    IReadOnlyList<SpeakerWordCount> WordsPerSpeaker,
    IReadOnlyList<BranchStat>       Branches,
    IReadOnlyList<EndingStat>       Endings,
    IReadOnlyList<UnfollowedJump>   Unfollowed,
    int                             ConversationsSpanned);
```

- [ ] **Step 5: Sweep `PathStatsService` from `int` to `NodeRef`**

In `DialogEditor.Core/Analytics/PathStatsService.cs`, make `Analyze(MultiConversationGraph)` the real implementation and `Analyze(ConversationEditSnapshot)` a wrapper. **No arithmetic changes in this task** — jumps are not yet read.

The transformation is mechanical. Every local keyed by `int` becomes keyed by `NodeRef`:

```csharp
public static PathStatsReport Analyze(ConversationEditSnapshot snapshot) =>
    Analyze(new MultiConversationGraph(
        RootConversation: "",
        Conversations: new Dictionary<string, ConversationEditSnapshot> { [""] = snapshot },
        Jumps: [],
        Unfollowed: []));

public static PathStatsReport Analyze(MultiConversationGraph graph)
{
    // Flatten every conversation's nodes into one NodeRef-keyed map.
    var nodeById = new Dictionary<NodeRef, NodeEditSnapshot>();
    foreach (var (conv, snap) in graph.Conversations)
        foreach (var n in snap.Nodes)
            nodeById[new NodeRef(conv, n.NodeId)] = n;

    if (nodeById.Count == 0)
        return new PathStatsReport(false, 0, 0, 0, 0, 0, 0, [], [], [],
                                   graph.Unfollowed, graph.Conversations.Count);

    var root = new NodeRef(graph.RootConversation, 0);
    var allNodes = nodeById.Values.ToList();

    // ... Words/Def/Fem unchanged; Weight now takes a NodeRef:
    int Weight(NodeRef id, bool female) => female ? Fem(nodeById[id]) : Def(nodeById[id]);

    // Out-edges. Two SEPARATE sets — merging them would destroy the distinction the
    // additive arithmetic depends on (Task 2). In this task jumps are always empty.
    var jumpsByFrom = graph.Jumps
        .GroupBy(j => j.From)
        .ToDictionary(g => g.Key, g => g.Select(j => j.To).ToList());

    List<NodeRef> LinksOf(NodeRef u) => nodeById[u].Links
        .Select(l => new NodeRef(u.Conversation, l.ToNodeId))
        .Where(nodeById.ContainsKey)
        .ToList();
    List<NodeRef> JumpsOf(NodeRef u) =>
        jumpsByFrom.TryGetValue(u, out var js)
            ? js.Where(nodeById.ContainsKey).ToList() : [];
    // ...
}
```

Apply the same `int` → `NodeRef` change to: `dag` (now `dagLinks` and `dagJumps`, both `Dictionary<NodeRef, List<NodeRef>>`), `onStack`/`visited` (`HashSet<NodeRef>`), `Dfs`, `longMemo`/`shortMemo`/`longToMemo`/`shortToMemo` (keys become `(NodeRef, bool)`), `ReachableSum`, `ChoiceFrontier`, `forkStack`, `Forks`, and `preds`. Replace the literal `0` root with `root`, and `nodeById.ContainsKey(0)` with `nodeById.ContainsKey(root)`.

Fork ordering changes from `OrderBy(c => c)` to a deterministic composite that keeps the open conversation on top:

```csharp
.OrderBy(c => c.Conversation == graph.RootConversation ? 0 : 1)
.ThenBy(c => c.Conversation, StringComparer.Ordinal)
.ThenBy(c => c.NodeId)
```

Totals and `wordsPerSpeaker` are computed over `allNodes` instead of `snapshot.Nodes`. Construct the report with `graph.Unfollowed` and `graph.Conversations.Count`.

- [ ] **Step 6: Update existing test assertions mechanically**

In `DialogEditor.Tests/Analytics/PathStatsServiceTests.cs`, add a `scripts` parameter to the local helper (it currently hard-codes `[]` as the last argument):

```csharp
private static NodeEditSnapshot Node(
    int id, string defaultText = "", string femaleText = "",
    bool isPlayerChoice = false, string speaker = "",
    SpeakerCategory category = SpeakerCategory.Npc,
    IReadOnlyList<LinkEditSnapshot>? links = null,
    IReadOnlyList<ScriptCall>? scripts = null) =>
    new(id, isPlayerChoice, category, speaker, "", defaultText, femaleText,
        "Conversation", "None", "", "", "", false, false,
        links ?? [], [], scripts ?? []);
```

Then change node-id accessors only: `b.ChoiceNodeId` → `b.Choice.NodeId`, and `e.NodeId` → `e.Node.NodeId`. **Change nothing else.** That the remaining assertions pass untouched is the evidence the single-conversation path did not move.

- [ ] **Step 7: Run the whole path-stats suite**

Run: `dotnet test DialogEditor.Tests --filter "PathStatsServiceTests"`
Expected: PASS, including `SnapshotOverload_EqualsOneConversationGraph`.

- [ ] **Step 8: Build the solution to catch downstream breakage**

Run: `dotnet build DialogEditor.slnx`
Expected: errors in `FlowAnalyticsViewModel.cs` where `ChoiceNodeId` / `NodeId` are read. Fix them to `.Choice.NodeId` / `.Node.NodeId`, and pass `[]` and `1` for the two new `PathStatsReport` fields anywhere a report is constructed in test doubles. Re-run until the build is clean and `dotnet test DialogEditor.Tests` is green.

- [ ] **Step 9: Commit**

```bash
git add DialogEditor.Core/Analytics DialogEditor.Tests/Analytics/PathStatsServiceTests.cs DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs
git commit -m "refactor(path-stats): key the graph by NodeRef, add jump types (#14)"
```

---

### Task 2: Additive jump arithmetic and the refined ending rule

**Files:**
- Modify: `DialogEditor.Core/Analytics/PathStatsService.cs`
- Test: `DialogEditor.Tests/Analytics/PathStatsServiceTests.cs`

**Interfaces:**
- Consumes: everything Task 1 produced.
- Produces: `Analyze` now walks `JumpEdge`s — additive in `Longest` and `Shortest`; endings exclude handoff nodes; `ChoiceFrontier`/`ReachableSum` cross jumps.

- [ ] **Step 1: Write the failing tests**

Add a graph helper and the behaviour tests:

```csharp
private static MultiConversationGraph Graph(
    string root,
    IReadOnlyDictionary<string, ConversationEditSnapshot> convs,
    params JumpEdge[] jumps) =>
    new(root, convs, jumps, []);

[Fact]
public void Handoff_TerminalNodeJumps_LongestSpansBothConversations()
{
    var a = Snap(Node(0, "one two", links: [Link(0, 1)]), Node(1, "three"));
    var b = Snap(Node(0, "four five six"));

    var report = PathStatsService.Analyze(Graph("A",
        new Dictionary<string, ConversationEditSnapshot> { ["A"] = a, ["B"] = b },
        new JumpEdge(new NodeRef("A", 1), new NodeRef("B", 0))));

    Assert.Equal(6, report.DefaultLongestWords);   // 2 + 1 + 3
    Assert.Equal(2, report.ConversationsSpanned);
}

// Pins the ADDITIVE rule against the rejected "treat a jump as one more link" design.
// If someone folds jumps into the ordinary edge list, Longest becomes max(1, 3) + 2 = 5
// and this test fails. Do not "simplify" it away.
[Fact]
public void NodeThatContinuesAndHandsOff_CountsBoth_NotMax()
{
    var a = Snap(Node(0, "one two", links: [Link(0, 1)]), Node(1, "three"));
    var b = Snap(Node(0, "four five six"));

    var report = PathStatsService.Analyze(Graph("A",
        new Dictionary<string, ConversationEditSnapshot> { ["A"] = a, ["B"] = b },
        new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

    Assert.Equal(6, report.DefaultLongestWords);   // 2 + max(1) + 3, not 2 + max(1, 3)
}

[Fact]
public void Shortest_SumsJumps_BecauseASpawnIsNotOptional()
{
    var a = Snap(Node(0, "one", links: [Link(0, 1), Link(0, 2)]),
                 Node(1, "two three four"), Node(2, "five"));
    var b = Snap(Node(0, "six seven"));

    var report = PathStatsService.Analyze(Graph("A",
        new Dictionary<string, ConversationEditSnapshot> { ["A"] = a, ["B"] = b },
        new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

    Assert.Equal(4, report.DefaultShortestWords);  // 1 + min(3, 1) + 2
}

[Fact]
public void JumpCycle_Terminates_AndCountsOnce()
{
    var a = Snap(Node(0, "a"));
    var b = Snap(Node(0, "b"));

    var report = PathStatsService.Analyze(Graph("A",
        new Dictionary<string, ConversationEditSnapshot> { ["A"] = a, ["B"] = b },
        new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0)),
        new JumpEdge(new NodeRef("B", 0), new NodeRef("A", 0))));

    Assert.Equal(2, report.DefaultLongestWords);
}

[Fact]
public void NodeWithAHandoff_IsNotAnEnding()
{
    var a = Snap(Node(0, "a"));
    var b = Snap(Node(0, "b"));

    var report = PathStatsService.Analyze(Graph("A",
        new Dictionary<string, ConversationEditSnapshot> { ["A"] = a, ["B"] = b },
        new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

    Assert.DoesNotContain(report.Endings, e => e.Node == new NodeRef("A", 0));
    Assert.Contains(report.Endings,     e => e.Node == new NodeRef("B", 0));
}

[Fact]
public void ForkInsideAJumpedToConversation_AppearsInTheTree()
{
    var a = Snap(Node(0, "greeting"));
    var b = Snap(Node(0, "hub", links: [Link(0, 1)]),
                 Node(1, "pick me", isPlayerChoice: true));

    var report = PathStatsService.Analyze(Graph("A",
        new Dictionary<string, ConversationEditSnapshot> { ["A"] = a, ["B"] = b },
        new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

    Assert.Contains(report.Branches, x => x.Choice == new NodeRef("B", 1));
}

[Fact]
public void WordsPerSpeaker_SumsOneSpeakerAcrossConversations()
{
    var a = Snap(Node(0, "one two", speaker: "serafen"));
    var b = Snap(Node(0, "three", speaker: "serafen"));

    var report = PathStatsService.Analyze(Graph("A",
        new Dictionary<string, ConversationEditSnapshot> { ["A"] = a, ["B"] = b },
        new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

    Assert.Equal(3, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "serafen").DefaultWords);
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "PathStatsServiceTests"`
Expected: the seven new tests FAIL (jumps are not walked yet); all pre-existing tests still PASS.

- [ ] **Step 3: Walk jumps in the DFS and the reachable/frontier walks**

In `Dfs`, traverse both edge sets, recording them separately so the arithmetic can tell them apart:

```csharp
void Dfs(NodeRef u)
{
    visited.Add(u);
    onStack.Add(u);
    dagLinks[u] = [];
    dagJumps[u] = [];
    foreach (var v in LinksOf(u))
    {
        if (onStack.Contains(v)) continue;   // back-edge → drop
        dagLinks[u].Add(v);
        if (!visited.Contains(v)) Dfs(v);
    }
    foreach (var v in JumpsOf(u))
    {
        if (onStack.Contains(v)) continue;   // a handoff that loops back — counted once
        dagJumps[u].Add(v);
        if (!visited.Contains(v)) Dfs(v);
    }
    onStack.Remove(u);
}
```

In `ReachableSum` and `ChoiceFrontier`, enqueue `LinksOf(u).Concat(JumpsOf(u))` — both are set-based walks, so the additive/alternative distinction does not apply there.

- [ ] **Step 4: Make the arithmetic additive**

```csharp
int Longest(NodeRef u, bool female)
{
    if (longMemo.TryGetValue((u, female), out var cached)) return cached;
    var best = Weight(u, female);
    // Links are ALTERNATIVES: the player takes one, so take the max.
    if (dagLinks.TryGetValue(u, out var outs) && outs.Count > 0)
        best += outs.Max(v => Longest(v, female));
    // Jumps are SPAWNS: ConversationManager.StartConversation adds a new FlowChartPlayer
    // and never stops the current one, so a handoff's words are read IN ADDITION to
    // whatever this node's own links contribute. Summed, not maxed.
    if (dagJumps.TryGetValue(u, out var js))
        best += js.Sum(v => Longest(v, female));
    longMemo[(u, female)] = best;
    return best;
}
```

`Shortest` is identical except `outs.Min(...)` for links — jumps are still `Sum`, because a spawn is not optional and there is no shorter read that skips it.

- [ ] **Step 5: Refine the ending rule**

```csharp
var endings = dagLinks.Keys
    .Where(id => nodeById[id].Links.Count == 0 && JumpsOf(id).Count == 0)
    .Select(...)
```

A node that hands off is a way the conversation *continues*, not a way it finishes.

- [ ] **Step 6: Build `preds` from both edge sets**

```csharp
foreach (var (u, outs) in dagLinks)
    foreach (var v in outs) { ... }
foreach (var (u, outs) in dagJumps)
    foreach (var v in outs) { ... }
```

- [ ] **Step 7: Extend the `EndingStat` doc comment**

Add to the existing comment in `PathStatsModels.cs`, which already explains one reason these figures need not reconcile with the header:

```
/// Second reason these need not reconcile: LongestTo/ShortestTo walk predecessors taking
/// max/min, which under additive spawn semantics under-counts the concurrent case —
/// arriving at an ending in one conversation also entails reading whatever the spawning
/// conversation continued on to, and a max-over-predecessors walk misses those sibling
/// words. Accepted deliberately: making it exact needs per-path spawn sets, for a case
/// FlowIssueKind.ConversationJumpWhileContinuing already reports as a defect.
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test DialogEditor.Tests --filter "PathStatsServiceTests"`
Expected: PASS, all of them.

- [ ] **Step 9: Commit**

```bash
git add DialogEditor.Core/Analytics DialogEditor.Tests/Analytics/PathStatsServiceTests.cs
git commit -m "feat(path-stats): walk conversation handoffs additively (#14)"
```

---

### Task 3: The concurrency diagnostic

Independent of the toggle and of the resolver — it ships value on its own, because a node that both continues and hands off is an authoring defect whether or not anyone follows jumps.

**Files:**
- Modify: `DialogEditor.Core/Analytics/FlowAnalysisModels.cs`
- Modify: `DialogEditor.Core/Analytics/FlowAnalysisService.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs` (`KindLabel`)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs`

**Interfaces:**
- Consumes: `ConversationJumpVerbs.IsJump` from Task 1.
- Produces: `FlowIssueKind.ConversationJumpWhileContinuing`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void JumpWhileContinuing_IsReported()
{
    var start = new ScriptCall("Void StartConversation(Guid, Guid, Int32)",
                               ["spk", "conv", "0"], ScriptCategory.Exit);
    var report = FlowAnalysisService.Analyze(Snap(
        Node(0, "hands off and continues", links: [Link(0, 1)], scripts: [start]),
        Node(1, "more")));

    Assert.Contains(report.Issues,
        i => i.NodeId == 0 && i.Kind == FlowIssueKind.ConversationJumpWhileContinuing);
}

[Fact]
public void CleanHandoff_TerminalNode_IsNotReported()
{
    var start = new ScriptCall("Void StartConversation(Guid, Guid, Int32)",
                               ["spk", "conv", "0"], ScriptCategory.Exit);
    var report = FlowAnalysisService.Analyze(Snap(Node(0, "hands off", scripts: [start])));

    Assert.DoesNotContain(report.Issues,
        i => i.Kind == FlowIssueKind.ConversationJumpWhileContinuing);
}

// MarkConversationNodeAsRead has the SAME parameter shape as StartConversation — a
// Conversation lookup kind plus a "Conversation Node ID" — but starts nothing. Guards
// against anyone replacing the verb whitelist with parameter-shape matching.
[Fact]
public void MarkConversationNodeAsRead_IsNotAJump()
{
    var mark = new ScriptCall("Void MarkConversationNodeAsRead(Guid, Int32)",
                              ["conv", "3"], ScriptCategory.Enter);
    var report = FlowAnalysisService.Analyze(Snap(
        Node(0, "reads a flag", links: [Link(0, 1)], scripts: [mark]),
        Node(1, "more")));

    Assert.DoesNotContain(report.Issues,
        i => i.Kind == FlowIssueKind.ConversationJumpWhileContinuing);
}
```

Add the same `scripts` parameter to this file's local `Node(...)` helper as in Task 1 Step 6.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FlowAnalysisServiceTests"`
Expected: the first FAILS (kind does not exist — compile error); fix by adding the enum member, then it fails on the assertion.

- [ ] **Step 3: Add the enum member**

In `DialogEditor.Core/Analytics/FlowAnalysisModels.cs`, append to `FlowIssueKind` (append, so existing serialized values keep their ordinals):

```csharp
public enum FlowIssueKind
{
    Unreachable,
    PlayerDeadEnd,
    EmptyText,
    NoIncomingLinks,
    BarkTextTooLong,
    BarkHasPlayerChoiceChild,
    ConversationJumpWhileContinuing
}
```

- [ ] **Step 4: Raise the issue**

In `FlowAnalysisService.Analyze`, in the per-node issue loop:

```csharp
// A handoff is a SPAWN, not a replace — ConversationManager.StartConversation adds a
// new FlowChartPlayer without stopping the current one. A node that both links onward
// and hands off therefore really does run both, which is almost always a mistake.
if (node.Links.Count > 0 && node.Scripts.Any(s => ConversationJumpVerbs.IsJump(s.DisplayName)))
    issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.ConversationJumpWhileContinuing));
```

- [ ] **Step 5: Add the label and its resource key**

In `FlowAnalyticsViewModel.KindLabel`, add before the `_` arm:

```csharp
FlowIssueKind.ConversationJumpWhileContinuing
    => Loc.Get("FlowAnalytics_Issue_ConversationJumpWhileContinuing"),
```

In `DialogEditor.Avalonia/Resources/Strings.axaml`, beside the other `FlowAnalytics_Issue_*` keys:

```xml
<sys:String x:Key="FlowAnalytics_Issue_ConversationJumpWhileContinuing">Starts another conversation while continuing</sys:String>
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test DialogEditor.Tests --filter "FlowAnalysisServiceTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Core/Analytics DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs
git commit -m "feat(flow-analytics): report a node that hands off while continuing (#14)"
```

---

### Task 4: `ConversationJumpResolver`

**Files:**
- Create: `DialogEditor.ViewModels/Services/ConversationJumpResolver.cs`
- Test: `DialogEditor.Tests/Services/ConversationJumpResolverTests.cs`

**Interfaces:**
- Consumes: `MultiConversationGraph`, `JumpEdge`, `UnfollowedJump`, `UnfollowedReason`, `NodeRef`, `ConversationJumpVerbs` (Task 1).
- Produces:

```csharp
public static MultiConversationGraph Resolve(
    DialogProject project,
    IGameDataProvider provider,
    string primaryLanguage,
    string openConversationName,
    ConversationEditSnapshot openSnapshot,
    IReadOnlyDictionary<string, string> conversationNamesById,
    ScriptCatalogue? catalogue = null);
```

`conversationNamesById` maps conversation GUID → conversation name. `catalogue` defaults to `ScriptCatalogue.Instance`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ConversationJumpResolverTests
{
    private static NodeEditSnapshot Node(
        int id, string text = "", IReadOnlyList<LinkEditSnapshot>? links = null,
        IReadOnlyList<ScriptCall>? scripts = null) =>
        new(id, false, SpeakerCategory.Npc, "", "", text, "",
            "Conversation", "None", "", "", "", false, false,
            links ?? [], [], scripts ?? []);

    private static ScriptCall Poe2Start(string convGuid, int nodeId) =>
        new("Void StartConversation(Guid, Guid, Int32)",
            ["speaker", convGuid, nodeId.ToString()], ScriptCategory.Exit);

    private static ScriptCall Poe1Start(string convName, int nodeId) =>
        new("Void StartConversation(Guid, String, Int32)",
            ["speaker", convName, nodeId.ToString()], ScriptCategory.Exit);

    private static ConversationPatch Patch(string name) =>
        new(name, ConversationPatch.CurrentSchemaVersion, [], [], []);

    private static DialogProject ProjectWith(params string[] patchedConversations) =>
        new("test",
            DialogProject.CurrentSchemaVersion,
            patchedConversations.ToDictionary(n => n, Patch));

    /// Purpose-built multi-conversation fake. The existing StubProvider in
    /// DialogEditor.Tests/Helpers/ is single-conversation with a hard-coded
    /// `GameId => "stub"`, and other tests depend on its current shape — bending it
    /// would risk them, so these tests carry their own.
    private sealed class FakeJumpProvider : IGameDataProvider
    {
        private readonly Dictionary<string, ConversationEditSnapshot> _convs = new();
        private readonly HashSet<string> _throwOn = [];

        public string GameName => "Fake";
        public string GameId   { get; init; } = "poe2";
        public IReadOnlyList<string> AvailableLanguages => ["en"];
        public string Language { get; set; } = "en";

        public void AddConversation(string name, ConversationEditSnapshot snap)
            => _convs[name] = snap;
        public void ThrowOnLoad(string name) { _convs[name] = new([]); _throwOn.Add(name); }

        public IReadOnlyList<ConversationFile> EnumerateConversations()
            => _convs.Keys.Select(n => new ConversationFile(n, "", n + ".bundle", "")).ToList();

        public Conversation LoadConversation(ConversationFile f)
        {
            if (_throwOn.Contains(f.Name))
                throw new IOException($"simulated load failure for {f.Name}");
            var snap = _convs[f.Name];
            var nodes = snap.Nodes.Select(n => new ConversationNode(
                n.NodeId, n.IsPlayerChoice, n.SpeakerCategory, n.SpeakerGuid, n.ListenerGuid,
                n.Links.Select(l => new NodeLink(l.FromNodeId, l.ToNodeId, l.Conditions ?? [],
                                                 l.RandomWeight, l.QuestionNodeTextDisplay)).ToList(),
                n.Conditions, n.Scripts, n.DisplayType, n.Persistence,
                n.ActorDirection, n.Comments, n.ExternalVO, n.HasVO, n.HideSpeaker)).ToList();
            var strings = new StringTable(
                snap.Nodes.Select(n => new StringEntry(n.NodeId, n.DefaultText, n.FemaleText)));
            return new Conversation(f.Name, nodes, strings);
        }

        public IReadOnlyDictionary<string, string> LoadSpeakerNames() => new Dictionary<string, string>();
        public void SaveConversation(ConversationFile f, ConversationEditSnapshot s) { }
        public string GetStringTablePath(ConversationFile f) => "";
        public string GetStringTablePath(ConversationFile f, string language) => "";
        public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots() => ("", "");
        public ConversationFile BuildNewConversationFile(string name) => new(name, "", "", "");
        public void InitializeConversationFile(ConversationFile file) { }
    }

    [Fact]
    public void Poe2_ResolvesGuid_AndFollowsPatchedTarget()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe2Start("GUID-B", 7)])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(7, "b")]));

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A", "B"), provider, "en", "A", open,
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Contains(graph.Jumps,
            j => j.From == new NodeRef("A", 0) && j.To == new NodeRef("B", 7));
        Assert.True(graph.Conversations.ContainsKey("B"));
    }

    [Fact]
    public void Poe1_ResolvesStringName()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe1Start("B", 2)])]);
        var provider = new FakeJumpProvider { GameId = "poe1" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(2, "b")]));

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A", "B"), provider, "en", "A", open,
            new Dictionary<string, string>());   // PoE1 needs no GUID map

        Assert.Contains(graph.Jumps, j => j.To == new NodeRef("B", 2));
    }

    // Same parameter shape as StartConversation, starts nothing. If this fails, someone
    // replaced the verb whitelist with parameter-shape matching and the report now
    // contains phantom handoffs — silently, with no exception.
    [Fact]
    public void MarkConversationNodeAsRead_ProducesNoJump()
    {
        var mark = new ScriptCall("Void MarkConversationNodeAsRead(Guid, Int32)",
                                  ["GUID-B", "7"], ScriptCategory.Enter);
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [mark])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(7, "b")]));

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A", "B"), provider, "en", "A", open,
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Empty(graph.Jumps);
        Assert.Empty(graph.Unfollowed);
    }

    [Fact]
    public void UnpatchedTarget_IsReportedNotFollowed()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe2Start("GUID-B", 0)])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(0, "b")]));

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A"), provider, "en", "A", open,     // B not patched
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Empty(graph.Jumps);
        Assert.Contains(graph.Unfollowed,
            u => u.Reason == UnfollowedReason.NotPatched && u.TargetLabel == "B");
    }

    [Fact]
    public void UnresolvableGuid_IsReportedUnresolved()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe2Start("GUID-?", 0)])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A"), provider, "en", "A", open,
            new Dictionary<string, string>());

        Assert.Contains(graph.Unfollowed, u => u.Reason == UnfollowedReason.Unresolved);
    }

    [Fact]
    public void LoadFailure_IsReported_NotThrown()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe2Start("GUID-B", 0)])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.ThrowOnLoad("B");

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A", "B"), provider, "en", "A", open,
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Contains(graph.Unfollowed, u => u.Reason == UnfollowedReason.LoadFailed);
    }

    // NodeEditSnapshot.DefaultText is [JsonIgnore], so an ADDED node comes back from a
    // patch with empty text. Without the fix-up every patch-loaded conversation counts
    // zero words — wrong numbers, no exception, nothing else would catch it.
    [Fact]
    public void AddedNodeText_IsRestoredFromTranslations()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe2Start("GUID-B", 5)])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(5, "")]));  // empty

        var patchB = Patch("B") with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(5, "one two three", "")]
            }
        };
        var project = new DialogProject("test", DialogProject.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch> { ["A"] = Patch("A"), ["B"] = patchB });

        var graph = ConversationJumpResolver.Resolve(
            project, provider, "en", "A", open,
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Equal("one two three",
            graph.Conversations["B"].Nodes.Single(n => n.NodeId == 5).DefaultText);
    }

    [Fact]
    public void OnlyJumpReachableConversations_AreLoaded()
    {
        var open = new ConversationEditSnapshot([Node(0, "a")]);   // no jumps
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(0, "b")]));

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A", "B"), provider, "en", "A", open,
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Single(graph.Conversations);           // only the open one
        Assert.False(graph.Conversations.ContainsKey("B"));
    }
}
```

- [ ] **Step 2: Fix the patch translations in the text fix-up test**

`ConversationPatch.Translations` is `{ get; init; }` over an `IReadOnlyDictionary`, so it cannot be assigned after construction. In `AddedNodeText_IsRestoredFromTranslations`, build the patch with a `with` expression instead:

```csharp
var patchB = Patch("B") with
{
    Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
    {
        ["en"] = [new NodeTranslation(5, "one two three", "")]
    }
};
var project = new DialogProject("test", DialogProject.CurrentSchemaVersion,
    new Dictionary<string, ConversationPatch> { ["A"] = Patch("A"), ["B"] = patchB });
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test DialogEditor.Tests --filter "ConversationJumpResolverTests"`
Expected: FAIL — `ConversationJumpResolver` does not exist.

- [ ] **Step 4: Implement the resolver**

Create `DialogEditor.ViewModels/Services/ConversationJumpResolver.cs`:

```csharp
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// Resolves StartConversation handoffs into a pure MultiConversationGraph for
/// PathStatsService. Owns ALL the IO, patch application and GUID resolution, because
/// DialogEditor.Core has no project references and PathStatsService must stay pure.
///
/// Mirrors ProjectFindService's walk: the open conversation uses its live snapshot
/// (unsaved edits included); every other is vanilla + patch; an unreadable conversation
/// is warned and reported, never thrown.
/// Spec: docs/superpowers/specs/2026-09-10-cross-conversation-path-stats-design.md
public static class ConversationJumpResolver
{
    public static MultiConversationGraph Resolve(
        DialogProject project,
        IGameDataProvider provider,
        string primaryLanguage,
        string openConversationName,
        ConversationEditSnapshot openSnapshot,
        IReadOnlyDictionary<string, string> conversationNamesById,
        ScriptCatalogue? catalogue = null)
    {
        var cat        = catalogue ?? ScriptCatalogue.Instance;
        var loaded     = new Dictionary<string, ConversationEditSnapshot>
                             { [openConversationName] = openSnapshot };
        var jumps      = new List<JumpEdge>();
        var unfollowed = new List<UnfollowedJump>();
        var queue      = new Queue<string>();
        queue.Enqueue(openConversationName);

        while (queue.Count > 0)
        {
            var convName = queue.Dequeue();
            foreach (var node in loaded[convName].Nodes)
            foreach (var script in node.Scripts)
            {
                if (!ConversationJumpVerbs.IsJump(script.DisplayName)) continue;

                var from = new NodeRef(convName, node.NodeId);
                var target = ResolveTarget(script, cat, conversationNamesById);
                if (target is null)
                {
                    unfollowed.Add(new UnfollowedJump(
                        from, Loc.Get("PathStats_UnknownTarget"), UnfollowedReason.Unresolved));
                    continue;
                }
                var (name, entryNode) = target.Value;

                if (!project.Patches.ContainsKey(name))
                {
                    unfollowed.Add(new UnfollowedJump(from, name, UnfollowedReason.NotPatched));
                    continue;
                }

                if (!loaded.ContainsKey(name))
                {
                    var snap = TryLoad(project, provider, primaryLanguage, name);
                    if (snap is null)
                    {
                        unfollowed.Add(new UnfollowedJump(from, name, UnfollowedReason.LoadFailed));
                        continue;
                    }
                    loaded[name] = snap;
                    queue.Enqueue(name);
                }
                jumps.Add(new JumpEdge(from, new NodeRef(name, entryNode)));
            }
        }

        return new MultiConversationGraph(openConversationName, loaded, jumps, unfollowed);
    }

    /// Locates the conversation and entry-node arguments by CATALOGUE METADATA, never by
    /// index: PoE1 is (Guid, String, Int32) and PoE2 is (Guid, Guid, Int32), so positions
    /// and types differ between games. Resolution then branches on the declared Type —
    /// PoE2 carries a GUID needing the map, PoE1 carries the name itself.
    private static (string Name, int EntryNode)? ResolveTarget(
        ScriptCall script, ScriptCatalogue catalogue,
        IReadOnlyDictionary<string, string> namesById)
    {
        var entry = catalogue.FindByFullName(script.FullName);
        if (entry is null) return null;

        var convIndex = -1;
        var nodeIndex = -1;
        for (var i = 0; i < entry.Parameters.Count; i++)
        {
            var p = entry.Parameters[i];
            if (string.Equals(p.LookupKind, "Conversation", StringComparison.Ordinal))
                convIndex = i;
            else if (string.Equals(p.Name, "Conversation Node ID", StringComparison.Ordinal))
                nodeIndex = i;
        }
        if (convIndex < 0 || convIndex >= script.Parameters.Count) return null;

        var raw = script.Parameters[convIndex];
        var name = string.Equals(entry.Parameters[convIndex].Type, "Guid", StringComparison.Ordinal)
            ? namesById.GetValueOrDefault(raw)
            : raw;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var entryNode = 0;
        if (nodeIndex >= 0 && nodeIndex < script.Parameters.Count)
            int.TryParse(script.Parameters[nodeIndex], out entryNode);

        return (name, entryNode);
    }

    private static ConversationEditSnapshot? TryLoad(
        DialogProject project, IGameDataProvider provider,
        string primaryLanguage, string convName)
    {
        try
        {
            var patch    = project.Patches[convName];
            var file     = provider.FindConversation(convName);
            var baseSnap = file is not null
                ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                : new ConversationEditSnapshot([]);
            var applied  = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
            return WithTextRestored(applied, patch, primaryLanguage);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Path stats: could not load '{convName}': {ex.Message}");
            return null;
        }
    }

    /// NodeEditSnapshot.DefaultText/FemaleText are [JsonIgnore], so nodes the writer ADDED
    /// come back from a patch empty. Without this every patch-loaded conversation would
    /// count zero words — wrong numbers, and nothing would throw. Same fallback
    /// ProjectFindService performs.
    private static ConversationEditSnapshot WithTextRestored(
        ConversationEditSnapshot snap, ConversationPatch patch, string primaryLanguage)
    {
        var byId = (patch.Translations.GetValueOrDefault(primaryLanguage) ?? [])
            .ToDictionary(t => t.NodeId);
        if (byId.Count == 0) return snap;

        return new ConversationEditSnapshot(snap.Nodes.Select(n =>
        {
            if (!string.IsNullOrEmpty(n.DefaultText) && !string.IsNullOrEmpty(n.FemaleText))
                return n;
            if (!byId.TryGetValue(n.NodeId, out var t)) return n;
            return n with
            {
                DefaultText = string.IsNullOrEmpty(n.DefaultText) ? t.DefaultText ?? "" : n.DefaultText,
                FemaleText  = string.IsNullOrEmpty(n.FemaleText)  ? t.FemaleText  ?? "" : n.FemaleText,
            };
        }).ToList());
    }
}
```

- [ ] **Step 5: Add the placeholder resource key**

In `DialogEditor.Avalonia/Resources/Strings.axaml`:

```xml
<sys:String x:Key="PathStats_UnknownTarget">(unknown target)</sys:String>
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test DialogEditor.Tests --filter "ConversationJumpResolverTests"`
Expected: PASS, all nine.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.ViewModels/Services/ConversationJumpResolver.cs DialogEditor.Tests/Services/ConversationJumpResolverTests.cs DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat(path-stats): resolve conversation handoffs into a pure graph (#14)"
```

---

### Task 5: Settings flag and ViewModel wiring

**Files:**
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs`, `DialogEditor.Tests/Services/AppSettingsTests.cs`

**Interfaces:**
- Consumes: `MultiConversationGraph` (Task 1), `PathStatsReport.Unfollowed` / `.ConversationsSpanned` (Task 1).
- Produces: `FlowAnalyticsViewModel.FollowConversationJumps`; constructor parameters `resolveGraph`, `followConversationJumps`, `persistFollowJumps`, `navigateToNodeInConversation`; `PathBranchRowViewModel.ConversationName` / `.IsInAnotherConversation` (same on `PathEndingRowViewModel`); `ConversationsSpannedText`; `bool HasSpannedConversations`; `ObservableCollection<UnfollowedJumpRowViewModel> UnfollowedJumpRows` with `TargetLabel` and `ReasonText`; `AppSettings.FollowConversationJumps`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void ToggleOff_UsesTheSnapshotOverload()
{
    var called = false;
    var vm = new FlowAnalyticsViewModel(
        getSnapshot: () => Snap(Node(0, "a")),
        navigateToNode: _ => { },
        resolveGraph: () => { called = true; return EmptyGraph(); },
        followConversationJumps: false);

    vm.RefreshCommand.Execute(null);

    Assert.False(called);
}

[Fact]
public void ToggleOn_UsesTheResolver()
{
    var called = false;
    var vm = new FlowAnalyticsViewModel(
        getSnapshot: () => Snap(Node(0, "a")),
        navigateToNode: _ => { },
        resolveGraph: () => { called = true; return EmptyGraph(); },
        followConversationJumps: true);

    vm.RefreshCommand.Execute(null);

    Assert.True(called);
}

[Fact]
public void TogglingPersistsAndRefreshes()
{
    bool? persisted = null;
    var vm = new FlowAnalyticsViewModel(
        getSnapshot: () => Snap(Node(0, "a")),
        navigateToNode: _ => { },
        resolveGraph: EmptyGraph,
        persistFollowJumps: v => persisted = v);

    vm.FollowConversationJumps = true;

    Assert.True(persisted);
}

[Fact]
public void ToggleOn_WithNoResolver_FallsBackSafely()
{
    var vm = new FlowAnalyticsViewModel(
        getSnapshot: () => Snap(Node(0, "a")),
        navigateToNode: _ => { },
        followConversationJumps: true);       // resolveGraph is null

    vm.RefreshCommand.Execute(null);          // must not throw

    Assert.True(vm.HasPathStats);
}

[Fact]
public void CrossConversationRow_NavigatesThroughTheTwoArgumentDelegate()
{
    (string Conv, int Node)? went = null;
    var vm = new FlowAnalyticsViewModel(
        getSnapshot: () => Snap(Node(0, "a")),
        navigateToNode: _ => Assert.Fail("should use the cross-conversation delegate"),
        resolveGraph: GraphWithForkInB,
        followConversationJumps: true,
        navigateToNodeInConversation: (c, n) => went = (c, n));

    vm.RefreshCommand.Execute(null);
    vm.Branches.Single(b => b.IsInAnotherConversation).NavigateCommand.Execute(null);

    Assert.Equal(("B", 1), went);
}
```

Add the `EmptyGraph()` and `GraphWithForkInB()` helpers to the test class, built exactly as in Task 2 Step 1.

And in `DialogEditor.Tests/Services/AppSettingsTests.cs`, the round-trip. Note this suite runs serially precisely because `AppSettings` is global state, so restore the previous value in a `finally`:

```csharp
[Fact]
public void FollowConversationJumps_RoundTrips()
{
    var original = AppSettings.FollowConversationJumps;
    try
    {
        AppSettings.FollowConversationJumps = true;
        Assert.True(AppSettings.FollowConversationJumps);

        AppSettings.FollowConversationJumps = false;
        Assert.False(AppSettings.FollowConversationJumps);
    }
    finally
    {
        AppSettings.FollowConversationJumps = original;
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test DialogEditor.Tests --filter "FlowAnalyticsViewModelTests"`
Expected: FAIL — the new constructor parameters do not exist.

- [ ] **Step 3: Add the settings flag**

In `DialogEditor.ViewModels/Services/AppSettings.cs`, beside `ReadingWordsPerMinute`:

```csharp
// in the settings DTO, near line 82
public bool FollowConversationJumps { get; set; } = false;

// alongside the other static accessors, near line 257
/// Whether Playthrough stats follow StartConversation handoffs into conversations this
/// project patches. Off by default: turning it on changes existing figures, so the
/// writer opts in rather than discovering a doubled number.
public static bool FollowConversationJumps
{
    get => Load().FollowConversationJumps;
    set { var s = Load(); s.FollowConversationJumps = value; Save(s); }
}
```

- [ ] **Step 4: Extend the ViewModel**

Add the four optional constructor parameters (all defaulted, so existing call sites and tests compile unchanged), store them in readonly fields, and assign the toggle's **backing field** directly — going through the property would fire the changed-handler and persist a value we were just handed, exactly as the `_wordsPerMinute` comment at line 176 explains:

```csharp
[ObservableProperty] private bool _followConversationJumps;

// ... in the constructor, after the wordsPerMinute assignment:
_followConversationJumps = followConversationJumps;
_persistFollowJumps      = persistFollowJumps;
_resolveGraph            = resolveGraph;
_navigateToNodeInConv    = navigateToNodeInConversation;

partial void OnFollowConversationJumpsChanged(bool value)
{
    _persistFollowJumps?.Invoke(value);
    Refresh();
}
```

In `RefreshPathStats`, choose the overload:

```csharp
var report = (FollowConversationJumps && _resolveGraph is not null)
    ? PathStatsService.Analyze(_resolveGraph())
    : PathStatsService.Analyze(snapshot);
```

Give `PathBranchRowViewModel` and `PathEndingRowViewModel` a `ConversationName` and `bool IsInAnotherConversation`, set from `stat.Choice.Conversation != report-root` when building rows, and route their `NavigateCommand` to `_navigateToNodeInConv` when `IsInAnotherConversation` is true, otherwise `_navigateToNode`. Populate `ConversationsSpannedText` (only when `report.ConversationsSpanned > 1`) via `Loc.Format`, and an `UnfollowedJumpRows` collection from `report.Unfollowed`.

- [ ] **Step 5: Run the tests**

Run: `dotnet test DialogEditor.Tests --filter "FlowAnalyticsViewModelTests|AppSettingsTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels DialogEditor.Tests/ViewModels DialogEditor.Tests/Services/AppSettingsTests.cs
git commit -m "feat(flow-analytics): opt-in toggle for following conversation handoffs (#14)"
```

---

### Task 6: Window, resource keys, and MainWindowViewModel wiring

**Files:**
- Modify: `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Test: `DialogEditor.Tests/Views/FlowAnalyticsWindowTests.cs`

**Interfaces:**
- Consumes: everything from Task 5.
- Produces: the shipped UI.

- [ ] **Step 1: Write the failing smoke test**

```csharp
[Fact]
public void Window_ConstructsWithJumpRowsAndUnfollowedLeaves()
{
    var vm = new FlowAnalyticsViewModel(
        getSnapshot: () => Snap(Node(0, "a")),
        navigateToNode: _ => { },
        resolveGraph: GraphWithForkInB,
        followConversationJumps: true);
    vm.RefreshCommand.Execute(null);

    var window = new FlowAnalyticsWindow { DataContext = vm };

    Assert.NotNull(window);
    Assert.True(vm.FollowConversationJumps);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test DialogEditor.Tests --filter "FlowAnalyticsWindowTests"`
Expected: FAIL — the constructor parameters do not exist on the VM used by this test file until Task 5 is merged; if Task 5 is already merged, it fails on the missing XAML bindings.

- [ ] **Step 3: Add the resource keys**

In `DialogEditor.Avalonia/Resources/Strings.axaml`:

```xml
<sys:String x:Key="FlowAnalytics_FollowJumps">Follow conversation jumps</sys:String>
<sys:String x:Key="FlowAnalytics_FollowJumps_Tip">Counts content reached through Start Conversation handoffs into conversations this project patches. Figures will be larger than the single-conversation reading. Handoffs into conversations the project does not patch are listed but not counted.</sys:String>
<sys:String x:Key="FlowAnalytics_SpanningConversations">Spanning {0} conversations</sys:String>
<sys:String x:Key="FlowAnalytics_InConversation">in {0}</sys:String>
<sys:String x:Key="FlowAnalytics_UnfollowedHeader">Handoffs not followed</sys:String>
<sys:String x:Key="FlowAnalytics_Unfollowed_NotPatched">not patched by this project</sys:String>
<sys:String x:Key="FlowAnalytics_Unfollowed_Unresolved">target not found</sys:String>
<sys:String x:Key="FlowAnalytics_Unfollowed_LoadFailed">could not be loaded</sys:String>
<sys:String x:Key="FlowAnalytics_EndingsHint_WithJumps">An ending is a node with no outgoing links and no handoff. These figures need not reconcile with the longest playthrough above.</sys:String>
```

- [ ] **Step 4: Add the toggle to the window**

In `FlowAnalyticsWindow.axaml`, in the Playthrough-stats header strip beside the reading-speed picker:

```xml
<CheckBox IsChecked="{Binding FollowConversationJumps}"
          Content="{DynamicResource FlowAnalytics_FollowJumps}"
          ToolTip.Tip="{DynamicResource FlowAnalytics_FollowJumps_Tip}"
          AutomationProperties.Name="{DynamicResource FlowAnalytics_FollowJumps}"
          AutomationProperties.HelpText="{DynamicResource FlowAnalytics_FollowJumps_Tip}" />
```

The tooltip is mandatory (CLAUDE.md) and `AutomationProperties.Name` keeps the control findable by `tools/ui-automation/DriveApp.ps1`.

- [ ] **Step 5: Add the spanned line, conversation tags, and unfollowed section**

Bind the spanned line's visibility to a `HasSpannedConversations` bool. On branch and ending rows, add a `TextBlock` bound to `ConversationName` whose `IsVisible` is bound to `IsInAnotherConversation`, so single-file reports look exactly as they do today. Add the "Handoffs not followed" `ItemsControl` bound to `UnfollowedJumpRows`, each row showing the target label and its reason **as text** — no colour-only encoding.

- [ ] **Step 6: Wire `MainWindowViewModel`**

Cache the conversation GUID map where the folder-open handler already iterates `LoadGameDataNames()` (near line 1716) — the pairs are in hand, so this costs nothing and avoids re-parsing every bundle per analysis:

```csharp
// Conversation GUID → name, cached here because LoadGameDataNames parses every bundle
// on disk and is documented as called once per folder open. Used by
// ConversationJumpResolver; GameDataNameService is unsuitable because it stores
// NamedEntry(DisplayName, StoredValue) with DisplayName composed as "{name} — {id}".
foreach (var (kind, entries) in provider.LoadGameDataNames())
{
    if (kind == "Conversation")
        _conversationNamesById = entries
            .Where(e => !string.IsNullOrEmpty(e.Id))
            .ToDictionary(e => e.Id, e => e.Name);
    // ... existing NamedEntry registration unchanged
}
```

Then, where `FlowAnalyticsViewModel` is constructed, pass:

```csharp
resolveGraph: () => ConversationJumpResolver.Resolve(
    _project!, _provider!, _provider!.Language,
    Canvas.ConversationName ?? "", Canvas.BuildSnapshot(),
    _conversationNamesById),
followConversationJumps: AppSettings.FollowConversationJumps,
persistFollowJumps: v => AppSettings.FollowConversationJumps = v,
navigateToNodeInConversation: NavigateToFoundNode,
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS, everything.

- [ ] **Step 8: Verify in the real app**

Use the `running-the-app` skill. Open a project, press F7, confirm: the checkbox appears with its tooltip; ticking it changes the figures and shows the spanned line; a cross-conversation fork row shows its conversation tag and its Go button navigates there; unfollowed handoffs are listed with reasons. Unit tests passing is not the same as the app working.

- [ ] **Step 9: Commit**

```bash
git add DialogEditor.Avalonia DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/Views/FlowAnalyticsWindowTests.cs
git commit -m "feat(flow-analytics): follow-jumps toggle and handoff rows in the window (#14)"
```

---

## Closing out

- [ ] Update `#14`'s checklist: tick **Cross-conversation paths**, describing the settled decisions (project-patched boundary, additive spawn arithmetic, opt-in toggle, refined ending rule) the way the fork/ending item documents its own.
- [ ] Mark the deferral shipped in `docs/superpowers/specs/2026-07-13-path-based-writing-stats-design.md` — strike "Path stats across conversations (playthroughs that jump conversation files)" and point at the new spec.
- [ ] Do **not** touch `CHANGELOG.md` — frozen until the initial public release.
