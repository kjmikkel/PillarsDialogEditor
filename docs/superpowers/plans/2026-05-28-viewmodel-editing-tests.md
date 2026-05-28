# ViewModel Editing Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the missing tests for `AddConnectedNode`, redo operations, `CanUndo`/`CanRedo` state transitions, and `RestoreLayout` in `ConversationViewModelEditTests.cs`.

**Architecture:** Pure additions to one existing test file — no production code changes. All tests use the existing `MakeVm()` / `MakeNode(id)` helpers and the `StubDispatcher` / `StubStringProvider` already in place. Each task adds one cohesive group of tests and commits.

**Tech Stack:** xUnit, `DialogEditor.ViewModels`, `DialogEditor.Core.Models`, existing test helpers in `DialogEditor.Tests/Helpers/`.

---

## File to modify

- `DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs` — all tests go here

---

## Codebase orientation

Before starting, read the following so you understand what you're testing:

- **`ConversationViewModel.AddConnectedNode`** (`DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`, lines ~330–350): pushes *two separate commands* onto the undo stack — first `AddNodeCommand`, then `AddConnectionCommand`. This means one `Undo()` call removes only the connection; a second removes the node.
- **`ConversationViewModel.RestoreLayout`** (same file, ~376–381): iterates `Nodes`, sets `node.Location` only for IDs present in the dictionary; absent IDs are silently skipped.
- **`LayoutPoint`** (`DialogEditor.Core/Models/LayoutPoint.cs`): `readonly record struct LayoutPoint(double X, double Y)` — value equality works with `Assert.Equal`.
- **`ConnectionViewModel.Source`** / `.Target`**: `ConnectorViewModel` properties on `ConnectionViewModel`.
- Existing helpers in the test class:
  ```csharp
  private static ConversationViewModel MakeVm() =>
      new(new StubDispatcher());

  private static NodeViewModel MakeNode(int id) =>
      new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
          [], [], "Conversation", "None"), null);
  ```
- `ConversationNode` constructor parameter order: `(int NodeId, bool IsPlayerChoice, SpeakerCategory, string speakerGuid, string listenerGuid, IReadOnlyList<NodeLink> links, IReadOnlyList<ConditionSet> conditions, IReadOnlyList<ScriptSet> scripts, string displayType, string persistence)`.

---

## Task 1: `AddConnectedNode` — structure tests

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs`

- [ ] **Step 1: Add the four structure tests**

Append after the existing `SetNodeComment_NewEntry_AddsToDict` test, inside the `ConversationViewModelEditTests` class:

```csharp
// ── AddConnectedNode ──────────────────────────────────────────────────

[Fact]
public void AddConnectedNode_AppearsInNodesAndConnections()
{
    var vm     = MakeVm();
    var parent = MakeNode(1);
    vm.AddNode(parent, new LayoutPoint(0, 0));
    vm.AddConnectedNode(parent, new LayoutPoint(250, 0));
    Assert.Equal(2, vm.Nodes.Count);
    Assert.Single(vm.Connections);
    Assert.Equal(parent.Output, vm.Connections[0].Source);
}

[Fact]
public void AddConnectedNode_SetsSelectedNodeToChild()
{
    var vm     = MakeVm();
    var parent = MakeNode(1);
    vm.AddNode(parent, new LayoutPoint(0, 0));
    vm.AddConnectedNode(parent, new LayoutPoint(250, 0));
    Assert.NotNull(vm.SelectedNode);
    Assert.NotEqual(parent, vm.SelectedNode);
}

[Fact]
public void AddConnectedNode_InheritsParentProperties()
{
    var vm     = MakeVm();
    var parent = new NodeViewModel(
        new ConversationNode(1, false, SpeakerCategory.Narrator,
            "spk-1", "lst-1", [], [], [], "Bark", "ShowOnce"),
        null);
    vm.AddNode(parent, new LayoutPoint(0, 0));
    vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

    var child = vm.Nodes.Single(n => n.NodeId != 1);
    Assert.Equal(SpeakerCategory.Narrator, child.SpeakerCategory);
    Assert.Equal("spk-1",    child.SpeakerGuid);
    Assert.Equal("lst-1",    child.ListenerGuid);
    Assert.Equal("Bark",     child.DisplayType);
    Assert.Equal("ShowOnce", child.Persistence);
}

[Fact]
public void AddConnectedNode_AllocatesDistinctNodeId()
{
    var vm     = MakeVm();
    var parent = MakeNode(1);
    vm.AddNode(parent, new LayoutPoint(0, 0));
    vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

    var child = vm.Nodes.Single(n => n.NodeId != 1);
    Assert.NotEqual(1, child.NodeId);
}
```

- [ ] **Step 2: Run the new tests to verify they pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AddConnectedNode" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 4`

These tests exercise existing correct behaviour — they should be green immediately. If any fail, read the failure message carefully; it means the production code does not do what the design expected.

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs
git commit -m "test: AddConnectedNode structure — nodes, connection, selection, inherited props"
```

---

## Task 2: `AddConnectedNode` — undo/redo semantics

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs`

- [ ] **Step 1: Add the four undo/redo tests**

Append after the Task 1 tests:

```csharp
[Fact]
public void UndoAddConnectedNode_FirstUndoRemovesConnectionOnly()
{
    var vm     = MakeVm();
    var parent = MakeNode(1);
    vm.AddNode(parent, new LayoutPoint(0, 0));
    vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

    vm.Undo(); // undoes the AddConnection command
    Assert.Equal(2, vm.Nodes.Count);
    Assert.Empty(vm.Connections);
}

[Fact]
public void UndoAddConnectedNode_SecondUndoRemovesChildNode()
{
    var vm     = MakeVm();
    var parent = MakeNode(1);
    vm.AddNode(parent, new LayoutPoint(0, 0));
    vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

    vm.Undo(); // undoes AddConnection
    vm.Undo(); // undoes AddNode (child)
    Assert.Single(vm.Nodes); // only the parent remains
    Assert.Empty(vm.Connections);
}

[Fact]
public void RedoAddConnectedNode_FirstRedoRestoresChildNodeOnly()
{
    var vm     = MakeVm();
    var parent = MakeNode(1);
    vm.AddNode(parent, new LayoutPoint(0, 0));
    vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

    vm.Undo(); // undo connection
    vm.Undo(); // undo child node
    vm.Redo(); // redo AddNode — child is back, no connection yet

    Assert.Equal(2, vm.Nodes.Count);
    Assert.Empty(vm.Connections);
}

[Fact]
public void RedoAddConnectedNode_SecondRedoRestoresConnection()
{
    var vm     = MakeVm();
    var parent = MakeNode(1);
    vm.AddNode(parent, new LayoutPoint(0, 0));
    vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

    vm.Undo(); // undo connection
    vm.Undo(); // undo child node
    vm.Redo(); // redo AddNode
    vm.Redo(); // redo AddConnection

    Assert.Equal(2, vm.Nodes.Count);
    Assert.Single(vm.Connections);
}
```

- [ ] **Step 2: Run the new tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~UndoAddConnectedNode|FullyQualifiedName~RedoAddConnectedNode" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs
git commit -m "test: AddConnectedNode two-command undo/redo semantics"
```

---

## Task 3: Redo for each standard operation type

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs`

- [ ] **Step 1: Add four redo tests — one per operation**

Append after the Task 2 tests:

```csharp
// ── Redo ──────────────────────────────────────────────────────────────

[Fact]
public void RedoAddNode_RestoresNode()
{
    var vm   = MakeVm();
    var node = MakeNode(1);
    vm.AddNode(node, new LayoutPoint(0, 0));
    vm.Undo();
    vm.Redo();
    Assert.Contains(node, vm.Nodes);
}

[Fact]
public void RedoDeleteNode_RemovesNodeAgain()
{
    var vm = MakeVm();
    var n1 = MakeNode(1);
    var n2 = MakeNode(2);
    vm.AddNode(n1, new LayoutPoint(0, 0));
    vm.AddNode(n2, new LayoutPoint(200, 0));
    vm.AddConnection(n1.Output, n2.Input);
    vm.DeleteNode(n1);
    vm.Undo();
    vm.Redo();
    Assert.DoesNotContain(n1, vm.Nodes);
    Assert.Empty(vm.Connections);
}

[Fact]
public void RedoAddConnection_RestoresConnection()
{
    var vm = MakeVm();
    var n1 = MakeNode(1);
    var n2 = MakeNode(2);
    vm.AddNode(n1, new LayoutPoint(0, 0));
    vm.AddNode(n2, new LayoutPoint(200, 0));
    vm.AddConnection(n1.Output, n2.Input);
    vm.Undo();
    vm.Redo();
    Assert.Single(vm.Connections);
}

[Fact]
public void RedoDeleteConnection_RemovesConnectionAgain()
{
    var vm = MakeVm();
    var n1 = MakeNode(1);
    var n2 = MakeNode(2);
    vm.AddNode(n1, new LayoutPoint(0, 0));
    vm.AddNode(n2, new LayoutPoint(200, 0));
    vm.AddConnection(n1.Output, n2.Input);
    var conn = vm.Connections.Single();
    vm.DeleteConnection(conn);
    vm.Undo();
    vm.Redo();
    Assert.Empty(vm.Connections);
}
```

- [ ] **Step 2: Run the new tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~RedoAddNode|FullyQualifiedName~RedoDeleteNode|FullyQualifiedName~RedoAddConnection|FullyQualifiedName~RedoDeleteConnection" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs
git commit -m "test: redo for AddNode, DeleteNode, AddConnection, DeleteConnection"
```

---

## Task 4: Redo-stack clearing and `CanUndo`/`CanRedo` state machine

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs`

- [ ] **Step 1: Add the two state-machine tests**

Append after the Task 3 tests:

```csharp
// ── CanUndo / CanRedo ─────────────────────────────────────────────────

[Fact]
public void NewOperationAfterUndo_ClearsRedoStack()
{
    var vm = MakeVm();
    vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));
    vm.Undo();
    vm.AddNode(MakeNode(2), new LayoutPoint(0, 0));
    Assert.False(vm.CanRedo);
}

[Fact]
public void CanUndoCanRedo_StateTransitions()
{
    var vm = MakeVm();
    Assert.False(vm.CanUndo);
    Assert.False(vm.CanRedo);

    vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));
    Assert.True(vm.CanUndo);
    Assert.False(vm.CanRedo);

    vm.Undo();
    Assert.False(vm.CanUndo);
    Assert.True(vm.CanRedo);

    vm.Redo();
    Assert.True(vm.CanUndo);
    Assert.False(vm.CanRedo);
}
```

- [ ] **Step 2: Run the new tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NewOperationAfterUndo|FullyQualifiedName~CanUndoCanRedo" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs
git commit -m "test: redo-stack cleared by new operation; CanUndo/CanRedo state transitions"
```

---

## Task 5: `RestoreLayout` and `IsModified` after undo

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs`

- [ ] **Step 1: Add the four tests**

Append after the Task 4 tests:

```csharp
// ── RestoreLayout ─────────────────────────────────────────────────────

[Fact]
public void RestoreLayout_SetsPositionsFromDictionary()
{
    var vm = MakeVm();
    var n1 = MakeNode(1);
    var n2 = MakeNode(2);
    vm.AddNode(n1, new LayoutPoint(0, 0));
    vm.AddNode(n2, new LayoutPoint(0, 0));

    vm.RestoreLayout(new Dictionary<int, LayoutPoint>
    {
        [1] = new LayoutPoint(100, 200),
        [2] = new LayoutPoint(300, 400),
    });

    Assert.Equal(new LayoutPoint(100, 200), n1.Location);
    Assert.Equal(new LayoutPoint(300, 400), n2.Location);
}

[Fact]
public void RestoreLayout_AbsentNodeIdKeepsOriginalPosition()
{
    var vm   = MakeVm();
    var node = MakeNode(1);
    vm.AddNode(node, new LayoutPoint(50, 75));

    vm.RestoreLayout(new Dictionary<int, LayoutPoint>()); // empty — node 1 is absent

    Assert.Equal(new LayoutPoint(50, 75), node.Location);
}

[Fact]
public void RestoreLayout_ExtraKeyInDictionary_DoesNotThrow()
{
    var vm = MakeVm();
    vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));

    var ex = Record.Exception(() => vm.RestoreLayout(new Dictionary<int, LayoutPoint>
    {
        [1]   = new LayoutPoint(10, 20),
        [999] = new LayoutPoint(0,  0),  // no node with id 999 in the VM
    }));

    Assert.Null(ex);
}

// ── IsModified after undo ─────────────────────────────────────────────

[Fact]
public void IsModified_RemainsAfterUndo()
{
    // IsModified is set true by every mutating operation and false only by Load().
    // Undo does not clear it — the VM has no saved-state baseline.
    var vm = MakeVm();
    vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));
    vm.Undo();
    Assert.True(vm.IsModified);
}
```

- [ ] **Step 2: Run the new tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~RestoreLayout|FullyQualifiedName~IsModified_RemainsAfterUndo" --no-build
```

Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 3: Run the full suite to confirm no regressions**

```
dotnet test DialogEditor.Tests --no-build
```

Expected: `Passed! - Failed: 0, Passed: 647` (plus the 18 new tests = 665 total)

- [ ] **Step 4: Commit**

```
git add DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs
git commit -m "test: RestoreLayout positions, absent-key safety, IsModified not cleared by undo"
```
