# Selective Apply (Spec 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** From the compare-versions window, let a writer tick individual changed dialogue lines and **bring them into their own copy** of the `.dialogproject`, with a live "applied preview", a save-first guard, a single-step undo, and plain-language help.

**Architecture:** A pure, git-free engine (`NodeApplyBuilder`) copies a selected node's full patch contribution from the *source* project into the *target* (working-copy) project; a pure `NodeLinkAnalyzer` flags links left dangling by the selection. `DiffViewModel` gains a two-level checkable selection tree, a `CanApply` gate, a `Changes`/`Applied preview` canvas mode, and a `CommitApply` callback. `MainWindowViewModel.ApplyFromDiff` performs the save-guard, snapshots the pre-apply project, swaps + saves it, and exposes a single-step undo. The UI adds the tree, an apply bar, a segmented toggle, a colour-key strip, and a `DiffHelpWindow`. A terminology pass keeps all user-facing strings jargon-free.

**Tech Stack:** C# 12 / .NET 8, CommunityToolkit.Mvvm, Avalonia 11.3, xUnit, `Avalonia.Headless.XUnit`.

---

## Context

Spec 1 (read-only diff viewer) shipped on `main`. This implements **Spec 2** (`docs/superpowers/specs/2026-05-30-selective-apply-design.md`): selective apply. Read that spec first.

Key facts:
- A `DialogProject` is `{ Name, SchemaVersion, Patches: Dictionary<string, ConversationPatch>, Layouts?, NewConversations? }`. Records are immutable; use `with`.
- A `ConversationPatch` is `(ConversationName, SchemaVersion, AddedNodes: IReadOnlyList<NodeEditSnapshot>, DeletedNodeIds: IReadOnlyList<int>, ModifiedNodes: IReadOnlyList<NodeModification>)` plus init-only `Translations: IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>` and `NodeComments`.
- `NodeEditSnapshot` has `.NodeId` and `.Links: IReadOnlyList<LinkEditSnapshot>`; `LinkEditSnapshot` has `.ToNodeId`. `NodeModification` has `.NodeId`, `.AddedLinks: IReadOnlyList<LinkEditSnapshot>`, `.ModifiedLinks: IReadOnlyList<ModifiedLink>` (`.ToNodeId`). `NodeTranslation` has `.NodeId`.
- **Apply rule (direction-agnostic):** for each selected node, make the target patch's contribution for that node identical to the source's. "Source has no contribution" is a valid value meaning "revert that node to the base game file" (strip it from the target patch).

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `DialogEditor.Patch/Diff/NodeSelection.cs` | Create | `NodeSelection(ConversationName, NodeId)` record struct. |
| `DialogEditor.Patch/Diff/NodeApplyBuilder.cs` | Create | Pure: copy selected nodes' contributions source→target. |
| `DialogEditor.Patch/Diff/DanglingLink.cs` | Create | `DanglingLink(Conversation, FromNode, ToNode)` record struct. |
| `DialogEditor.Patch/Diff/NodeLinkAnalyzer.cs` | Create | Pure: flag links to nodes deleted in the same patch. |
| `DialogEditor.ViewModels/ViewModels/NodeChangeViewModel.cs` | Create | One checkable changed line (kind + id + IsSelected). |
| `DialogEditor.ViewModels/ViewModels/ConversationChangeViewModel.cs` | Create | Expandable, tri-state group of `NodeChangeViewModel`. |
| `DialogEditor.ViewModels/ViewModels/CanvasMode.cs` | Create | `enum CanvasMode { Changes, AppliedPreview }`. |
| `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` | Modify | Selection tree, `CanApply`, target/source, `ApplyCommand`, `CommitApply`, preview. |
| `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Modify | `ApplyFromDiff`, `ConfirmSaveBeforeApply`, `UndoApplyCommand`, pre-apply snapshot. |
| `DialogEditor.Avalonia/Views/DiffWindow.axaml(.cs)` | Modify | Checkable tree, segmented toggle, colour-key strip, apply bar, `?` button, warning panel. |
| `DialogEditor.Avalonia/Views/DiffHelpWindow.axaml(.cs)` | Create | Plain-language legend + prose help window. |
| `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Modify | Wire `CommitApply` + `ConfirmSaveBeforeApply` when opening the diff window. |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | Modify | New jargon-free strings; terminology pass on touched Spec 1 strings. |
| `DialogEditor.Tests/Patch/Diff/NodeApplyBuilderTests.cs` | Create | Engine unit tests. |
| `DialogEditor.Tests/Patch/Diff/NodeLinkAnalyzerTests.cs` | Create | Analyzer unit tests. |
| `DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs` | Create | Selection/CanApply/ApplyCommand/preview tests. |
| `DialogEditor.Tests/ViewModels/MainWindowViewModelApplyTests.cs` | Create | `ApplyFromDiff` save-guard/snapshot/undo tests. |
| `DialogEditor.Tests/Views/DiffHelpWindowTests.cs` | Create | Headless window smoke test. |

## Reusable existing code & conventions
- `DialogProject` / `ConversationPatch` / `NodeModification` / `NodeEditSnapshot` / `LinkEditSnapshot` / `NodeTranslation` — `DialogEditor.Patch/` & `DialogEditor.Core/`.
- `DiffEndpoint` (`WorkingCopy` | `GitRef`) — `DialogEditor.Patch/Diff/DiffEndpoint.cs`; test with `o.Endpoint is DiffEndpoint.WorkingCopy`.
- `DiffViewModel` already loads `_leftProject` / `_rightProject` and reconstructs a conversation in `BuildDiffCanvas` via `ReconstructConversation` + `ConversationViewModel.Load` + `NodeViewModel.DiffStatus`.
- `MainWindowViewModel.SetProject(...)` swaps the in-memory `_project` (used by conflict-merge); `SaveProject()` serialises + clears `IsModified`; `IsModified` is the dirty flag.
- Existing host-callback pattern: `MainWindowViewModel.RequestConflictResolution` (a `Func<...,Task<bool>>` set in `MainWindow.axaml.cs`).
- `LegendWindow.axaml` — model `DiffHelpWindow` on it (scrollable, sectioned, resources, app icon).
- Conventions: strict red/green TDD; every `<Window>` needs a public parameterless ctor and `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`; every control gets a jargon-free `ToolTip`; all strings from `Strings.axaml`; every caught exception logged via `AppLog`; tests in `DialogEditor.Tests` mirror structure; `[Fact]` for pure, `[AvaloniaFact]` for headless; in headless VM tests invoke commands via `Command!.Execute(null)` / `CanExecute(null)`.
- Run a single test: `dotnet test --filter "FullyQualifiedName~<TestName>"`. Commit messages end with a trailing `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` line.

---

# Stage 1 — Pure apply engine

### Task 1: `NodeSelection` + `NodeApplyBuilder` — adopt Added & Modified nodes

**Files:**
- Create: `DialogEditor.Patch/Diff/NodeSelection.cs`
- Create: `DialogEditor.Patch/Diff/NodeApplyBuilder.cs`
- Test: `DialogEditor.Tests/Patch/Diff/NodeApplyBuilderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class NodeApplyBuilderTests
{
    // Builds a project with one conversation patch.
    private static DialogProject Project(string name, ConversationPatch patch) =>
        new("p", DialogProject.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch> { [name] = patch });

    private static ConversationPatch Patch(
        string name,
        IReadOnlyList<NodeEditSnapshot>? added = null,
        IReadOnlyList<int>? deleted = null,
        IReadOnlyList<NodeModification>? modified = null) =>
        new(name, ConversationPatch.CurrentSchemaVersion,
            added ?? [], deleted ?? [], modified ?? []);

    private static NodeEditSnapshot Node(int id) =>
        new(id, false, default, "", "", "", "", "", "", "", "", "", false, false, [], [], []);

    private static NodeModification Mod(int id) =>
        new(id, new Dictionary<string, FieldChange>(), [], [], []);

    [Fact]
    public void Apply_BringsInAnAddedNode_FromSource()
    {
        var target = Project("c", Patch("c"));
        var source = Project("c", Patch("c", added: [Node(7)]));

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        Assert.Contains(result.Patches["c"].AddedNodes, n => n.NodeId == 7);
    }

    [Fact]
    public void Apply_BringsInAModifiedNode_FromSource()
    {
        var target = Project("c", Patch("c"));
        var source = Project("c", Patch("c", modified: [Mod(7)]));

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        Assert.Contains(result.Patches["c"].ModifiedNodes, m => m.NodeId == 7);
    }

    [Fact]
    public void Apply_ReplacesTargetsOwnVersion_WithSources()
    {
        // target already modifies node 7; source modifies it differently (added link).
        var target = Project("c", Patch("c", modified: [Mod(7)]));
        var srcMod = new NodeModification(7, new Dictionary<string, FieldChange>(),
            [new LinkEditSnapshot(7, 99, 1f, "", false)], [], []);
        var source = Project("c", Patch("c", modified: [srcMod]));

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        var mod = Assert.Single(result.Patches["c"].ModifiedNodes, m => m.NodeId == 7);
        Assert.Single(mod.AddedLinks);   // took source's version, not target's empty one
    }

    [Fact]
    public void Apply_EmptySelection_ReturnsTargetUnchanged()
    {
        var target = Project("c", Patch("c", added: [Node(1)]));
        var result = NodeApplyBuilder.Apply(target, target, []);
        Assert.Same(target, result);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~NodeApplyBuilderTests"`
Expected: FAIL — `NodeSelection` / `NodeApplyBuilder` do not exist.

- [ ] **Step 3: Write the minimal implementation**

`NodeSelection.cs`:
```csharp
namespace DialogEditor.Patch.Diff;

/// One picked node: which conversation, which node id.
public readonly record struct NodeSelection(string ConversationName, int NodeId);
```

`NodeApplyBuilder.cs`:
```csharp
using DialogEditor.Core.Models;

namespace DialogEditor.Patch.Diff;

/// Cherry-picks individual nodes from a source project's patches into a target
/// project's patches. For each selected node the target's contribution is made
/// identical to the source's (which may be "nothing" — i.e. revert to base).
/// Pure; never mutates its inputs.
public static class NodeApplyBuilder
{
    public static DialogProject Apply(
        DialogProject target,
        DialogProject source,
        IReadOnlyList<NodeSelection> selected)
    {
        if (selected.Count == 0) return target;

        var byConv = selected
            .GroupBy(s => s.ConversationName)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<int>)g.Select(s => s.NodeId).ToHashSet());

        var patches = new Dictionary<string, ConversationPatch>(target.Patches);
        foreach (var (conv, ids) in byConv)
        {
            var targetPatch = target.Patches.GetValueOrDefault(conv) ?? Empty(conv);
            var sourcePatch = source.Patches.GetValueOrDefault(conv);
            patches[conv] = ApplyNodes(targetPatch, sourcePatch, ids);
        }

        return target with { Patches = patches };
    }

    private static ConversationPatch Empty(string conv) =>
        new(conv, ConversationPatch.CurrentSchemaVersion, [], [], []);

    private static ConversationPatch ApplyNodes(
        ConversationPatch target, ConversationPatch? source, IReadOnlySet<int> ids)
    {
        // 1. Strip every selected id out of all of target's buckets.
        var added    = target.AddedNodes.Where(n => !ids.Contains(n.NodeId)).ToList();
        var modified = target.ModifiedNodes.Where(m => !ids.Contains(m.NodeId)).ToList();
        var deleted  = target.DeletedNodeIds.Where(d => !ids.Contains(d)).ToList();
        var translations = target.Translations.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Where(t => !ids.Contains(t.NodeId)).ToList());

        // 2. Copy source's contribution for each selected id (if any).
        if (source is not null)
        {
            added.AddRange(source.AddedNodes.Where(n => ids.Contains(n.NodeId)));
            modified.AddRange(source.ModifiedNodes.Where(m => ids.Contains(m.NodeId)));
            deleted.AddRange(source.DeletedNodeIds.Where(ids.Contains));
            foreach (var (lang, list) in source.Translations)
            {
                var picked = list.Where(t => ids.Contains(t.NodeId)).ToList();
                if (picked.Count == 0) continue;
                if (!translations.TryGetValue(lang, out var existing))
                    translations[lang] = existing = new List<NodeTranslation>();
                existing.AddRange(picked);
            }
        }

        var cleaned = translations
            .Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<NodeTranslation>)kv.Value);

        return target with
        {
            AddedNodes     = added,
            ModifiedNodes  = modified,
            DeletedNodeIds = deleted,
            Translations   = cleaned,
        };
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~NodeApplyBuilderTests"`
Expected: PASS (all four).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Diff/NodeSelection.cs DialogEditor.Patch/Diff/NodeApplyBuilder.cs DialogEditor.Tests/Patch/Diff/NodeApplyBuilderTests.cs
git commit -m "feat: NodeApplyBuilder — bring in added/modified nodes

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `NodeApplyBuilder` — revert (source absent), source-deleted, translations, multi-conversation, immutability

**Files:**
- Test: `DialogEditor.Tests/Patch/Diff/NodeApplyBuilderTests.cs` (add cases)
- (No implementation change expected — the Task 1 engine should already satisfy these. If a test fails, fix `NodeApplyBuilder` minimally.)

- [ ] **Step 1: Add the failing tests**

```csharp
    private static NodeTranslation Tr(int id) => new(id, "hello", "");

    [Fact]
    public void Apply_RevertsNode_WhenSourceHasNoContribution()
    {
        // target adds node 7; source has no patch entry for it → bringing in "source's
        // version" means removing 7 from the target patch (revert to base).
        var target = Project("c", Patch("c", added: [Node(7)]));
        var source = Project("c", Patch("c"));

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        Assert.DoesNotContain(result.Patches["c"].AddedNodes, n => n.NodeId == 7);
    }

    [Fact]
    public void Apply_BringsInADeletion_FromSource()
    {
        var target = Project("c", Patch("c"));
        var source = Project("c", Patch("c", deleted: [7]));

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        Assert.Contains(7, result.Patches["c"].DeletedNodeIds);
    }

    [Fact]
    public void Apply_BringsInATranslation_AndDropsTargetsOwn()
    {
        var target = Project("c", Patch("c") with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { ["en"] = [new NodeTranslation(7, "OLD", "")] }
        });
        var source = Project("c", Patch("c") with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { ["en"] = [Tr(7)] }
        });

        var result = NodeApplyBuilder.Apply(target, source, [new("c", 7)]);

        var en = result.Patches["c"].Translations["en"];
        Assert.Equal("hello", Assert.Single(en, t => t.NodeId == 7).DefaultText);
    }

    [Fact]
    public void Apply_OnlyTouchesSelectedConversations()
    {
        var target = new DialogProject("p", DialogProject.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch>
            {
                ["a"] = Patch("a", added: [Node(1)]),
                ["b"] = Patch("b", added: [Node(2)]),
            });
        var source = new DialogProject("p", DialogProject.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch>
            {
                ["a"] = Patch("a", added: [Node(1), Node(9)]),
                ["b"] = Patch("b"),
            });

        var result = NodeApplyBuilder.Apply(target, source, [new("a", 9)]);

        Assert.Contains(result.Patches["a"].AddedNodes, n => n.NodeId == 9);
        Assert.Contains(result.Patches["b"].AddedNodes, n => n.NodeId == 2); // b untouched
    }

    [Fact]
    public void Apply_DoesNotMutateInputs()
    {
        var targetPatch = Patch("c", added: [Node(1)]);
        var target = Project("c", targetPatch);
        var source = Project("c", Patch("c", added: [Node(9)]));

        NodeApplyBuilder.Apply(target, source, [new("c", 9)]);

        Assert.Single(target.Patches["c"].AddedNodes);          // still just node 1
        Assert.Single(targetPatch.AddedNodes);
    }
```

- [ ] **Step 2: Run to verify** — `dotnet test --filter "FullyQualifiedName~NodeApplyBuilderTests"`. Expected: all PASS. If `Apply_DoesNotMutateInputs` or any other fails, the engine mutated a shared list — fix by ensuring every bucket is rebuilt with `.ToList()` (it already is) and re-run.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/Patch/Diff/NodeApplyBuilderTests.cs
git commit -m "test: NodeApplyBuilder revert/delete/translation/multi-conv/immutability

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

# Stage 2 — Dangling-link analyzer

### Task 3: `DanglingLink` + `NodeLinkAnalyzer`

**Files:**
- Create: `DialogEditor.Patch/Diff/DanglingLink.cs`
- Create: `DialogEditor.Patch/Diff/NodeLinkAnalyzer.cs`
- Test: `DialogEditor.Tests/Patch/Diff/NodeLinkAnalyzerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class NodeLinkAnalyzerTests
{
    private static DialogProject Project(string name, ConversationPatch patch) =>
        new("p", DialogProject.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch> { [name] = patch });

    private static ConversationPatch Patch(
        string name,
        IReadOnlyList<NodeEditSnapshot>? added = null,
        IReadOnlyList<int>? deleted = null,
        IReadOnlyList<NodeModification>? modified = null) =>
        new(name, ConversationPatch.CurrentSchemaVersion, added ?? [], deleted ?? [], modified ?? []);

    private static NodeEditSnapshot NodeWithLink(int id, int toId) =>
        new(id, false, default, "", "", "", "", "", "", "", "", "", false, false,
            [new LinkEditSnapshot(id, toId, 1f, "", false)], [], []);

    [Fact]
    public void Analyze_FlagsAddedNodeLink_ToADeletedNode()
    {
        var project = Project("c", Patch("c", added: [NodeWithLink(5, 8)], deleted: [8]));

        var dangling = NodeLinkAnalyzer.Analyze(project);

        var link = Assert.Single(dangling);
        Assert.Equal(("c", 5, 8), (link.Conversation, link.FromNode, link.ToNode));
    }

    [Fact]
    public void Analyze_FlagsModifiedNodeAddedLink_ToADeletedNode()
    {
        var mod = new NodeModification(5, new Dictionary<string, FieldChange>(),
            [new LinkEditSnapshot(5, 8, 1f, "", false)], [], []);
        var project = Project("c", Patch("c", deleted: [8], modified: [mod]));

        var dangling = NodeLinkAnalyzer.Analyze(project);

        Assert.Single(dangling);
    }

    [Fact]
    public void Analyze_ReturnsEmpty_WhenNoLinkTargetsADeletedNode()
    {
        var project = Project("c", Patch("c", added: [NodeWithLink(5, 6)]));
        Assert.Empty(NodeLinkAnalyzer.Analyze(project));
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter "FullyQualifiedName~NodeLinkAnalyzerTests"`. Expected: FAIL — types don't exist.

- [ ] **Step 3: Write the implementation**

`DanglingLink.cs`:
```csharp
namespace DialogEditor.Patch.Diff;

/// A link in the applied result whose target node will not exist after apply.
public readonly record struct DanglingLink(string Conversation, int FromNode, int ToNode);
```

`NodeLinkAnalyzer.cs`:
```csharp
namespace DialogEditor.Patch.Diff;

/// Best-effort, patch-level dangling-link detection. Flags any link — from an
/// added node's Links, or a modified node's AddedLinks/ModifiedLinks — whose
/// target node is deleted by the same conversation's patch. Does not reconstruct
/// against base game data (full reachability is deferred); pairs with the
/// "warn, but allow" stance.
public static class NodeLinkAnalyzer
{
    public static IReadOnlyList<DanglingLink> Analyze(DialogProject projected)
    {
        var result = new List<DanglingLink>();

        foreach (var (conv, patch) in projected.Patches)
        {
            var deleted = patch.DeletedNodeIds.ToHashSet();
            if (deleted.Count == 0) continue;

            foreach (var n in patch.AddedNodes)
                foreach (var link in n.Links)
                    if (deleted.Contains(link.ToNodeId))
                        result.Add(new DanglingLink(conv, n.NodeId, link.ToNodeId));

            foreach (var m in patch.ModifiedNodes)
            {
                foreach (var link in m.AddedLinks)
                    if (deleted.Contains(link.ToNodeId))
                        result.Add(new DanglingLink(conv, m.NodeId, link.ToNodeId));
                foreach (var link in m.ModifiedLinks)
                    if (deleted.Contains(link.ToNodeId))
                        result.Add(new DanglingLink(conv, m.NodeId, link.ToNodeId));
            }
        }

        return result;
    }
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test --filter "FullyQualifiedName~NodeLinkAnalyzerTests"`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Diff/DanglingLink.cs DialogEditor.Patch/Diff/NodeLinkAnalyzer.cs DialogEditor.Tests/Patch/Diff/NodeLinkAnalyzerTests.cs
git commit -m "feat: NodeLinkAnalyzer — flag links to nodes deleted in the same patch

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

# Stage 3 — Selection model in `DiffViewModel`

### Task 4: `NodeChangeViewModel` + `ConversationChangeViewModel` (tri-state)

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/NodeChangeViewModel.cs`
- Create: `DialogEditor.ViewModels/ViewModels/ConversationChangeViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class DiffViewModelApplyTests
{
    [Fact]
    public void ConversationGroup_TogglingAll_SelectsEveryNode()
    {
        var change = new ConversationChange("c", added: [1, 2], removed: [], modified: [3]);
        var group  = new ConversationChangeViewModel(change);

        group.IsAllSelected = true;

        Assert.All(group.Nodes, n => Assert.True(n.IsSelected));
    }

    [Fact]
    public void ConversationGroup_IsAllSelected_IsNull_WhenPartiallySelected()
    {
        var change = new ConversationChange("c", added: [1, 2], removed: [], modified: []);
        var group  = new ConversationChangeViewModel(change);

        group.Nodes[0].IsSelected = true;   // one of two

        Assert.Null(group.IsAllSelected);   // tri-state indeterminate
    }

    [Fact]
    public void ConversationGroup_SelectedNodeIds_ReflectsChecked()
    {
        var change = new ConversationChange("c", added: [1, 2], removed: [], modified: []);
        var group  = new ConversationChangeViewModel(change);

        group.Nodes[0].IsSelected = true;

        Assert.Equal([1], group.SelectedNodeIds);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter "FullyQualifiedName~DiffViewModelApplyTests"`. Expected: FAIL — VMs don't exist.

- [ ] **Step 3: Write the implementations**

`NodeChangeViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.ViewModels;

/// One changed dialogue line in the selection tree. Kind drives the +/~/− glyph
/// and colour; IsSelected drives whether it is brought in.
public partial class NodeChangeViewModel : ObservableObject
{
    public int        NodeId { get; }
    public DiffStatus Kind   { get; }

    [ObservableProperty] private bool _isSelected;

    public event Action? SelectionChanged;

    public NodeChangeViewModel(int nodeId, DiffStatus kind)
    {
        NodeId = nodeId;
        Kind   = kind;
    }

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();
}
```

`ConversationChangeViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Patch.Diff;

namespace DialogEditor.ViewModels;

/// Expandable group of changed lines for one conversation, with a tri-state
/// "select all" checkbox (true / false / null = indeterminate).
public partial class ConversationChangeViewModel : ObservableObject
{
    public string Name { get; }
    public ObservableCollection<NodeChangeViewModel> Nodes { get; } = [];

    [ObservableProperty] private bool _isExpanded;

    private bool _suppressRollDown;

    public event Action? SelectionChanged;

    public ConversationChangeViewModel(ConversationChange change)
    {
        Name = change.Name;
        foreach (var id in change.Added)    Add(id, DiffStatus.Added);
        foreach (var id in change.Modified) Add(id, DiffStatus.Changed);
        foreach (var id in change.Removed)  Add(id, DiffStatus.Removed);
    }

    private void Add(int id, DiffStatus kind)
    {
        var node = new NodeChangeViewModel(id, kind);
        node.SelectionChanged += OnNodeSelectionChanged;
        Nodes.Add(node);
    }

    public IReadOnlyList<int> SelectedNodeIds =>
        Nodes.Where(n => n.IsSelected).Select(n => n.NodeId).ToList();

    // null = indeterminate (some but not all selected).
    public bool? IsAllSelected
    {
        get
        {
            var selected = Nodes.Count(n => n.IsSelected);
            if (selected == 0)           return false;
            if (selected == Nodes.Count) return true;
            return null;
        }
        set
        {
            if (value is null) return;             // ignore the indeterminate write
            _suppressRollDown = true;
            foreach (var n in Nodes) n.IsSelected = value.Value;
            _suppressRollDown = false;
            OnPropertyChanged(nameof(IsAllSelected));
            SelectionChanged?.Invoke();
        }
    }

    private void OnNodeSelectionChanged()
    {
        if (_suppressRollDown) return;
        OnPropertyChanged(nameof(IsAllSelected));
        SelectionChanged?.Invoke();
    }
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test --filter "FullyQualifiedName~DiffViewModelApplyTests"`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/NodeChangeViewModel.cs DialogEditor.ViewModels/ViewModels/ConversationChangeViewModel.cs DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs
git commit -m "feat: checkable selection tree VMs for selective apply

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: `DiffViewModel` — build the tree, `CanApply`, target/source detection

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`
- Create: `DialogEditor.ViewModels/ViewModels/CanvasMode.cs`
- Test: `DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs` (add cases)

Use the existing `DiffViewModelTests.cs` for the fake `IGitRunner` / construction pattern (copy its setup helper into the new test file or reuse a shared fixture).

- [ ] **Step 1: Add `CanvasMode`**

`CanvasMode.cs`:
```csharp
namespace DialogEditor.ViewModels;

/// Which reconstruction the diff canvas shows.
public enum CanvasMode { Changes, AppliedPreview }
```

- [ ] **Step 2: Write the failing tests**

```csharp
    [Fact]
    public void CanApply_False_WhenNeitherEndpointIsWorkingCopy()
    {
        var vm = DiffTestFixture.WithRefVsRef();   // see helper note below
        vm.SelectFirstConversationAllNodes();
        Assert.False(vm.CanApply);
    }

    [Fact]
    public void CanApply_False_WhenNothingSelected()
    {
        var vm = DiffTestFixture.WithWorkingVsRef();
        Assert.False(vm.CanApply);
    }

    [Fact]
    public void CanApply_True_WhenWorkingCopyIsAnEndpoint_AndNodesSelected()
    {
        var vm = DiffTestFixture.WithWorkingVsRef();
        vm.SelectFirstConversationAllNodes();
        Assert.True(vm.CanApply);
    }
```

> **Helper note:** add a `DiffTestFixture` static helper in the test file that builds a `DiffViewModel` over a fake `IGitRunner` returning two known project versions (one as working copy via a temp file, one as a ref), mirroring the construction already exercised in `DiffViewModelTests.cs`. `SelectFirstConversationAllNodes()` is an extension that sets `Groups[0].IsAllSelected = true`. Reuse the existing fake runner from `DiffViewModelTests` rather than writing a new one.

- [ ] **Step 3: Modify `DiffViewModel`**

Add fields/collection (near the existing `Changes` collection):
```csharp
public ObservableCollection<ConversationChangeViewModel> Groups { get; } = [];

[ObservableProperty] private CanvasMode _canvasMode = CanvasMode.Changes;

// True when exactly one endpoint is the working copy (the writable target).
private bool WorkingCopyIsEndpoint =>
    (LeftEndpoint?.Endpoint is DiffEndpoint.WorkingCopy) ^
    (RightEndpoint?.Endpoint is DiffEndpoint.WorkingCopy);

// The writable side and the side we pull from.
private DialogProject? TargetProject =>
    LeftEndpoint?.Endpoint is DiffEndpoint.WorkingCopy ? _leftProject : _rightProject;
private DialogProject? SourceProject =>
    LeftEndpoint?.Endpoint is DiffEndpoint.WorkingCopy ? _rightProject : _leftProject;

public bool CanApply =>
    WorkingCopyIsEndpoint && TargetProject is not null && SourceProject is not null
    && Groups.Any(g => g.SelectedNodeIds.Count > 0);
```

In `Recompute()`, after populating `Changes`, build the parallel `Groups` tree and wire selection to re-raise `CanApply` (and refresh the preview):
```csharp
Groups.Clear();
foreach (var change in results)
{
    var group = new ConversationChangeViewModel(change);
    group.SelectionChanged += OnSelectionChanged;
    Groups.Add(group);
}
OnPropertyChanged(nameof(CanApply));
```

Add the handler and command-refresh:
```csharp
private void OnSelectionChanged()
{
    OnPropertyChanged(nameof(CanApply));
    ApplyCommand.NotifyCanExecuteChanged();
    if (CanvasMode == CanvasMode.AppliedPreview) BuildDiffCanvas();
}

partial void OnCanvasModeChanged(CanvasMode value) => BuildDiffCanvas();
```

Also raise `CanApply` when endpoints change — at the end of `Recompute()` it is already re-evaluated; additionally notify in `OnLeftEndpointChanged`/`OnRightEndpointChanged` is unnecessary because `Recompute()` runs there.

- [ ] **Step 4: Run to verify pass** — `dotnet test --filter "FullyQualifiedName~DiffViewModelApplyTests"`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/DiffViewModel.cs DialogEditor.ViewModels/ViewModels/CanvasMode.cs DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs
git commit -m "feat: DiffViewModel selection tree + CanApply gating

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: `DiffViewModel.ApplyCommand` + `CommitApply` + dangling warning

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs` (add cases)

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void Apply_RaisesCommitApply_WithSelectedNodesBroughtIn()
    {
        var vm = DiffTestFixture.WithWorkingVsRef();   // ref has an extra node 9 in conv "c"
        vm.SelectNode("c", 9);
        DialogProject? committed = null;
        vm.CommitApply = p => committed = p;

        vm.ApplyCommand.Execute(null);

        Assert.NotNull(committed);
        Assert.Contains(committed!.Patches["c"].AddedNodes, n => n.NodeId == 9);
    }

    [Fact]
    public void Apply_PopulatesDanglingWarning_WhenSelectionLeavesADanglingLink()
    {
        var vm = DiffTestFixture.WithDanglingScenario();  // bring in a node linking to a deleted node
        vm.SelectAll();
        vm.CommitApply = _ => { };

        vm.ApplyCommand.Execute(null);

        Assert.NotEmpty(vm.DanglingLinks);
    }
```

> Add `SelectNode(conv, id)` / `SelectAll()` test helpers that flip the matching `NodeChangeViewModel.IsSelected`. Extend `DiffTestFixture` with `WithDanglingScenario()` building a ref whose added node links to a node the same patch deletes.

- [ ] **Step 2: Run to verify failure** — Expected: FAIL — `CommitApply` / `ApplyCommand` / `DanglingLinks` don't exist.

- [ ] **Step 3: Modify `DiffViewModel`**

```csharp
using CommunityToolkit.Mvvm.Input;

// ...

/// Raised when the user brings in changes; the host persists the new project.
public Action<DialogProject>? CommitApply { get; set; }

public ObservableCollection<DanglingLink> DanglingLinks { get; } = [];

[RelayCommand(CanExecute = nameof(CanApply))]
private void Apply()
{
    if (TargetProject is null || SourceProject is null) return;

    var selection = Groups
        .SelectMany(g => g.SelectedNodeIds.Select(id => new NodeSelection(g.Name, id)))
        .ToList();
    if (selection.Count == 0) return;

    DialogProject result;
    try
    {
        result = NodeApplyBuilder.Apply(TargetProject, SourceProject, selection);
    }
    catch (Exception ex)
    {
        AppLog.Error("DiffViewModel: bring-in failed", ex);
        StatusText = Loc.Format("Status_BringInError", ex.Message);
        return;
    }

    DanglingLinks.Clear();
    foreach (var d in NodeLinkAnalyzer.Analyze(result))
        DanglingLinks.Add(d);

    CommitApply?.Invoke(result);
    StatusText = Loc.Format("Status_BroughtIn", selection.Count);
}
```

(Replace `[ObservableProperty] private string _statusText` usages remain as-is; `AppLog`/`Loc` are already imported.)

- [ ] **Step 4: Run to verify pass** — `dotnet test --filter "FullyQualifiedName~DiffViewModelApplyTests"`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/DiffViewModel.cs DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs
git commit -m "feat: DiffViewModel ApplyCommand + dangling-link warning

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

# Stage 4 — Applied preview

### Task 7: `BuildDiffCanvas` — selection-aware applied-preview mode

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs` (add case)

The preview reconstructs the selected conversation from `NodeApplyBuilder.Apply(TargetProject, SourceProject, selectionForThisConv)` and tints by `diff(workingCopy, projectedResult)`. Reuse the existing `ReconstructConversation` + `ConversationViewModel.Load` path; compute tint sets with `ProjectDiff.Diff` between the target and the projected result for the selected conversation.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void AppliedPreview_TintsBroughtInNode_AsAdded()
    {
        var vm = DiffTestFixture.WithWorkingVsRefAndProvider(); // provider available
        vm.SelectNode("c", 9);
        vm.Selected = vm.Changes.First(c => c.Name == "c");
        vm.CanvasMode = CanvasMode.AppliedPreview;

        var node = vm.DiffCanvas!.Nodes.First(n => n.NodeId == 9);
        Assert.Equal(DiffStatus.Added, node.DiffStatus);
    }
```

> `WithWorkingVsRefAndProvider()` supplies a fake `IGameDataProvider` (reuse the existing fake provider used by `DiffViewModelTests` canvas tests) so reconstruction runs.

- [ ] **Step 2: Run to verify failure** — Expected: FAIL — preview branch not implemented (node 9 absent or untinted).

- [ ] **Step 3: Modify `BuildDiffCanvas`**

At the top of `BuildDiffCanvas`, after the `Selected`/`_provider` null guards, branch on mode:
```csharp
if (CanvasMode == CanvasMode.AppliedPreview && WorkingCopyIsEndpoint
    && TargetProject is not null && SourceProject is not null)
{
    BuildAppliedPreviewCanvas();
    return;
}
```

Add the method (mirrors the existing canvas build, but reconstructs from the projected project and tints by target→projected diff):
```csharp
private void BuildAppliedPreviewCanvas()
{
    try
    {
        var name = Selected!.Name;
        var selection = Groups
            .Where(g => g.Name == name)
            .SelectMany(g => g.SelectedNodeIds.Select(id => new NodeSelection(name, id)))
            .ToList();

        var projected = NodeApplyBuilder.Apply(TargetProject!, SourceProject!, selection);

        // What changes in the working copy as a result (target → projected).
        var change = ProjectDiff.Diff(TargetProject!, projected)
            .FirstOrDefault(c => c.Name == name);

        Conversation conv = ReconstructConversation(name, projected, _provider!);
        var vm = new ConversationViewModel(_dispatcher);
        vm.Load(conv);
        vm.IsEditable = false;

        if (change is not null)
        {
            var addedSet    = change.Added.ToHashSet();
            var changedSet  = change.Modified.ToHashSet();
            var removedSet  = change.Removed.ToHashSet();
            foreach (var node in vm.Nodes)
            {
                if (addedSet.Contains(node.NodeId))        node.DiffStatus = DiffStatus.Added;
                else if (changedSet.Contains(node.NodeId)) node.DiffStatus = DiffStatus.Changed;
                else if (removedSet.Contains(node.NodeId)) node.DiffStatus = DiffStatus.Removed;
            }
        }

        DiffCanvas = vm;
        CanvasHint = "";
    }
    catch (Exception ex)
    {
        AppLog.Warn($"DiffViewModel: applied-preview build failed for '{Selected?.Name}': {ex.Message}");
        DiffCanvas = null;
        CanvasHint = ex.Message;
    }
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test --filter "FullyQualifiedName~DiffViewModelApplyTests"`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/DiffViewModel.cs DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs
git commit -m "feat: selection-aware applied-preview canvas mode

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

# Stage 5 — Editor integration

### Task 8: `MainWindowViewModel.ApplyFromDiff` + save-guard + `UndoApplyCommand`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelApplyTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Patch;
using DialogEditor.ViewModels;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelApplyTests
{
    // Use the existing MainWindowViewModel test setup helper (see MainWindowViewModelTests.cs)
    // to get a vm with a loaded project + temp project path; named MakeLoadedVm() below.

    [Fact]
    public async Task ApplyFromDiff_Dirty_AbortsWhenSaveDeclined()
    {
        var vm = MakeLoadedVm(out var path);
        vm.MarkModifiedForTest();                  // sets IsModified = true
        vm.ConfirmSaveBeforeApply = () => Task.FromResult(false); // user cancels
        var applied = SomeProjectWithExtraNode();

        await vm.ApplyFromDiff(applied);

        Assert.NotEqual(applied.Name, vm.CurrentProjectForTest.Name); // not applied
    }

    [Fact]
    public async Task ApplyFromDiff_WritesAndClearsDirty()
    {
        var vm = MakeLoadedVm(out var path);
        var applied = SomeProjectWithExtraNode();

        await vm.ApplyFromDiff(applied);

        Assert.False(vm.IsModified);                          // saved, clean
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task UndoApply_RestoresPreApplyProject()
    {
        var vm = MakeLoadedVm(out _);
        var before = vm.CurrentProjectForTest;
        await vm.ApplyFromDiff(SomeProjectWithExtraNode());

        vm.UndoApplyCommand.Execute(null);

        Assert.Equal(before.Patches.Count, vm.CurrentProjectForTest.Patches.Count);
    }
}
```

> If `MainWindowViewModel` has no test seam for the current project / marking modified, add minimal internal test hooks: `internal DialogProject? CurrentProjectForTest => _project;` and `internal void MarkModifiedForTest() => IsModified = true;` guarded by `[assembly: InternalsVisibleTo("DialogEditor.Tests")]` (check whether that attribute already exists — the existing VM tests imply a seam; reuse it).

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter "FullyQualifiedName~MainWindowViewModelApplyTests"`. Expected: FAIL.

- [ ] **Step 3: Modify `MainWindowViewModel`**

Add near the other host callbacks (e.g. by `RequestConflictResolution`):
```csharp
/// Set by the UI: asks the user to save the current copy before bringing in
/// changes. Returns true to proceed (after saving), false to abort.
public Func<Task<bool>>? ConfirmSaveBeforeApply { get; set; }

private DialogProject? _preApplyProject;
```

Add the apply method and undo command:
```csharp
/// Brings an applied project (from the compare window) into the live editor:
/// guards on unsaved changes, snapshots the prior state, swaps + saves.
public async Task ApplyFromDiff(DialogProject applied)
{
    if (_projectPath is null) return;

    if (IsModified)
    {
        if (ConfirmSaveBeforeApply is null) return;
        var proceed = await ConfirmSaveBeforeApply();
        if (!proceed) return;
        SaveProject();                 // flush current edits to disk first
    }

    _preApplyProject = _project;       // the just-saved, on-disk state
    SetProject(applied);
    SaveProject();                     // disk + memory in sync, not dirty
    UndoApplyCommand.NotifyCanExecuteChanged();
    StatusText = Loc.Format("Status_BroughtInApplied", applied.Name);
}

[RelayCommand(CanExecute = nameof(CanUndoApply))]
private void UndoApply()
{
    if (_preApplyProject is null) return;
    SetProject(_preApplyProject);
    SaveProject();
    _preApplyProject = null;
    UndoApplyCommand.NotifyCanExecuteChanged();
}

private bool CanUndoApply() => _preApplyProject is not null;
```

> Note: `SaveProject()` folds open-canvas edits into the patch first; after `SetProject(applied)` there is no current conversation file change pending for the applied conversations, so it just serialises `applied`. Confirm `SaveProject` is reachable (it is a private `[RelayCommand]`; call it directly as the existing code does at `:672`).

- [ ] **Step 4: Run to verify pass** — `dotnet test --filter "FullyQualifiedName~MainWindowViewModelApplyTests"`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelApplyTests.cs
git commit -m "feat: MainWindowViewModel.ApplyFromDiff with save-guard and single-step undo

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

# Stage 6 — UI

### Task 9: `DiffWindow.axaml` — checkable tree, toggle, colour-key strip, apply bar

**Files:**
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/Views/DiffWindowTests.cs` (extend the existing smoke test to assert the window still constructs)

This task is XAML wiring of already-tested VM state; verification is the headless smoke test plus manual check. Strings are placeholders here only in the sense of keys — define real English text in `Strings.axaml` (jargon-free wording is finalised in Task 12).

- [ ] **Step 1: Add string keys to `Strings.axaml`** (English values; revisited in Task 12)

```xml
<x:String x:Key="Diff_BringInButton">Bring in</x:String>
<x:String x:Key="Diff_UndoBringIn">Undo bring-in</x:String>
<x:String x:Key="Diff_ViewChanges">Changes</x:String>
<x:String x:Key="Diff_ViewPreview">Applied preview</x:String>
<x:String x:Key="Diff_Hint">Tick the changes you want to bring into your copy, then Bring in.</x:String>
<x:String x:Key="Diff_HelpButton">?</x:String>
<x:String x:Key="Diff_LegendAdded">Added</x:String>
<x:String x:Key="Diff_LegendChanged">Changed</x:String>
<x:String x:Key="Diff_LegendRemoved">Removed</x:String>
<x:String x:Key="ToolTip_Diff_BringIn">Brings the ticked changes into your copy. Your copy must be one of the two versions being compared; the changes are written into it and saved straight away.</x:String>
<x:String x:Key="ToolTip_Diff_BringInDisabled">To bring in changes, one of the two versions on screen must be your own copy, and your copy must be saved first.</x:String>
<x:String x:Key="ToolTip_Diff_UndoBringIn">Reverses the last set of changes you brought in, restoring your copy to how it was just before.</x:String>
<x:String x:Key="ToolTip_Diff_ViewToggle">Switch between seeing everything that differs between the two versions, and a preview of what your copy will look like after you bring in the ticked changes.</x:String>
<x:String x:Key="ToolTip_Diff_Help">Open a short guide explaining how comparing versions and bringing in changes works.</x:String>
```

- [ ] **Step 2: Replace the changed-list `ListBox.ItemTemplate`** with an expandable, checkable tree. Replace the `DataTemplate` body (lines ~96–129 of `DiffWindow.axaml`) so each conversation row has a tri-state checkbox + expander, and each child line a checkbox with the +/~/− glyph:

```xml
<ListBox.ItemTemplate>
    <DataTemplate DataType="{x:Type vm:ConversationChangeViewModel}">
        <StackPanel Orientation="Vertical" Margin="2,2">
            <StackPanel Orientation="Horizontal">
                <CheckBox IsChecked="{Binding IsAllSelected, Mode=TwoWay}"
                          IsThreeState="True"
                          ToolTip.Tip="{StaticResource ToolTip_Diff_BringIn}"/>
                <ToggleButton Content="{Binding Name}"
                              IsChecked="{Binding IsExpanded, Mode=TwoWay}"
                              Background="Transparent" Foreground="#ddd" BorderThickness="0"/>
            </StackPanel>
            <ItemsControl ItemsSource="{Binding Nodes}"
                          IsVisible="{Binding IsExpanded}" Margin="20,0,0,0">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type vm:NodeChangeViewModel}">
                        <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}"
                                  ToolTip.Tip="{StaticResource ToolTip_Diff_BringIn}">
                            <TextBlock Text="{Binding NodeId, StringFormat='Line {0}'}"
                                       Foreground="#ccc" FontSize="11"/>
                        </CheckBox>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </DataTemplate>
</ListBox.ItemTemplate>
```

Bind the `ListBox` to `Groups` instead of `Changes`:
```xml
<ListBox x:Name="ChangedList" ItemsSource="{Binding Groups}" ... />
```
Keep `Selected` driven by the canvas: add a small handler so selecting a group sets `Selected` to the matching `ConversationChange` (or change `OnSelectedChanged` to accept the group). Simplest: bind the `ToggleButton`'s click to also set `Selected`; or set `DiffViewModel.Selected` in `OnSelectionChanged` when a group is first interacted with. Implement by adding `SelectedGroup` to the VM that mirrors into `Selected` by name.

- [ ] **Step 3: Add the canvas header** (segmented toggle + colour-key strip + `?`), above the `<Panel Grid.Column="2">` canvas, inside a new top row of that panel's grid:

```xml
<StackPanel Orientation="Horizontal" DockPanel.Dock="Top" Margin="0,0,0,6" Spacing="8">
    <RadioButton GroupName="CanvasMode" Content="{StaticResource Diff_ViewChanges}"
                 IsChecked="{Binding CanvasMode, Converter={StaticResource EnumToBool}, ConverterParameter=Changes}"
                 ToolTip.Tip="{StaticResource ToolTip_Diff_ViewToggle}"/>
    <RadioButton GroupName="CanvasMode" Content="{StaticResource Diff_ViewPreview}"
                 IsChecked="{Binding CanvasMode, Converter={StaticResource EnumToBool}, ConverterParameter=AppliedPreview}"
                 ToolTip.Tip="{StaticResource ToolTip_Diff_ViewToggle}"/>
    <Border Width="10" Height="10" Background="#7dcea0"/><TextBlock Text="{StaticResource Diff_LegendAdded}" Foreground="#aaa" FontSize="10" VerticalAlignment="Center"/>
    <Border Width="10" Height="10" Background="#f0ad4e"/><TextBlock Text="{StaticResource Diff_LegendChanged}" Foreground="#aaa" FontSize="10" VerticalAlignment="Center"/>
    <Border Width="10" Height="10" Background="#e74c3c"/><TextBlock Text="{StaticResource Diff_LegendRemoved}" Foreground="#aaa" FontSize="10" VerticalAlignment="Center"/>
    <Button Content="{StaticResource Diff_HelpButton}" Click="Help_Click"
            ToolTip.Tip="{StaticResource ToolTip_Diff_Help}"/>
</StackPanel>
```

> Add an `EnumToBool` converter if none exists (`DialogEditor.Avalonia/Converters/EnumToBoolConverter.cs`): `Convert` returns `value?.ToString() == parameter as string`; `ConvertBack` returns `Enum.Parse(targetType, parameter)` when `true` else `BindingOperations.DoNothing`. Register it in `App.axaml` resources next to the other converters. (Check first — there may already be one.)

- [ ] **Step 4: Add the apply bar** above the existing bottom status bar (a second `DockPanel.Dock="Bottom"` border):

```xml
<Border DockPanel.Dock="Bottom" Background="#202020" Padding="8,6" Margin="0,6,0,0">
    <DockPanel>
        <Button DockPanel.Dock="Right" Content="{StaticResource Diff_BringInButton}"
                Command="{Binding ApplyCommand}"
                ToolTip.Tip="{StaticResource ToolTip_Diff_BringIn}"/>
        <Button DockPanel.Dock="Right" Content="{StaticResource Diff_UndoBringIn}"
                Click="UndoBringIn_Click" Margin="0,0,8,0"
                ToolTip.Tip="{StaticResource ToolTip_Diff_UndoBringIn}"/>
        <TextBlock Text="{StaticResource Diff_Hint}" Foreground="#888"
                   FontSize="11" VerticalAlignment="Center"/>
    </DockPanel>
</Border>
```

- [ ] **Step 5: Run the headless smoke test** — `dotnet test --filter "FullyQualifiedName~DiffWindowTests"`. Expected: PASS (window constructs with the new template). Fix any XAML binding/compile errors surfaced.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Views/DiffWindow.axaml DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/Converters/EnumToBoolConverter.cs DialogEditor.Avalonia/App.axaml DialogEditor.Tests/Views/DiffWindowTests.cs
git commit -m "feat: DiffWindow checkable tree, view toggle, colour key, apply bar

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: `DiffHelpWindow` (legend + prose) + wiring the `?` and Undo buttons

**Files:**
- Create: `DialogEditor.Avalonia/Views/DiffHelpWindow.axaml` + `.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml.cs` (handlers)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (help prose)
- Test: `DialogEditor.Tests/Views/DiffHelpWindowTests.cs`

- [ ] **Step 1: Write the failing headless test** (mirror the existing `LegendWindow` test)

```csharp
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using Xunit;

namespace DialogEditor.Tests.Views;

public class DiffHelpWindowTests
{
    [AvaloniaFact]
    public void Constructs_AndShows()
    {
        var window = new DiffHelpWindow();
        window.Show();
        Assert.True(window.IsVisible);
        window.Close();
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter "FullyQualifiedName~DiffHelpWindowTests"`. Expected: FAIL — type doesn't exist.

- [ ] **Step 3: Add help prose strings to `Strings.axaml`**

```xml
<x:String x:Key="DiffHelp_Title">Comparing versions — help</x:String>
<x:String x:Key="DiffHelp_ColourKeyHeader">What the colours mean</x:String>
<x:String x:Key="DiffHelp_Added">Green — a line that was added.</x:String>
<x:String x:Key="DiffHelp_Changed">Orange — a line that was changed.</x:String>
<x:String x:Key="DiffHelp_Removed">Red — a line that was removed.</x:String>
<x:String x:Key="DiffHelp_CompareHeader">Comparing two versions</x:String>
<x:String x:Key="DiffHelp_CompareBody">Pick two versions of your dialogue at the top. The list shows every conversation that differs between them; click one to see it.</x:String>
<x:String x:Key="DiffHelp_ViewsHeader">The two views</x:String>
<x:String x:Key="DiffHelp_ViewsBody">"Changes" shows everything different between the two versions. "Applied preview" shows what your copy will look like after you bring in the lines you have ticked.</x:String>
<x:String x:Key="DiffHelp_BringInHeader">Bringing in changes</x:String>
<x:String x:Key="DiffHelp_BringInBody">Tick the lines you want, then press "Bring in". The changes are written into your own copy and saved. Your copy must be one of the two versions on screen.</x:String>
<x:String x:Key="DiffHelp_UndoHeader">Undo</x:String>
<x:String x:Key="DiffHelp_UndoBody">"Undo bring-in" reverses the last set of changes you brought in.</x:String>
<x:String x:Key="DiffHelp_LinksHeader">A note on links</x:String>
<x:String x:Key="DiffHelp_LinksBody">If you bring in a line that points to a line you removed, you will see a warning. It is safe to continue, but that link may not lead anywhere.</x:String>
```

- [ ] **Step 4: Create `DiffHelpWindow.axaml`** (modeled on `LegendWindow.axaml`: scrollable, sectioned, app icon)

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.DiffHelpWindow"
        Title="{StaticResource DiffHelp_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="380" Height="560" CanResize="True" ShowInTaskbar="False"
        Background="#1a1a2a">
    <ScrollViewer>
        <StackPanel Margin="14,12,14,16" Spacing="6">
            <TextBlock Text="{StaticResource DiffHelp_ColourKeyHeader}" Foreground="#888" FontSize="10" FontWeight="Bold"/>
            <StackPanel Orientation="Horizontal" Spacing="8"><Border Width="12" Height="12" Background="#7dcea0"/><TextBlock Text="{StaticResource DiffHelp_Added}" Foreground="#ccc" FontSize="12"/></StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="8"><Border Width="12" Height="12" Background="#f0ad4e"/><TextBlock Text="{StaticResource DiffHelp_Changed}" Foreground="#ccc" FontSize="12"/></StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="8"><Border Width="12" Height="12" Background="#e74c3c"/><TextBlock Text="{StaticResource DiffHelp_Removed}" Foreground="#ccc" FontSize="12"/></StackPanel>
            <TextBlock Text="{StaticResource DiffHelp_CompareHeader}" Foreground="#888" FontSize="10" FontWeight="Bold" Margin="0,8,0,0"/>
            <TextBlock Text="{StaticResource DiffHelp_CompareBody}" Foreground="#bbb" FontSize="12" TextWrapping="Wrap"/>
            <TextBlock Text="{StaticResource DiffHelp_ViewsHeader}" Foreground="#888" FontSize="10" FontWeight="Bold" Margin="0,8,0,0"/>
            <TextBlock Text="{StaticResource DiffHelp_ViewsBody}" Foreground="#bbb" FontSize="12" TextWrapping="Wrap"/>
            <TextBlock Text="{StaticResource DiffHelp_BringInHeader}" Foreground="#888" FontSize="10" FontWeight="Bold" Margin="0,8,0,0"/>
            <TextBlock Text="{StaticResource DiffHelp_BringInBody}" Foreground="#bbb" FontSize="12" TextWrapping="Wrap"/>
            <TextBlock Text="{StaticResource DiffHelp_UndoHeader}" Foreground="#888" FontSize="10" FontWeight="Bold" Margin="0,8,0,0"/>
            <TextBlock Text="{StaticResource DiffHelp_UndoBody}" Foreground="#bbb" FontSize="12" TextWrapping="Wrap"/>
            <TextBlock Text="{StaticResource DiffHelp_LinksHeader}" Foreground="#888" FontSize="10" FontWeight="Bold" Margin="0,8,0,0"/>
            <TextBlock Text="{StaticResource DiffHelp_LinksBody}" Foreground="#bbb" FontSize="12" TextWrapping="Wrap"/>
        </StackPanel>
    </ScrollViewer>
</Window>
```

`DiffHelpWindow.axaml.cs`:
```csharp
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Views;

public partial class DiffHelpWindow : Window
{
    public DiffHelpWindow() => InitializeComponent();
}
```

- [ ] **Step 5: Wire the handlers in `DiffWindow.axaml.cs`**

```csharp
private DiffHelpWindow? _helpWindow;

private void Help_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
{
    if (_helpWindow is null || !_helpWindow.IsVisible)
        _helpWindow = new DiffHelpWindow();
    _helpWindow.Show();
    _helpWindow.Activate();
}

private void UndoBringIn_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
{
    // Forward to the editor's undo via the same callback the host wired (see Task 11):
    (DataContext as DialogEditor.ViewModels.DiffViewModel)?.RequestUndoApply?.Invoke();
}
```

> Add `public Action? RequestUndoApply { get; set; }` to `DiffViewModel`; the host wires it to `mainVm.UndoApplyCommand.Execute(null)` in Task 11. (Undo lives on the editor, not the diff window, because it restores the editor's project.)

- [ ] **Step 6: Run to verify pass** — `dotnet test --filter "FullyQualifiedName~DiffHelpWindowTests"`. Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Views/DiffHelpWindow.axaml DialogEditor.Avalonia/Views/DiffHelpWindow.axaml.cs DialogEditor.Avalonia/Views/DiffWindow.axaml.cs DialogEditor.ViewModels/ViewModels/DiffViewModel.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/Views/DiffHelpWindowTests.cs
git commit -m "feat: DiffHelpWindow (plain-language legend + prose) and help/undo wiring

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: Wire `CommitApply`, `ConfirmSaveBeforeApply`, and undo in `MainWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (`CompareVersions_Click`, lines ~306–314)

- [ ] **Step 1: Wire the callbacks when opening the diff window**

```csharp
private void CompareVersions_Click(object? sender, RoutedEventArgs e)
{
    var vm = (MainWindowViewModel)DataContext!;
    if (vm.ProjectPath is null) return;

    var diffVm = new DiffViewModel(new ProcessGitRunner(), new AvaloniaDispatcher(),
                                   vm.ProjectPath, vm.Provider, vm.Provider?.Language ?? "en");

    diffVm.CommitApply        = applied => _ = vm.ApplyFromDiff(applied);
    diffVm.RequestUndoApply   = () => vm.UndoApplyCommand.Execute(null);
    vm.ConfirmSaveBeforeApply = () => ShowSaveBeforeApplyDialogAsync(vm);

    new DiffWindow(diffVm).Show();
}

// Reuse the existing unsaved-changes dialog: returns true to proceed (saving),
// false to cancel. Model on ShowUnsavedChangesDialogAsync.
private async Task<bool> ShowSaveBeforeApplyDialogAsync(MainWindowViewModel vm)
{
    var dialog = new UnsavedChangesDialog();   // existing dialog type
    var result = await dialog.ShowDialog<UnsavedChangesResult>(this);
    return result == UnsavedChangesResult.Save;
}
```

> Check the actual `UnsavedChangesDialog` API used by `ShowUnsavedChangesDialogAsync` and match it (the existing method shows the same dialog). If its result enum differs, adapt the comparison. The point: return `true` only when the user chooses to save.

- [ ] **Step 2: Build and run the full suite** — `dotnet test`. Expected: PASS (no regressions). Fix any wiring/compile issues.

- [ ] **Step 3: Manual smoke (documented, not automated)** — open a project in a git repo, Compare Versions, tick a line, confirm Bring in writes + saves, Undo bring-in restores, and the `?` opens help. Note: covered by manual verification per the spec; the headless tests cover the logic.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat: wire selective-apply callbacks into the compare window

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

# Stage 7 — Approachability terminology pass

### Task 12: Jargon-free terminology pass on user-facing strings

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (and any Spec-1 diff strings that surface jargon)

This is a wording pass verified by review, not by automated tests. Apply the glossary from the spec: *endpoint→version, working copy→your copy, ref/branch/commit→a saved version, apply→bring in, node→dialogue line, undo apply→undo bring-in*.

- [ ] **Step 1: Audit the diff/apply strings** — grep the resources for the touched keys:

Run: `dotnet build` then review `Strings.axaml` for keys beginning `Diff_`, `DiffWindow_`, `DiffHelp_`, `ToolTip_Diff`. Confirm none of the *visible* values say "endpoint", "ref", "commit", "patch", "node", "working copy", or "cherry-pick".

- [ ] **Step 2: Rewrite any jargon found.** Known Spec-1 candidates to revisit (confirm exact keys in the file):
  - the two endpoint-picker labels (`DiffWindow_LeftLabel` / `DiffWindow_RightLabel`) → "Earlier version" / "Later version" or "Version A" / "Version B" with tooltips explaining you can pick your own copy or a saved version.
  - `Diff_WorkingCopy` endpoint option label → "Your copy".
  - the compare menu item / window title → "Compare versions".
  - status strings introduced this feature (`Status_BringInError`, `Status_BroughtIn`, `Status_BroughtInApplied`) → ensure they read in plain language (define them if not already): e.g. `Status_BroughtIn` = "Brought in {0} change(s) to your copy."

- [ ] **Step 3: Build to confirm no missing keys** — `dotnet build`. Expected: success. Then `dotnet test` to confirm no string-key test broke.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: jargon-free terminology pass for compare/bring-in UI

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review (completed during planning)

- **Spec coverage:** engine (Tasks 1–2), dangling links (Task 3), per-node tri-state selection + CanApply (Tasks 4–5), ApplyCommand + warning (Task 6), applied preview (Task 7), save-guard + write-in-sync + single-step undo (Task 8), UI tree/toggle/colour-key/apply bar (Task 9), Help window + cues (Task 10), host wiring (Task 11), terminology pass (Task 12). First-run intro is explicitly deferred (spec + `Gaps.md`).
- **Direction constraint:** `CanApply`/`WorkingCopyIsEndpoint` enforce "apply only when the working copy is one endpoint"; the disabled tooltip explains it.
- **Type consistency:** `NodeSelection(ConversationName, NodeId)`, `DanglingLink(Conversation, FromNode, ToNode)`, `CanvasMode { Changes, AppliedPreview }`, `CommitApply: Action<DialogProject>`, `RequestUndoApply: Action`, `ConfirmSaveBeforeApply: Func<Task<bool>>`, `ApplyFromDiff(DialogProject)` used consistently across tasks.
- **Open verification points flagged for the implementer (not placeholders — confirm against the live code):** the `InternalsVisibleTo` test seam in Task 8; whether an `EnumToBool` converter already exists (Task 9 Step 3); the exact `UnsavedChangesDialog` result API (Task 11).
