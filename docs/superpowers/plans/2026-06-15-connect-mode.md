# Keyboard Connect Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let keyboard users create a connection between two nodes on the conversation
canvas — the last mouse-only structural operation — via Ctrl+L / a "Connect to…" context
menu item, picking the target with the existing topological navigation keys, confirming
with Enter and cancelling with Escape.

**Architecture:** `ConversationViewModel` gains a small state machine (`IsConnecting`,
`ConnectionSource`, `BeginConnect`/`TryBeginConnect`/`TryConfirmConnection`/
`CancelConnect`, a `ConnectModeChanged` event). `NodeViewModel` gains an
`IsConnectionSource` flag that drives a dashed amber overlay + 🔗 badge (Layer 2.5,
reusing `Brush.Severity.Warning`). `ConversationView.axaml.cs` reinterprets Enter/Escape
while connecting and adds Ctrl+L. `MainWindowViewModel` turns `ConnectModeChanged` into
`StatusText` announcements for the existing live region.

**Tech Stack:** C#/.NET, Avalonia (incl. headless `[AvaloniaFact]` tests), Nodify,
CommunityToolkit.Mvvm source generators, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-15-connect-mode-design.md` (approved).

---

## Task 1: Support types — `ConnectModeChange`, `ConnectModeEventArgs`, `NodeViewModel.IsConnectionSource`

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/ConnectModeChange.cs`
- Create: `DialogEditor.ViewModels/ViewModels/ConnectModeEventArgs.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs:246-248`

These are plain data-shape additions (an enum, a record, and one more
`[ObservableProperty] private bool` alongside the existing `_isSelected`/
`_isSearchMatch` fields — same pattern, no new logic). Per CLAUDE.md, strict red/green
TDD applies to non-trivial logic; this task has none, so it's build-verified rather than
test-first. The behaviour these types enable is covered by Task 2's tests.

- [ ] **Step 1: Create the `ConnectModeChange` enum**

```csharp
namespace DialogEditor.ViewModels;

/// <summary>
/// The three transitions of keyboard "connect mode" (Gaps.md Accessibility item 4
/// follow-up — see docs/superpowers/specs/2026-06-15-connect-mode-design.md).
/// </summary>
public enum ConnectModeChange
{
    Started,
    Connected,
    Cancelled,
}
```

Save as `DialogEditor.ViewModels/ViewModels/ConnectModeChange.cs`.

- [ ] **Step 2: Create the `ConnectModeEventArgs` record**

```csharp
namespace DialogEditor.ViewModels;

/// <summary>
/// Raised by <see cref="ConversationViewModel.ConnectModeChanged"/> when keyboard
/// connect mode starts, completes with a new connection, or is cancelled.
/// <paramref name="Target"/> is non-null only for <see cref="ConnectModeChange.Connected"/>.
/// </summary>
public sealed record ConnectModeEventArgs(
    ConnectModeChange Change,
    NodeViewModel Source,
    NodeViewModel? Target);
```

Save as `DialogEditor.ViewModels/ViewModels/ConnectModeEventArgs.cs`.

- [ ] **Step 3: Add `IsConnectionSource` to `NodeViewModel`**

In `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs`, find:

```csharp
    // ── Canvas state ──────────────────────────────────────────────────────
    [ObservableProperty] private LayoutPoint _location;
    [ObservableProperty] private bool        _isSelected;
    [ObservableProperty] private bool        _isSearchMatch = true;
```

Replace with:

```csharp
    // ── Canvas state ──────────────────────────────────────────────────────
    [ObservableProperty] private LayoutPoint _location;
    [ObservableProperty] private bool        _isSelected;
    [ObservableProperty] private bool        _isSearchMatch = true;

    /// <summary>
    /// True only for the node currently acting as the source in keyboard "connect
    /// mode" (Ctrl+L). Drives the dashed-border + 🔗-badge overlay in
    /// ConversationView.axaml; see docs/superpowers/specs/2026-06-15-connect-mode-design.md.
    /// </summary>
    [ObservableProperty] private bool        _isConnectionSource;
```

- [ ] **Step 4: Build to verify the new types compile**

Run: `dotnet build DialogEditor.slnx`
Expected: `Build succeeded.` (no errors)

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/ConnectModeChange.cs DialogEditor.ViewModels/ViewModels/ConnectModeEventArgs.cs DialogEditor.ViewModels/ViewModels/NodeViewModel.cs
git commit -m "feat(connect-mode): add ConnectModeChange/ConnectModeEventArgs and NodeViewModel.IsConnectionSource"
```

---

## Task 2: `ConversationViewModel` connect-mode state machine

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/ConversationViewModelConnectModeTests.cs` (new)

This is the core of the feature: `IsConnecting`/`ConnectionSource` observable state,
`BeginConnect`/`TryBeginConnect`/`TryConfirmConnection`/`CancelConnect`, the
`ConnectModeChanged` event, a `DeleteNode` hook, and a `BeginConnectCmd` relay command
for the context menu (Task 4).

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/ConversationViewModelConnectModeTests.cs`:

```csharp
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class ConversationViewModelConnectModeTests
{
    public ConversationViewModelConnectModeTests()
    {
        Loc.Configure(new StubStringProvider());
    }

    private static ConversationViewModel MakeVm() =>
        new(new StubDispatcher()) { IsEditable = true };

    private static NodeViewModel MakeNode(int id) =>
        new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);

    [Fact]
    public void BeginConnect_SetsConnectingStateAndSelectsSource()
    {
        var vm = MakeVm();
        var node = MakeNode(1);
        vm.AddNode(node, new LayoutPoint(0, 0));

        var result = vm.BeginConnect(node);

        Assert.True(result);
        Assert.True(vm.IsConnecting);
        Assert.Same(node, vm.ConnectionSource);
        Assert.True(node.IsConnectionSource);
        Assert.Same(node, vm.SelectedNode);
    }

    [Fact]
    public void BeginConnect_RaisesStartedEvent()
    {
        var vm = MakeVm();
        var node = MakeNode(1);
        vm.AddNode(node, new LayoutPoint(0, 0));

        ConnectModeEventArgs? raised = null;
        vm.ConnectModeChanged += (_, e) => raised = e;

        vm.BeginConnect(node);

        Assert.NotNull(raised);
        Assert.Equal(ConnectModeChange.Started, raised!.Change);
        Assert.Same(node, raised.Source);
        Assert.Null(raised.Target);
    }

    [Fact]
    public void BeginConnect_NotEditable_ReturnsFalseAndDoesNotEnterConnectMode()
    {
        var vm = MakeVm();
        vm.IsEditable = false;
        var node = MakeNode(1);
        vm.Nodes.Add(node);

        var result = vm.BeginConnect(node);

        Assert.False(result);
        Assert.False(vm.IsConnecting);
        Assert.Null(vm.ConnectionSource);
    }

    [Fact]
    public void BeginConnect_AlreadyConnecting_ReturnsFalseAndKeepsOriginalSource()
    {
        var vm = MakeVm();
        var first = MakeNode(1);
        var second = MakeNode(2);
        vm.AddNode(first, new LayoutPoint(0, 0));
        vm.AddNode(second, new LayoutPoint(200, 0));
        vm.BeginConnect(first);

        var result = vm.BeginConnect(second);

        Assert.False(result);
        Assert.Same(first, vm.ConnectionSource);
        Assert.False(second.IsConnectionSource);
    }

    [Fact]
    public void TryBeginConnect_NoSelection_ReturnsFalse()
    {
        var vm = MakeVm();

        Assert.False(vm.TryBeginConnect());
        Assert.False(vm.IsConnecting);
    }

    [Fact]
    public void TryBeginConnect_DelegatesToSelectedNode()
    {
        var vm = MakeVm();
        var node = MakeNode(1);
        vm.AddNode(node, new LayoutPoint(0, 0));
        vm.SelectNode(node);

        var result = vm.TryBeginConnect();

        Assert.True(result);
        Assert.Same(node, vm.ConnectionSource);
    }

    [Fact]
    public void TryConfirmConnection_ValidTarget_CreatesConnectionAndExitsConnectMode()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        var target = MakeNode(2);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.AddNode(target, new LayoutPoint(200, 0));
        vm.BeginConnect(source);
        vm.SelectNode(target);

        var result = vm.TryConfirmConnection();

        Assert.True(result);
        Assert.False(vm.IsConnecting);
        Assert.Null(vm.ConnectionSource);
        Assert.False(source.IsConnectionSource);
        Assert.Contains(vm.Connections, c => c.Source == source.Output && c.Target == target.Input);
    }

    [Fact]
    public void TryConfirmConnection_ValidTarget_RaisesConnectedEvent()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        var target = MakeNode(2);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.AddNode(target, new LayoutPoint(200, 0));
        vm.BeginConnect(source);
        vm.SelectNode(target);

        ConnectModeEventArgs? raised = null;
        vm.ConnectModeChanged += (_, e) => raised = e;

        vm.TryConfirmConnection();

        Assert.NotNull(raised);
        Assert.Equal(ConnectModeChange.Connected, raised!.Change);
        Assert.Same(source, raised.Source);
        Assert.Same(target, raised.Target);
    }

    [Fact]
    public void TryConfirmConnection_SelfTarget_NoOpStaysConnecting()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.BeginConnect(source); // BeginConnect also selects the source node

        var raised = false;
        vm.ConnectModeChanged += (_, _) => raised = true;

        var result = vm.TryConfirmConnection();

        Assert.True(result);
        Assert.True(vm.IsConnecting);
        Assert.Same(source, vm.ConnectionSource);
        Assert.Empty(vm.Connections);
        Assert.False(raised);
    }

    [Fact]
    public void TryConfirmConnection_DuplicateTarget_NoOpStaysConnecting()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        var target = MakeNode(2);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.AddNode(target, new LayoutPoint(200, 0));
        vm.AddConnection(source.Output, target.Input);
        vm.BeginConnect(source);
        vm.SelectNode(target);

        var raised = false;
        vm.ConnectModeChanged += (_, _) => raised = true;

        var result = vm.TryConfirmConnection();

        Assert.True(result);
        Assert.True(vm.IsConnecting);
        Assert.Single(vm.Connections);
        Assert.False(raised);
    }

    [Fact]
    public void CancelConnect_ClearsStateAndRaisesCancelledEvent()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.BeginConnect(source);

        ConnectModeEventArgs? raised = null;
        vm.ConnectModeChanged += (_, e) => raised = e;

        var result = vm.CancelConnect();

        Assert.True(result);
        Assert.False(vm.IsConnecting);
        Assert.Null(vm.ConnectionSource);
        Assert.False(source.IsConnectionSource);
        Assert.NotNull(raised);
        Assert.Equal(ConnectModeChange.Cancelled, raised!.Change);
        Assert.Same(source, raised.Source);
        Assert.Null(raised.Target);
    }

    [Fact]
    public void DeleteNode_OnConnectionSource_CancelsConnectModeFirst()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.BeginConnect(source);

        vm.DeleteNode(source);

        Assert.False(vm.IsConnecting);
        Assert.Null(vm.ConnectionSource);
        Assert.DoesNotContain(source, vm.Nodes);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter ConversationViewModelConnectModeTests`
Expected: build error (or all FAIL) — `BeginConnect`, `TryBeginConnect`,
`TryConfirmConnection`, `CancelConnect`, `IsConnecting`, `ConnectionSource`,
`ConnectModeChanged` do not exist yet on `ConversationViewModel`.

- [ ] **Step 3: Add `IsConnecting`/`ConnectionSource` observable state and the event**

In `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`, find:

```csharp
    [ObservableProperty]
    private NodeViewModel? _selectedNode;
```

Replace with:

```csharp
    [ObservableProperty]
    private NodeViewModel? _selectedNode;

    // ── Keyboard "connect mode" (Gaps.md Accessibility item 4 follow-up) ──────
    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private NodeViewModel? _connectionSource;

    /// <summary>
    /// Raised when keyboard connect mode starts, completes with a new connection, or
    /// is cancelled. <see cref="MainWindowViewModel"/> turns this into a
    /// <c>StatusText</c> announcement for the existing live region.
    /// </summary>
    public event EventHandler<ConnectModeEventArgs>? ConnectModeChanged;
```

- [ ] **Step 4: Add `BeginConnect`, `TryBeginConnect`, `TryConfirmConnection`, `CancelConnect`**

In `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`, find the end of the
"Keyboard selection & navigation" section — the closing brace of `NudgeSelected`:

```csharp
    public bool NudgeSelected(double dx, double dy)
    {
        if (!IsEditable || SelectedNode is null) return false;
        SelectedNode.Location = new LayoutPoint(SelectedNode.Location.X + dx, SelectedNode.Location.Y + dy);
        return true;
    }
```

Immediately after that closing brace, add:

```csharp

    // ── Keyboard connect mode ────────────────────────────────────────────────
    /// <summary>
    /// Starts connect mode with <paramref name="node"/> as the source. The source
    /// becomes the initial target-candidate too — arrow keys then move the target
    /// candidate away from it (spec decision: entry = SelectNode(source)).
    /// No-op (returns false) if the canvas isn't editable or connect mode is already
    /// active — the user must confirm or cancel the current session first.
    /// </summary>
    public bool BeginConnect(NodeViewModel node)
    {
        if (!IsEditable || IsConnecting) return false;

        SelectNode(node);
        ConnectionSource = node;
        node.IsConnectionSource = true;
        IsConnecting = true;

        ConnectModeChanged?.Invoke(this, new ConnectModeEventArgs(ConnectModeChange.Started, node, null));
        return true;
    }

    /// <summary>Starts connect mode using <see cref="SelectedNode"/> as the source.</summary>
    public bool TryBeginConnect()
    {
        if (SelectedNode is null) return false;
        return BeginConnect(SelectedNode);
    }

    /// <summary>
    /// Confirms the connection from <see cref="ConnectionSource"/> to the current
    /// target candidate (<see cref="SelectedNode"/>), called only while
    /// <see cref="IsConnecting"/>. If the target is the source itself or a node
    /// already connected to the source's output, this is a silent no-op and connect
    /// mode remains active (spec decision 2 — matches
    /// <see cref="PendingConnectionViewModel.Complete"/>'s self/duplicate handling).
    /// Always returns true: the key is consumed either way.
    /// </summary>
    public bool TryConfirmConnection()
    {
        var source = ConnectionSource!;
        var target = SelectedNode;

        var isSelf      = target == source;
        var isDuplicate = target is not null &&
            Connections.Any(c => c.Source == source.Output && c.Target == target.Input);

        if (target is not null && !isSelf && !isDuplicate)
        {
            AddConnection(source.Output, target.Input);
            ExitConnectMode();
            ConnectModeChanged?.Invoke(this, new ConnectModeEventArgs(ConnectModeChange.Connected, source, target));
        }

        return true;
    }

    /// <summary>
    /// Cancels connect mode without creating a connection (selection unchanged).
    /// Called only while <see cref="IsConnecting"/>. Always returns true.
    /// </summary>
    public bool CancelConnect()
    {
        var source = ConnectionSource!;
        ExitConnectMode();
        ConnectModeChanged?.Invoke(this, new ConnectModeEventArgs(ConnectModeChange.Cancelled, source, null));
        return true;
    }

    private void ExitConnectMode()
    {
        if (ConnectionSource is not null)
            ConnectionSource.IsConnectionSource = false;
        ConnectionSource = null;
        IsConnecting     = false;
    }
```

- [ ] **Step 5: Hook `DeleteNode` to cancel connect mode for the deleted source**

In `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`, find:

```csharp
    public void DeleteNode(NodeViewModel node)
    {
        var removed = Connections
            .Where(c => c.Source.Owner == node || c.Target.Owner == node)
            .ToList();
        _undoStack.Execute(new DeleteNodeCommand(this, node, removed));
        IsModified = true;
        RefreshUndoRedo();
    }
```

Replace with:

```csharp
    public void DeleteNode(NodeViewModel node)
    {
        // Connect mode cannot reference a deleted node.
        if (node == ConnectionSource)
            CancelConnect();

        var removed = Connections
            .Where(c => c.Source.Owner == node || c.Target.Owner == node)
            .ToList();
        _undoStack.Execute(new DeleteNodeCommand(this, node, removed));
        IsModified = true;
        RefreshUndoRedo();
    }
```

- [ ] **Step 6: Add the `BeginConnectCmd` relay command**

In `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`, find:

```csharp
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void AddConnectedNodeCmd(NodeViewModel? parent)
    {
        if (parent is null) return;
```

Add a new command directly above it:

```csharp
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void BeginConnectCmd(NodeViewModel? node)
    {
        if (node is not null) BeginConnect(node);
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void AddConnectedNodeCmd(NodeViewModel? parent)
    {
        if (parent is null) return;
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter ConversationViewModelConnectModeTests`
Expected: `Passed!` — all 11 tests pass.

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs DialogEditor.Tests/ViewModels/ConversationViewModelConnectModeTests.cs
git commit -m "feat(connect-mode): add ConversationViewModel connect-mode state machine"
```

---

## Task 3: Keyboard wiring — Ctrl+L, Enter/Escape while connecting

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml.cs:26-63`
- Test: `DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs`

- [ ] **Step 1: Write the failing tests**

In `DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs`, add these tests after
`MenuKey_OpensSelectedNodeContextMenu` (before the closing `}` of the class):

```csharp
    [AvaloniaFact]
    public void CtrlL_StartsConnectMode()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);

        Press(view, Key.L, KeyModifiers.Control);

        Assert.True(vm.IsConnecting);
        Assert.Same(root, vm.ConnectionSource);
    }

    [AvaloniaFact]
    public void CtrlL_WithoutSelection_DoesNotStartConnectMode()
    {
        var (_, view, vm, _, _) = Setup();

        Press(view, Key.L, KeyModifiers.Control);

        Assert.False(vm.IsConnecting);
    }

    [AvaloniaFact]
    public void Enter_WhileConnecting_ConfirmsInsteadOfFocusingDetail()
    {
        var (_, view, vm, root, child) = Setup();
        vm.BeginConnect(root);
        vm.SelectNode(child);

        var raised = false;
        view.FocusDetailRequested += (_, _) => raised = true;
        Press(view, Key.Enter);

        Assert.False(raised);
        Assert.False(vm.IsConnecting);
        Assert.Contains(vm.Connections, c => c.Source == root.Output && c.Target == child.Input);
    }

    [AvaloniaFact]
    public void Escape_WhileConnecting_CancelsInsteadOfDeselecting()
    {
        var (_, view, vm, root, _) = Setup();
        vm.BeginConnect(root);

        Press(view, Key.Escape);

        Assert.False(vm.IsConnecting);
        Assert.Same(root, vm.SelectedNode); // selection unchanged, per spec decision 2
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter ConversationViewKeyboardTests`
Expected:
- `CtrlL_StartsConnectMode` and `CtrlL_WithoutSelection_DoesNotStartConnectMode` FAIL
  (Ctrl+L isn't wired — `vm.IsConnecting` stays `false`).
- `Enter_WhileConnecting_ConfirmsInsteadOfFocusingDetail` FAILS (`raised` is `true` —
  Enter still calls `RaiseFocusDetail`).
- `Escape_WhileConnecting_CancelsInsteadOfDeselecting` FAILS (`vm.SelectedNode` is
  `null` — Escape still calls `Deselect`).
- All other existing tests in the file still PASS.

- [ ] **Step 3: Wire Ctrl+L and reorder the Enter/Escape arms**

In `DialogEditor.Avalonia/Views/ConversationView.axaml.cs`, find:

```csharp
            var handled = e.Key switch
            {
                Key.Right when ctrl => vm.NudgeSelected(step, 0),
                Key.Left  when ctrl => vm.NudgeSelected(-step, 0),
                Key.Up    when ctrl => vm.NudgeSelected(0, -step),
                Key.Down  when ctrl => vm.NudgeSelected(0, step),

                Key.Right when none => vm.TryNavigate(CanvasNavDirection.Child),
                Key.Left  when none => vm.TryNavigate(CanvasNavDirection.Parent),
                Key.Up    when none => vm.TryNavigate(CanvasNavDirection.PreviousSibling),
                Key.Down  when none => vm.TryNavigate(CanvasNavDirection.NextSibling),

                Key.PageDown when none => vm.TryCycle(forward: true),
                Key.PageUp   when none => vm.TryCycle(forward: false),
                Key.Home     when none => vm.TrySelectRoot(),

                Key.Enter when none && vm.SelectedNode is not null => RaiseFocusDetail(),

                Key.Apps                                          => OpenSelectedNodeContextMenu(vm),
                Key.F10 when e.KeyModifiers == KeyModifiers.Shift => OpenSelectedNodeContextMenu(vm),

                Key.Escape when none => vm.Deselect(),

                _ => false,
            };
```

Replace with:

```csharp
            var handled = e.Key switch
            {
                Key.Right when ctrl => vm.NudgeSelected(step, 0),
                Key.Left  when ctrl => vm.NudgeSelected(-step, 0),
                Key.Up    when ctrl => vm.NudgeSelected(0, -step),
                Key.Down  when ctrl => vm.NudgeSelected(0, step),
                Key.L     when ctrl => vm.TryBeginConnect(),

                Key.Right when none => vm.TryNavigate(CanvasNavDirection.Child),
                Key.Left  when none => vm.TryNavigate(CanvasNavDirection.Parent),
                Key.Up    when none => vm.TryNavigate(CanvasNavDirection.PreviousSibling),
                Key.Down  when none => vm.TryNavigate(CanvasNavDirection.NextSibling),

                Key.PageDown when none => vm.TryCycle(forward: true),
                Key.PageUp   when none => vm.TryCycle(forward: false),
                Key.Home     when none => vm.TrySelectRoot(),

                // Order matters: connect-mode arms must win over the unconditional
                // SelectedNode-based arms below (same Key, both `when none`).
                Key.Enter when none && vm.IsConnecting => vm.TryConfirmConnection(),
                Key.Enter when none && vm.SelectedNode is not null => RaiseFocusDetail(),

                Key.Apps                                          => OpenSelectedNodeContextMenu(vm),
                Key.F10 when e.KeyModifiers == KeyModifiers.Shift => OpenSelectedNodeContextMenu(vm),

                Key.Escape when none && vm.IsConnecting => vm.CancelConnect(),
                Key.Escape when none                    => vm.Deselect(),

                _ => false,
            };
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter ConversationViewKeyboardTests`
Expected: `Passed!` — all tests in the file pass, including the 4 new ones and the
existing `Enter_RaisesFocusDetailRequested`/`Enter_WithoutSelection_DoesNotRaise`/
`Escape_Deselects` regression tests.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConversationView.axaml.cs DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs
git commit -m "feat(connect-mode): wire Ctrl+L and reinterpret Enter/Escape while connecting"
```

---

## Task 4: "Connect to…" context menu item + source highlight overlay

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml:136-192`
- Test: `DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs`

- [ ] **Step 1: Write the failing tests**

In `DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs`, add these tests after
the Task 3 tests:

```csharp
    [AvaloniaFact]
    public void ConnectToNode_MenuItem_StartsConnectMode()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);

        Press(view, Key.Apps);

        var editor = view.FindControl<Control>("Editor")!;
        var menu = ((global::Avalonia.Visual)editor).GetVisualDescendants()
            .OfType<Control>()
            .Select(c => c.ContextMenu)
            .FirstOrDefault(m => m is not null);
        Assert.NotNull(menu);

        var connectItem = menu!.Items
            .OfType<MenuItem>()
            .First(i => Equals(i.CommandParameter, root));
        connectItem.Command!.Execute(root);

        Assert.True(vm.IsConnecting);
        Assert.Same(root, vm.ConnectionSource);
    }

    [AvaloniaFact]
    public void ConnectionSourceOverlay_VisibleOnlyForConnectionSource()
    {
        var (_, view, vm, root, child) = Setup();
        vm.BeginConnect(root);

        var editor = view.FindControl<Control>("Editor")!;
        var overlays = ((global::Avalonia.Visual)editor).GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Name == "ConnectionSourceBadge")
            .ToList();

        Assert.Equal(2, overlays.Count); // one per realized node container
        Assert.True(root.IsConnectionSource);
        Assert.False(child.IsConnectionSource);
    }
```

`MenuItem` requires `using Avalonia.Controls;` which the file already has.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter ConversationViewKeyboardTests`
Expected:
- `ConnectToNode_MenuItem_StartsConnectMode` FAILS — `connectItem` lookup throws
  (`InvalidOperationException: Sequence contains no matching element`), no "Connect
  to…" menu item exists yet.
- `ConnectionSourceOverlay_VisibleOnlyForConnectionSource` FAILS — no `Border` named
  `ConnectionSourceBadge` exists yet, so `overlays.Count` is `0`.

- [ ] **Step 3: Add the new localisation strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, find:

```xml
    <!-- ─── Editing — canvas context menus ──────────────────────────────── -->
    <sys:String x:Key="Menu_DeleteNode">Delete node</sys:String>
    <sys:String x:Key="Menu_AddConnectedNode">Add connected node</sys:String>
    <sys:String x:Key="Menu_DeleteConnection">Delete connection</sys:String>
```

Replace with:

```xml
    <!-- ─── Editing — canvas context menus ──────────────────────────────── -->
    <sys:String x:Key="Menu_DeleteNode">Delete node</sys:String>
    <sys:String x:Key="Menu_AddConnectedNode">Add connected node</sys:String>
    <sys:String x:Key="Menu_DeleteConnection">Delete connection</sys:String>
    <sys:String x:Key="Menu_ConnectToNode">Connect to…</sys:String>
```

Then find:

```xml
    <!-- ─── Canvas context menus ──────────────────────────────────────────── -->
    <sys:String x:Key="ToolTip_DeleteNode">Remove this node and all its connections from the conversation</sys:String>
    <sys:String x:Key="ToolTip_AddConnectedNode">Create a new node and link it from this one</sys:String>
    <sys:String x:Key="ToolTip_DeleteConnection">Remove the link between these two nodes</sys:String>
```

Replace with:

```xml
    <!-- ─── Canvas context menus ──────────────────────────────────────────── -->
    <sys:String x:Key="ToolTip_DeleteNode">Remove this node and all its connections from the conversation</sys:String>
    <sys:String x:Key="ToolTip_AddConnectedNode">Create a new node and link it from this one</sys:String>
    <sys:String x:Key="ToolTip_DeleteConnection">Remove the link between these two nodes</sys:String>
    <sys:String x:Key="ToolTip_ConnectToNode">Start connecting this node to another one. Use the arrow keys to pick the destination, Enter to confirm, or Escape to cancel. (Shortcut: Ctrl+L)</sys:String>
```

- [ ] **Step 4: Add the "Connect to…" menu item**

In `DialogEditor.Avalonia/Views/ConversationView.axaml`, find:

```xml
                            <nodify:Node.ContextMenu>
                                <ContextMenu>
                                    <MenuItem Header="{StaticResource Menu_DeleteNode}"
                                              Command="{Binding $parent[UserControl].DataContext.DeleteNodeCmdCommand}"
                                              CommandParameter="{Binding}"
                                              ToolTip.Tip="{StaticResource ToolTip_DeleteNode}"
                                              AutomationProperties.HelpText="{StaticResource ToolTip_DeleteNode}"/>
                                    <MenuItem Header="{StaticResource Menu_AddConnectedNode}"
                                              Command="{Binding $parent[UserControl].DataContext.AddConnectedNodeCmdCommand}"
                                              CommandParameter="{Binding}"
                                              ToolTip.Tip="{StaticResource ToolTip_AddConnectedNode}"
                                              AutomationProperties.HelpText="{StaticResource ToolTip_AddConnectedNode}"/>
                                </ContextMenu>
                            </nodify:Node.ContextMenu>
```

Replace with:

```xml
                            <nodify:Node.ContextMenu>
                                <ContextMenu>
                                    <MenuItem Header="{StaticResource Menu_DeleteNode}"
                                              Command="{Binding $parent[UserControl].DataContext.DeleteNodeCmdCommand}"
                                              CommandParameter="{Binding}"
                                              ToolTip.Tip="{StaticResource ToolTip_DeleteNode}"
                                              AutomationProperties.HelpText="{StaticResource ToolTip_DeleteNode}"/>
                                    <MenuItem Header="{StaticResource Menu_AddConnectedNode}"
                                              Command="{Binding $parent[UserControl].DataContext.AddConnectedNodeCmdCommand}"
                                              CommandParameter="{Binding}"
                                              ToolTip.Tip="{StaticResource ToolTip_AddConnectedNode}"
                                              AutomationProperties.HelpText="{StaticResource ToolTip_AddConnectedNode}"/>
                                    <MenuItem Header="{StaticResource Menu_ConnectToNode}"
                                              Command="{Binding $parent[UserControl].DataContext.BeginConnectCmdCommand}"
                                              CommandParameter="{Binding}"
                                              ToolTip.Tip="{StaticResource ToolTip_ConnectToNode}"
                                              AutomationProperties.HelpText="{StaticResource ToolTip_ConnectToNode}"/>
                                </ContextMenu>
                            </nodify:Node.ContextMenu>
```

- [ ] **Step 5: Add the connect-mode source highlight overlay**

In `DialogEditor.Avalonia/Views/ConversationView.axaml`, find the diff status badge
block (immediately after the diff tint overlay border):

```xml
                                <!-- Diff status badge — corner glyph mirroring the border colour;
                                     transparent background + empty glyph when Unchanged, so it
                                     renders as nothing (Layer 2.5) -->
                                <Border Grid.Row="0" Grid.RowSpan="3"
                                        Width="18" Height="18" CornerRadius="9"
                                        HorizontalAlignment="Right" VerticalAlignment="Top"
                                        Margin="0,-9,-9,0"
                                        IsHitTestVisible="False"
                                        ZIndex="11"
                                        Background="{Binding DiffStatus, Converter={StaticResource DiffStatusToBrushConverter}}">
                                    <TextBlock Text="{Binding DiffStatus, Converter={StaticResource DiffStatusToGlyph}}"
                                               Foreground="{DynamicResource Brush.Text.OnAccent}" FontWeight="Bold" FontSize="{StaticResource FontSize.Label}"
                                               HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
```

Immediately after that closing `</Border>`, add:

```xml

                                <!-- Connect-mode source highlight — dashed amber border + 🔗
                                     badge, top-left (opposite the diff badge's top-right corner
                                     so the two never collide). Transparent/invisible when this
                                     node isn't the connect-mode source (Layer 2.5), reusing
                                     Brush.Severity.Warning so no new colour token is needed. -->
                                <Rectangle Grid.Row="0" Grid.RowSpan="3"
                                           IsHitTestVisible="False"
                                           ZIndex="10"
                                           Stretch="Fill"
                                           StrokeThickness="3"
                                           StrokeDashArray="6,3"
                                           Stroke="{DynamicResource Brush.Severity.Warning}"
                                           IsVisible="{Binding IsConnectionSource}"/>

                                <Border x:Name="ConnectionSourceBadge"
                                        Grid.Row="0" Grid.RowSpan="3"
                                        Width="18" Height="18" CornerRadius="9"
                                        HorizontalAlignment="Left" VerticalAlignment="Top"
                                        Margin="-9,-9,0,0"
                                        IsHitTestVisible="False"
                                        ZIndex="11"
                                        Background="{DynamicResource Brush.Severity.Warning}"
                                        IsVisible="{Binding IsConnectionSource}">
                                    <TextBlock Text="🔗"
                                               FontSize="{StaticResource FontSize.Label}"
                                               HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
```

`Rectangle` is an Avalonia `Shape` (same family as the `nodify:Connection`/
`nodify:PendingConnection` shapes that already use `StrokeDashArray` elsewhere in this
file), so `StrokeDashArray` is supported there, unlike on `Border`. No new `xmlns` is
needed: `ConversationView.axaml` already uses the unprefixed `<Path>` shape (lines
~207-218, the node-type marker) under the default `xmlns="https://github.com/avaloniaui"`,
which resolves `Avalonia.Controls.Shapes` types — `<Rectangle>` resolves the same way.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter ConversationViewKeyboardTests`
Expected: `Passed!` — all tests in the file pass, including the 2 new ones from Step 1.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/Views/ConversationView.axaml DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs
git commit -m "feat(connect-mode): add Connect to... context menu item and source highlight overlay"
```

---

## Task 5: `MainWindowViewModel` — `ConnectModeChanged` → `StatusText`

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs:296-304`
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

In `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`, add a new test class
member group (place these near other `StatusText`-asserting tests, anywhere in the
class body):

```csharp
    [Fact]
    public void ConnectModeChanged_Started_SetsStatusText()
    {
        var vm = MakeVm();
        var source = MakeConnectModeNode(1);
        vm.Canvas.AddNode(source, new DialogEditor.Core.Layout.LayoutPoint(0, 0));

        vm.Canvas.BeginConnect(source);

        Assert.Equal("Status_ConnectMode_Started", vm.StatusText);
    }

    [Fact]
    public void ConnectModeChanged_Connected_SetsStatusText()
    {
        var vm = MakeVm();
        var source = MakeConnectModeNode(1);
        var target = MakeConnectModeNode(2);
        vm.Canvas.AddNode(source, new DialogEditor.Core.Layout.LayoutPoint(0, 0));
        vm.Canvas.AddNode(target, new Core.Layout.LayoutPoint(200, 0));
        vm.Canvas.BeginConnect(source);
        vm.Canvas.SelectNode(target);

        vm.Canvas.TryConfirmConnection();

        Assert.Equal("Status_ConnectMode_Connected", vm.StatusText);
    }

    [Fact]
    public void ConnectModeChanged_Cancelled_SetsStatusText()
    {
        var vm = MakeVm();
        var source = MakeConnectModeNode(1);
        vm.Canvas.AddNode(source, new DialogEditor.Core.Layout.LayoutPoint(0, 0));
        vm.Canvas.BeginConnect(source);

        vm.Canvas.CancelConnect();

        Assert.Equal("Status_ConnectMode_Cancelled", vm.StatusText);
    }

    private static NodeViewModel MakeConnectModeNode(int id) =>
        new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);
```

`StubStringProvider.Get(key) => key` and `Loc.Format` falls back to the raw key when it
has no `{0}` placeholders (see the existing `Status_ProjectGitConflictUnparseable`-style
assertions in this file), so asserting the bare resource key is correct and stable
regardless of the `{0}`/`{1}` node-id arguments passed to `Loc.Format`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter MainWindowViewModelTests`
Expected: the 3 new tests FAIL with `Assert.Equal` failures — `vm.StatusText` is `null`
(or its constructor-default value), because nothing subscribes to
`Canvas.ConnectModeChanged` yet.

- [ ] **Step 3: Add the new status-message strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, find:

```xml
    <sys:String x:Key="Status_RestoreError">Restore failed: {0}</sys:String>
    <sys:String x:Key="Status_NoBackupFound">No backup found for this game folder.</sys:String>
```

Replace with:

```xml
    <sys:String x:Key="Status_RestoreError">Restore failed: {0}</sys:String>
    <sys:String x:Key="Status_NoBackupFound">No backup found for this game folder.</sys:String>

    <!-- ─── Editing — connect mode (Ctrl+L / "Connect to…") ─────────────── -->
    <!-- {0} = source node id -->
    <sys:String x:Key="Status_ConnectMode_Started">Connecting from node {0}. Use the arrow keys to choose a destination, Enter to confirm, or Escape to cancel.</sys:String>
    <!-- {0} = source node id, {1} = target node id -->
    <sys:String x:Key="Status_ConnectMode_Connected">Connected node {0} to node {1}.</sys:String>
    <sys:String x:Key="Status_ConnectMode_Cancelled">Connect mode cancelled.</sys:String>
```

- [ ] **Step 4: Subscribe to `Canvas.ConnectModeChanged`**

In `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`, find:

```csharp
        Canvas.Connections.CollectionChanged += (_, args) =>
        {
            RefreshDetailLinks();
            // Subscribe to property changes on newly added connections so edits
            // (QuestionNodeTextDisplay, RandomWeight) mark the canvas as modified.
            if (args.NewItems is not null)
                foreach (ConnectionViewModel conn in args.NewItems)
                    conn.PropertyChanged += (_, _) => Canvas.IsModified = true;
        };
```

Immediately after that block, add:

```csharp

        Canvas.ConnectModeChanged += (_, e) =>
        {
            StatusText = e.Change switch
            {
                ConnectModeChange.Started   => Loc.Format("Status_ConnectMode_Started", e.Source.NodeId),
                ConnectModeChange.Connected => Loc.Format("Status_ConnectMode_Connected", e.Source.NodeId, e.Target!.NodeId),
                ConnectModeChange.Cancelled => Loc.Get("Status_ConnectMode_Cancelled"),
                _                            => StatusText,
            };
        };
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter MainWindowViewModelTests`
Expected: `Passed!` — the 3 new tests pass, and existing `MainWindowViewModelTests`
(constructor side effects, `StatusText` assertions for project load/save, etc.) are
unaffected.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat(connect-mode): announce connect-mode transitions via StatusText"
```

---

## Task 6: Legend documentation

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.Avalonia/Views/LegendWindow.axaml:223-259`

Markup-only addition of new legend rows. Per the spec, this is covered by the
localisation structure itself (resource keys present and non-empty) rather than a
behavioural test — consistent with the 2026-06-12 precedent (no dedicated test exists
for the other `Legend_Key_*` rows either). No test step in this task.

- [ ] **Step 1: Add the new legend strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, find:

```xml
    <sys:String x:Key="Legend_Key_Escape">Esc</sys:String>
    <sys:String x:Key="Legend_Key_Escape_Desc">Deselect the current node</sys:String>
```

Replace with:

```xml
    <sys:String x:Key="Legend_Key_Escape">Esc</sys:String>
    <sys:String x:Key="Legend_Key_Escape_Desc">Deselect the current node</sys:String>
    <sys:String x:Key="Legend_Key_CtrlL">Ctrl + L</sys:String>
    <sys:String x:Key="Legend_Key_CtrlL_Desc">Start connecting the selected line to another one. (Also available from the right-click / Menu-key menu as "Connect to…".)</sys:String>
    <sys:String x:Key="Legend_ConnectMode_WhileConnecting">While connecting: use the arrow keys to pick the destination line, press Enter to confirm, or Escape to cancel.</sys:String>
```

- [ ] **Step 2: Add the new legend rows**

In `DialogEditor.Avalonia/Views/LegendWindow.axaml`, find:

```xml
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_Escape}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="{StaticResource FontSize.Body}"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_Escape_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="{StaticResource FontSize.Body}" TextWrapping="Wrap"/>
            </Grid>
        </StackPanel>
    </ScrollViewer>
</Window>
```

Replace with:

```xml
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_Escape}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="{StaticResource FontSize.Body}"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_Escape_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="{StaticResource FontSize.Body}" TextWrapping="Wrap"/>
            </Grid>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_CtrlL}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="{StaticResource FontSize.Body}"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_CtrlL_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="{StaticResource FontSize.Body}" TextWrapping="Wrap"/>
            </Grid>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="{StaticResource FontSize.Body}"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_ConnectMode_WhileConnecting}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="{StaticResource FontSize.Body}" TextWrapping="Wrap"/>
            </Grid>
        </StackPanel>
    </ScrollViewer>
</Window>
```

The last row's first column is intentionally an empty `TextBlock` (not a key/shortcut —
this row explains contextual *behaviour* of the keys above, not a new key), keeping the
two-column layout consistent.

- [ ] **Step 3: Build to verify the XAML compiles**

Run: `dotnet build DialogEditor.slnx`
Expected: `Build succeeded.` (no errors)

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test DialogEditor.Tests`
Expected: `Passed!` — all tests pass, including every test added in Tasks 2-5.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/Views/LegendWindow.axaml
git commit -m "docs(connect-mode): add Ctrl+L and while-connecting rows to the canvas legend"
```

---

## Out of scope (per spec)

- Multi-connector nodes (the "pick a connector" step) — not applicable today.
- Deleting connections from the keyboard — `DeleteConnectionCmd` remains mouse-only.
- A live "already connected" preview while navigating in connect mode — the
  confirm-time duplicate check (Task 2) covers correctness.
