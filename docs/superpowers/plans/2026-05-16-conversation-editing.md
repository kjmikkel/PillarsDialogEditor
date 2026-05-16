# Conversation Editing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add full structural editing of existing conversations (text, properties, nodes, links) with undo/redo, save/backup, and editable UI throughout.

**Architecture:** Viewmodel-as-truth — `NodeViewModel` gains mutable observable properties wired to an `UndoRedoStack` owned by `ConversationViewModel`. Serializers re-read the original file from disk and update only the editable fields in-place, leaving conditions/scripts untouched. All new strings are defined in `Strings.axaml` before use.

**Tech Stack:** C# 12, .NET 8, Avalonia UI, CommunityToolkit.Mvvm, Nodify (graph canvas), xUnit (tests).

**Spec:** `docs/superpowers/specs/2026-05-16-conversation-editing-design.md`

---

## Part A — Core Layer

---

### Task 1: IEditCommand + UndoRedoStack

**Files:**
- Create: `DialogEditor.Core/Editing/IEditCommand.cs`
- Create: `DialogEditor.Core/Editing/UndoRedoStack.cs`
- Create: `DialogEditor.Tests/Editing/UndoRedoStackTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/Editing/UndoRedoStackTests.cs
using DialogEditor.Core.Editing;

namespace DialogEditor.Tests.Editing;

public class UndoRedoStackTests
{
    private static IEditCommand MakeCommand(List<string> log, string name) =>
        new LambdaCommand(name,
            execute: () => log.Add($"do:{name}"),
            undo:    () => log.Add($"undo:{name}"));

    [Fact]
    public void Execute_RunsCommandAndAddsToHistory()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand(log, "A"));
        Assert.Equal(["do:A"], log);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Undo_RevertsLastCommand()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand(log, "A"));
        stack.Undo();
        Assert.Equal(["do:A", "undo:A"], log);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void Redo_ReappliesUndoneCommand()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand(log, "A"));
        stack.Undo();
        stack.Redo();
        Assert.Equal(["do:A", "undo:A", "do:A"], log);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Execute_AfterUndo_ClearsRedoStack()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand(log, "A"));
        stack.Undo();
        stack.Execute(MakeCommand(log, "B"));
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoDescription_ReturnsTopCommandDescription()
    {
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand([], "Alpha"));
        Assert.Equal("Alpha", stack.UndoDescription);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand([], "A"));
        stack.Clear();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Null(stack.UndoDescription);
        Assert.Null(stack.RedoDescription);
    }

    [Fact]
    public void Undo_WhenEmpty_DoesNotThrow()
    {
        var stack = new UndoRedoStack();
        var ex = Record.Exception(() => stack.Undo());
        Assert.Null(ex);
    }
}

// Helper for tests only — put in DialogEditor.Tests/Editing/LambdaCommand.cs
// (or inline as a nested private class inside UndoRedoStackTests)
internal sealed class LambdaCommand(string description, Action execute, Action undo)
    : IEditCommand
{
    public string Description => description;
    public void Execute() => execute();
    public void Undo() => undo();
}
```

- [ ] **Step 2: Run tests — confirm they fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~UndoRedoStackTests"
```

Expected: build error — `IEditCommand` and `UndoRedoStack` do not exist yet.

- [ ] **Step 3: Create IEditCommand**

```csharp
// DialogEditor.Core/Editing/IEditCommand.cs
namespace DialogEditor.Core.Editing;

public interface IEditCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}
```

- [ ] **Step 4: Create UndoRedoStack**

```csharp
// DialogEditor.Core/Editing/UndoRedoStack.cs
namespace DialogEditor.Core.Editing;

public sealed class UndoRedoStack
{
    private readonly Stack<IEditCommand> _undoStack = new();
    private readonly Stack<IEditCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? UndoDescription => CanUndo ? _undoStack.Peek().Description : null;
    public string? RedoDescription => CanRedo ? _redoStack.Peek().Description : null;

    public void Execute(IEditCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
```

- [ ] **Step 5: Run tests — confirm they pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~UndoRedoStackTests"
```

Expected: all 8 tests pass.

- [ ] **Step 6: Commit**

```
git add DialogEditor.Core/Editing/IEditCommand.cs
git add DialogEditor.Core/Editing/UndoRedoStack.cs
git add DialogEditor.Tests/Editing/UndoRedoStackTests.cs
git commit -m "feat: IEditCommand interface and UndoRedoStack"
```

---

### Task 2: SetPropertyCommand\<T\>

This is the single reusable command that covers every scalar property edit on a node.

**Files:**
- Create: `DialogEditor.Core/Editing/SetPropertyCommand.cs`
- Test: `DialogEditor.Tests/Editing/UndoRedoStackTests.cs` (extend existing file)

- [ ] **Step 1: Add a test for SetPropertyCommand**

Add this test to `UndoRedoStackTests.cs`:

```csharp
[Fact]
public void SetPropertyCommand_UndoRestoresOldValue()
{
    var holder = new { Value = "original" };
    string current = "original";

    var cmd = new SetPropertyCommand<string>(
        description: "Edit text",
        apply: v => current = v,
        oldValue: "original",
        newValue: "updated");

    var stack = new UndoRedoStack();
    stack.Execute(cmd);
    Assert.Equal("updated", current);

    stack.Undo();
    Assert.Equal("original", current);

    stack.Redo();
    Assert.Equal("updated", current);
}
```

- [ ] **Step 2: Run — confirm it fails**

```
dotnet test DialogEditor.Tests --filter "SetPropertyCommand_UndoRestoresOldValue"
```

- [ ] **Step 3: Create SetPropertyCommand**

```csharp
// DialogEditor.Core/Editing/SetPropertyCommand.cs
namespace DialogEditor.Core.Editing;

public sealed class SetPropertyCommand<T>(
    string description,
    Action<T> apply,
    T oldValue,
    T newValue) : IEditCommand
{
    public string Description => description;
    public void Execute() => apply(newValue);
    public void Undo()    => apply(oldValue);
}
```

- [ ] **Step 4: Run — confirm it passes**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~UndoRedoStackTests"
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Editing/SetPropertyCommand.cs
git add DialogEditor.Tests/Editing/UndoRedoStackTests.cs
git commit -m "feat: SetPropertyCommand generic edit command"
```

---

### Task 3: NodeIdAllocator

**Files:**
- Create: `DialogEditor.Core/Editing/NodeIdAllocator.cs`
- Create: `DialogEditor.Tests/Editing/NodeIdAllocatorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// DialogEditor.Tests/Editing/NodeIdAllocatorTests.cs
using DialogEditor.Core.Editing;

namespace DialogEditor.Tests.Editing;

public class NodeIdAllocatorTests
{
    [Fact]
    public void Next_EmptyList_ReturnsOne()
    {
        Assert.Equal(1, NodeIdAllocator.Next([]));
    }

    [Fact]
    public void Next_ReturnMaxPlusOne()
    {
        Assert.Equal(6, NodeIdAllocator.Next([1, 3, 5]));
    }

    [Fact]
    public void Next_SingleElement_ReturnsPlusOne()
    {
        Assert.Equal(8, NodeIdAllocator.Next([7]));
    }
}
```

- [ ] **Step 2: Run — confirm failure**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeIdAllocatorTests"
```

- [ ] **Step 3: Implement NodeIdAllocator**

```csharp
// DialogEditor.Core/Editing/NodeIdAllocator.cs
namespace DialogEditor.Core.Editing;

public static class NodeIdAllocator
{
    public static int Next(IEnumerable<int> existingIds)
    {
        var ids = existingIds.ToList();
        return ids.Count == 0 ? 1 : ids.Max() + 1;
    }
}
```

- [ ] **Step 4: Run — confirm pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeIdAllocatorTests"
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Editing/NodeIdAllocator.cs
git add DialogEditor.Tests/Editing/NodeIdAllocatorTests.cs
git commit -m "feat: NodeIdAllocator"
```

---

### Task 4: BackupService

**Files:**
- Create: `DialogEditor.Core/Backup/BackupService.cs`
- Create: `DialogEditor.Tests/Backup/BackupServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// DialogEditor.Tests/Backup/BackupServiceTests.cs
using DialogEditor.Core.Backup;

namespace DialogEditor.Tests.Backup;

public class BackupServiceTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose() => Directory.Delete(_tmp, recursive: true);

    [Fact]
    public async Task BackupAsync_CopiesAllFilesPreservingStructure()
    {
        // Arrange — source tree
        var src = Path.Combine(_tmp, "source");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.txt"),       "A");
        File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "B");

        var dest = Path.Combine(_tmp, "backup");

        // Act
        await BackupService.BackupAsync(src, dest, CancellationToken.None);

        // Assert
        Assert.Equal("A", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(dest, "sub", "b.txt")));
    }

    [Fact]
    public async Task RestoreAsync_OverwritesSourceFromBackup()
    {
        // Arrange
        var backup = Path.Combine(_tmp, "backup");
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "file.txt"), "original");

        var live = Path.Combine(_tmp, "live");
        Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, "file.txt"), "modified");

        // Act
        await BackupService.RestoreAsync(backup, live, CancellationToken.None);

        // Assert
        Assert.Equal("original", File.ReadAllText(Path.Combine(live, "file.txt")));
    }

    [Fact]
    public async Task BackupAsync_EmptySource_CreatesEmptyDest()
    {
        var src  = Path.Combine(_tmp, "empty");
        var dest = Path.Combine(_tmp, "out");
        Directory.CreateDirectory(src);

        await BackupService.BackupAsync(src, dest, CancellationToken.None);

        Assert.True(Directory.Exists(dest));
        Assert.Empty(Directory.GetFiles(dest));
    }
}
```

- [ ] **Step 2: Run — confirm failure**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~BackupServiceTests"
```

- [ ] **Step 3: Implement BackupService**

```csharp
// DialogEditor.Core/Backup/BackupService.cs
namespace DialogEditor.Core.Backup;

public static class BackupService
{
    public static async Task BackupAsync(
        string sourceRoot,
        string destRoot,
        CancellationToken ct,
        IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(destRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, file);
            var target   = Path.Combine(destRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            progress?.Report(relative);
        }
        await Task.CompletedTask;
    }

    public static async Task RestoreAsync(
        string backupRoot,
        string destRoot,
        CancellationToken ct,
        IProgress<string>? progress = null)
    {
        await BackupAsync(backupRoot, destRoot, ct, progress);
    }
}
```

- [ ] **Step 4: Run — confirm pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~BackupServiceTests"
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Backup/BackupService.cs
git add DialogEditor.Tests/Backup/BackupServiceTests.cs
git commit -m "feat: BackupService — copy and restore directory trees"
```

---

### Task 5: AppSettings — backup tracking

`AppSettings` stores settings as a JSON file. Add two new fields:
- `KnownGameDirectories` — `HashSet<string>` so the backup offer fires only once per game folder
- `BackupPaths` — `Dictionary<string, string>` mapping game directory → chosen backup root

**Files:**
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs`

No separate tests — `AppSettings` is a thin persistence wrapper; testing it requires real file I/O and is covered by integration testing.

- [ ] **Step 1: Extend SettingsData**

In `AppSettings.cs`, update the inner `SettingsData` class and add the new public properties:

```csharp
private sealed class SettingsData
{
    public string? LastLanguage       { get; set; }
    public string? LastGameDirectory  { get; set; }
    public bool BrowserPinned         { get; set; } = true;
    public bool DetailExpanded        { get; set; } = true;
    // New fields:
    public HashSet<string> KnownGameDirectories { get; set; } = [];
    public Dictionary<string, string> BackupPaths { get; set; } = [];
}
```

Then add the two new public accessors after the existing `DetailExpanded` block:

```csharp
public static bool IsKnownGameDirectory(string path)
    => Load().KnownGameDirectories.Contains(path);

public static void MarkGameDirectoryKnown(string path)
{
    var s = Load();
    s.KnownGameDirectories.Add(path);
    Save(s);
}

public static string? GetBackupPath(string gameDirectory)
    => Load().BackupPaths.GetValueOrDefault(gameDirectory);

public static void SetBackupPath(string gameDirectory, string backupRoot)
{
    var s = Load();
    s.BackupPaths[gameDirectory] = backupRoot;
    Save(s);
}
```

- [ ] **Step 2: Run all existing tests to confirm nothing broken**

```
dotnet test DialogEditor.Tests
```

Expected: all existing tests still pass.

- [ ] **Step 3: Commit**

```
git add DialogEditor.ViewModels/Services/AppSettings.cs
git commit -m "feat: AppSettings — backup path and known-game-directory tracking"
```

---
