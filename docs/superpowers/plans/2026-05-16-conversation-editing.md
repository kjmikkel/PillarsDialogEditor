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

## Part B — Serializers

The serializers live in `DialogEditor.Core` and therefore cannot reference ViewModels. A thin snapshot record bridges the two layers.

---

### Task 6: ConversationEditSnapshot

A plain-data record that the ViewModel layer fills in and passes to `IGameDataProvider.SaveConversation`.

**Files:**
- Create: `DialogEditor.Core/Editing/ConversationEditSnapshot.cs`

No tests needed — it is a plain record with no logic.

- [ ] **Step 1: Create the snapshot records**

```csharp
// DialogEditor.Core/Editing/ConversationEditSnapshot.cs
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Editing;

public record LinkEditSnapshot(
    int FromNodeId,
    int ToNodeId,
    float RandomWeight,
    string QuestionNodeTextDisplay,
    bool HasConditions
);

public record NodeEditSnapshot(
    int NodeId,
    bool IsPlayerChoice,
    SpeakerCategory SpeakerCategory,
    string SpeakerGuid,
    string ListenerGuid,
    string DefaultText,
    string FemaleText,
    string DisplayType,
    string Persistence,
    string ActorDirection,
    string Comments,
    string ExternalVO,
    bool HasVO,
    bool HideSpeaker,
    IReadOnlyList<LinkEditSnapshot> Links
);

public record ConversationEditSnapshot(IReadOnlyList<NodeEditSnapshot> Nodes);
```

- [ ] **Step 2: Run all tests — confirm nothing broken**

```
dotnet test DialogEditor.Tests
```

- [ ] **Step 3: Commit**

```
git add DialogEditor.Core/Editing/ConversationEditSnapshot.cs
git commit -m "feat: ConversationEditSnapshot data records for serializers"
```

---

### Task 7: StringTableSerializer

Both PoE1 and PoE2 use the same `.stringtable` XML format, so one serializer covers both. It takes the original XML as a string, updates/adds entries, and returns the modified XML. File I/O (including the `.bak` step) is a thin wrapper.

Format (from `StringTableParser`):
```xml
<StringTableFile>
  <Entries>
    <Entry><ID>0</ID><DefaultText>Hello</DefaultText><FemaleText></FemaleText></Entry>
  </Entries>
</StringTableFile>
```

**Files:**
- Create: `DialogEditor.Core/Serialization/StringTableSerializer.cs`
- Create: `DialogEditor.Tests/Serialization/StringTableSerializerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// DialogEditor.Tests/Serialization/StringTableSerializerTests.cs
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Tests.Serialization;

public class StringTableSerializerTests
{
    private static NodeEditSnapshot Node(int id, string def, string fem = "") =>
        new(id, false, SpeakerCategory.Npc, "", "", def, fem,
            "Conversation", "None", "", "", "", false, false, []);

    private const string TwoEntryXml = """
        <StringTableFile>
          <Entries>
            <Entry><ID>0</ID><DefaultText>Hello</DefaultText><FemaleText></FemaleText></Entry>
            <Entry><ID>1</ID><DefaultText>Goodbye</DefaultText><FemaleText>Farewell</FemaleText></Entry>
          </Entries>
        </StringTableFile>
        """;

    [Fact]
    public void Serialize_UpdatesExistingEntry()
    {
        var nodes = new[] { Node(0, "Hi"), Node(1, "Goodbye", "Farewell") };
        var result = StringTableSerializer.Serialize(TwoEntryXml, nodes);
        var reparsed = DialogEditor.Core.Parsing.StringTableParser.Parse(result);
        Assert.Equal("Hi", reparsed.Get(0)!.DefaultText);
        Assert.Equal("Goodbye", reparsed.Get(1)!.DefaultText);
    }

    [Fact]
    public void Serialize_AddsNewEntry()
    {
        var nodes = new[] { Node(0, "Hello"), Node(99, "New node text") };
        var result = StringTableSerializer.Serialize(TwoEntryXml, nodes);
        var reparsed = DialogEditor.Core.Parsing.StringTableParser.Parse(result);
        Assert.Equal("New node text", reparsed.Get(99)!.DefaultText);
    }

    [Fact]
    public void Serialize_PreservesEntriesNotInSnapshot()
    {
        var nodes = new[] { Node(0, "Hello") };  // node 1 not in snapshot
        var result = StringTableSerializer.Serialize(TwoEntryXml, nodes);
        var reparsed = DialogEditor.Core.Parsing.StringTableParser.Parse(result);
        Assert.Equal("Goodbye", reparsed.Get(1)!.DefaultText);  // unchanged
    }

    [Fact]
    public void Serialize_WritesFemaleText()
    {
        var nodes = new[] { Node(0, "Hello", "Greetings ladies") };
        var result = StringTableSerializer.Serialize(TwoEntryXml, nodes);
        var reparsed = DialogEditor.Core.Parsing.StringTableParser.Parse(result);
        Assert.Equal("Greetings ladies", reparsed.Get(0)!.FemaleText);
    }

    [Fact]
    public void Serialize_EmptyOriginal_CreatesValidDocument()
    {
        var nodes = new[] { Node(5, "Brand new") };
        var result = StringTableSerializer.Serialize(string.Empty, nodes);
        var reparsed = DialogEditor.Core.Parsing.StringTableParser.Parse(result);
        Assert.Equal("Brand new", reparsed.Get(5)!.DefaultText);
    }
}
```

- [ ] **Step 2: Run — confirm failure**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~StringTableSerializerTests"
```

- [ ] **Step 3: Implement StringTableSerializer**

```csharp
// DialogEditor.Core/Serialization/StringTableSerializer.cs
using System.Text;
using System.Xml.Linq;
using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Serialization;

public static class StringTableSerializer
{
    public static string Serialize(string originalXml, IEnumerable<NodeEditSnapshot> nodes)
    {
        XElement entries;
        XDocument doc;

        if (string.IsNullOrWhiteSpace(originalXml))
        {
            entries = new XElement("Entries");
            doc = new XDocument(new XElement("StringTableFile", entries));
        }
        else
        {
            doc = XDocument.Parse(originalXml);
            entries = doc.Descendants("Entries").First();
        }

        var byId = entries.Elements("Entry")
            .ToDictionary(e => (int)e.Element("ID")!);

        foreach (var node in nodes)
        {
            if (byId.TryGetValue(node.NodeId, out var entry))
            {
                entry.Element("DefaultText")!.Value = node.DefaultText;
                entry.Element("FemaleText")!.Value  = node.FemaleText;
            }
            else
            {
                entries.Add(new XElement("Entry",
                    new XElement("ID",          node.NodeId),
                    new XElement("DefaultText", node.DefaultText),
                    new XElement("FemaleText",  node.FemaleText)));
            }
        }

        return doc.ToString(SaveOptions.None);
    }

    public static void SaveToFile(string path, IEnumerable<NodeEditSnapshot> nodes)
    {
        var original = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, Serialize(original, nodes), Encoding.UTF8);
    }
}
```

- [ ] **Step 4: Run — confirm pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~StringTableSerializerTests"
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Serialization/StringTableSerializer.cs
git add DialogEditor.Tests/Serialization/StringTableSerializerTests.cs
git commit -m "feat: StringTableSerializer — update and write .stringtable XML"
```

---

### Task 8: Poe2ConversationSerializer

Re-reads the original `.conversationbundle` JSON, updates editable fields for each node in the snapshot, adds new nodes, removes deleted ones, and rebuilds Link arrays. Conditions and scripts in the original are left untouched.

**Files:**
- Create: `DialogEditor.Core/Serialization/Poe2ConversationSerializer.cs`
- Create: `DialogEditor.Tests/Serialization/Poe2ConversationSerializerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// DialogEditor.Tests/Serialization/Poe2ConversationSerializerTests.cs
using System.Text.Json.Nodes;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Tests.Serialization;

public class Poe2ConversationSerializerTests
{
    private const string TwoNodeJson = """
        {"Conversations": [{
          "Nodes": [
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "aaaa-0000", "ListenerGuid": "bbbb-0000",
              "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
              "NodeID": 0, "ContainerNodeID": -1,
              "Links": [{
                "$type": "OEIFormats.FlowCharts.Conversations.DialogueLink, OEIFormats",
                "FromNodeID": 0, "ToNodeID": 1, "PointsToGhost": false,
                "Conditionals": {"Operator": 0, "Components": []},
                "ClassExtender": {"ExtendedProperties": []},
                "RandomWeight": 1, "PlayQuestionNodeVO": true, "QuestionNodeTextDisplay": 0
              }],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": [{"Data":{"FullName":"SomeCondition","Parameters":[]}}]},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "HideSpeaker": false, "HasVO": false, "ExternalVO": "",
              "IsTempText": false, "PlayVOAs3DSound": false, "PlayType": 0,
              "NoPlayRandomWeight": 0, "VOPositioning": 0, "NotSkippable": false
            },
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "cccc-0000", "ListenerGuid": "dddd-0000",
              "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
              "NodeID": 1, "ContainerNodeID": -1, "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "HideSpeaker": false, "HasVO": false, "ExternalVO": "",
              "IsTempText": false, "PlayVOAs3DSound": false, "PlayType": 0,
              "NoPlayRandomWeight": 0, "VOPositioning": 0, "NotSkippable": false
            }
          ]
        }]}
        """;

    private static NodeEditSnapshot Node(int id, string speakerGuid = "aaaa-0000",
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, false, SpeakerCategory.Npc, speakerGuid, "bbbb-0000",
            "text", "", "Conversation", "None", "", "", "", false, false,
            links ?? []);

    [Fact]
    public void Serialize_UpdatesSpeakerGuid()
    {
        var snapshot = new ConversationEditSnapshot([Node(0, speakerGuid: "new-guid"), Node(1)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var nodes = Poe2ConversationParser.ParseJson(result);
        Assert.Equal("new-guid", nodes[0].SpeakerGuid);
    }

    [Fact]
    public void Serialize_PreservesOriginalConditions()
    {
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var root = JsonNode.Parse(result)!;
        var condComponents = root["Conversations"]![0]!["Nodes"]![0]!
            ["Conditionals"]!["Components"]!.AsArray();
        Assert.NotEmpty(condComponents);
    }

    [Fact]
    public void Serialize_DeletesRemovedNode()
    {
        var snapshot = new ConversationEditSnapshot([Node(0)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var nodes = Poe2ConversationParser.ParseJson(result);
        Assert.DoesNotContain(nodes, n => n.NodeId == 1);
    }

    [Fact]
    public void Serialize_AddsNewNode()
    {
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1), Node(99)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var nodes = Poe2ConversationParser.ParseJson(result);
        Assert.Contains(nodes, n => n.NodeId == 99);
    }

    [Fact]
    public void Serialize_RebuildLinks()
    {
        var links = new[] { new LinkEditSnapshot(0, 1, 1f, "ShowOnce", false) };
        var snapshot = new ConversationEditSnapshot([Node(0, links: links), Node(1)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var nodes = Poe2ConversationParser.ParseJson(result);
        Assert.Single(nodes[0].Links);
        Assert.Equal(1, nodes[0].Links[0].ToNodeId);
    }
}
```

- [ ] **Step 2: Run — confirm failure**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2ConversationSerializerTests"
```

- [ ] **Step 3: Implement Poe2ConversationSerializer**

```csharp
// DialogEditor.Core/Serialization/Poe2ConversationSerializer.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Serialization;

public static class Poe2ConversationSerializer
{
    private const string TalkNodeType     = "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats";
    private const string PlayerNodeType   = "OEIFormats.FlowCharts.Conversations.PlayerResponseNode, OEIFormats";
    private const string DialogueLinkType = "OEIFormats.FlowCharts.Conversations.DialogueLink, OEIFormats";

    public static string Serialize(string originalJson, ConversationEditSnapshot snapshot)
    {
        var root    = JsonNode.Parse(originalJson)!;
        var conv    = root["Conversations"]![0]!;
        var origArr = conv["Nodes"]!.AsArray();

        var origByNodeId = origArr
            .Where(n => n!["NodeID"] is not null)
            .ToDictionary(n => n!["NodeID"]!.GetValue<int>(), n => n!);

        var newArr = new JsonArray();

        foreach (var nodeSnap in snapshot.Nodes)
        {
            if (origByNodeId.TryGetValue(nodeSnap.NodeId, out var orig))
            {
                var updated = JsonNode.Parse(orig.ToJsonString())!;
                ApplyNodeSnapshot(updated, nodeSnap, orig);
                newArr.Add(updated);
            }
            else
            {
                newArr.Add(BuildNewNode(nodeSnap));
            }
        }

        conv["Nodes"] = newArr;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void ApplyNodeSnapshot(JsonNode node, NodeEditSnapshot snap, JsonNode original)
    {
        node["$type"]        = snap.IsPlayerChoice ? PlayerNodeType : TalkNodeType;
        node["SpeakerGuid"]  = snap.SpeakerGuid;
        node["ListenerGuid"] = snap.ListenerGuid;
        node["DisplayType"]  = MapDisplayType(snap.DisplayType);
        node["Persistence"]  = MapPersistence(snap.Persistence);
        node["HideSpeaker"]  = snap.HideSpeaker;
        node["HasVO"]        = snap.HasVO;
        node["ExternalVO"]   = snap.ExternalVO;
        node["Links"]        = BuildLinks(snap.Links, original["Links"]?.AsArray());
    }

    private static JsonArray BuildLinks(IReadOnlyList<LinkEditSnapshot> links, JsonArray? originalLinks)
    {
        var arr = new JsonArray();
        foreach (var link in links)
        {
            var orig = originalLinks?.FirstOrDefault(l =>
                l!["FromNodeID"]?.GetValue<int>() == link.FromNodeId &&
                l["ToNodeID"]?.GetValue<int>()    == link.ToNodeId);

            if (orig is not null)
            {
                var cloned = JsonNode.Parse(orig.ToJsonString())!;
                cloned["RandomWeight"]            = link.RandomWeight;
                cloned["QuestionNodeTextDisplay"] = MapQuestionDisplay(link.QuestionNodeTextDisplay);
                arr.Add(cloned);
            }
            else
            {
                arr.Add(BuildNewLink(link));
            }
        }
        return arr;
    }

    private static JsonNode BuildNewNode(NodeEditSnapshot snap) => JsonNode.Parse($$"""
        {
          "$type": "{{(snap.IsPlayerChoice ? PlayerNodeType : TalkNodeType)}}",
          "SpeakerGuid":  "{{snap.SpeakerGuid}}",
          "ListenerGuid": "{{snap.ListenerGuid}}",
          "IsQuestionNode": false,
          "DisplayType": {{MapDisplayType(snap.DisplayType)}},
          "Persistence": {{MapPersistence(snap.Persistence)}},
          "NodeID": {{snap.NodeId}},
          "ContainerNodeID": -1,
          "Links": [],
          "ClassExtender": {"ExtendedProperties": []},
          "Conditionals": {"Operator": 0, "Components": []},
          "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
          "HideSpeaker": {{snap.HideSpeaker.ToString().ToLower()}},
          "HasVO": {{snap.HasVO.ToString().ToLower()}},
          "ExternalVO": "{{snap.ExternalVO}}",
          "IsTempText": false, "PlayVOAs3DSound": false, "PlayType": 0,
          "NoPlayRandomWeight": 0, "VOPositioning": 0, "NotSkippable": false
        }
        """)!;

    private static JsonNode BuildNewLink(LinkEditSnapshot link) => JsonNode.Parse($$"""
        {
          "$type": "{{DialogueLinkType}}",
          "FromNodeID": {{link.FromNodeId}},
          "ToNodeID": {{link.ToNodeId}},
          "PointsToGhost": false,
          "Conditionals": {"Operator": 0, "Components": []},
          "ClassExtender": {"ExtendedProperties": []},
          "RandomWeight": {{link.RandomWeight}},
          "PlayQuestionNodeVO": true,
          "QuestionNodeTextDisplay": {{MapQuestionDisplay(link.QuestionNodeTextDisplay)}}
        }
        """)!;

    private static int MapDisplayType(string s)   => s == "Bark"     ? 1 : 0;
    private static int MapPersistence(string s)   => s == "OnceEver" ? 1 : 0;
    private static int MapQuestionDisplay(string s) => s switch
    {
        "Always" => 1,
        "Never"  => 2,
        _        => 0
    };

    public static void SaveToFile(string path, ConversationEditSnapshot snapshot)
    {
        var original = File.ReadAllText(path);
        File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, Serialize(original, snapshot), Encoding.UTF8);
    }
}
```

- [ ] **Step 4: Run — confirm pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2ConversationSerializerTests"
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Serialization/Poe2ConversationSerializer.cs
git add DialogEditor.Tests/Serialization/Poe2ConversationSerializerTests.cs
git commit -m "feat: Poe2ConversationSerializer — in-place JSON update with condition preservation"
```

---

### Task 9: Poe1ConversationSerializer

Same pattern as Task 8 but for the PoE1 XML format (`.conversation` files).

**Files:**
- Create: `DialogEditor.Core/Serialization/Poe1ConversationSerializer.cs`
- Create: `DialogEditor.Tests/Serialization/Poe1ConversationSerializerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// DialogEditor.Tests/Serialization/Poe1ConversationSerializerTests.cs
using System.Xml.Linq;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Tests.Serialization;

public class Poe1ConversationSerializerTests
{
    private const string TwoNodeXml = """
        <FlowChartFile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <Nodes>
            <FlowChartNode xsi:type="TalkNode">
              <NodeID>0</NodeID>
              <SpeakerGuid>aaaa</SpeakerGuid><ListenerGuid>bbbb</ListenerGuid>
              <Links>
                <FlowChartLink>
                  <FromNodeID>0</FromNodeID><ToNodeID>1</ToNodeID>
                  <RandomWeight>1</RandomWeight>
                  <QuestionNodeTextDisplay>ShowOnce</QuestionNodeTextDisplay>
                  <Conditionals><Components/></Conditionals>
                </FlowChartLink>
              </Links>
              <Conditionals><Components>
                <ExpressionComponent><Data><FullName>SomeCond</FullName><Parameters/></Data></ExpressionComponent>
              </Components></Conditionals>
              <OnEnterScripts/><OnExitScripts/><OnUpdateScripts/>
              <DisplayType>Conversation</DisplayType><Persistence>None</Persistence>
              <ActorDirection/><Comments/><VOFilename/>
            </FlowChartNode>
            <FlowChartNode xsi:type="TalkNode">
              <NodeID>1</NodeID>
              <SpeakerGuid>cccc</SpeakerGuid><ListenerGuid>dddd</ListenerGuid>
              <Links/>
              <Conditionals><Components/></Conditionals>
              <OnEnterScripts/><OnExitScripts/><OnUpdateScripts/>
              <DisplayType>Conversation</DisplayType><Persistence>None</Persistence>
              <ActorDirection/><Comments/><VOFilename/>
            </FlowChartNode>
          </Nodes>
        </FlowChartFile>
        """;

    private static NodeEditSnapshot Node(int id, string speaker = "aaaa",
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, false, SpeakerCategory.Npc, speaker, "bbbb",
            "text", "", "Conversation", "None", "", "", "", false, false,
            links ?? []);

    [Fact]
    public void Serialize_UpdatesSpeakerGuid()
    {
        var snapshot = new ConversationEditSnapshot([Node(0, "new-guid"), Node(1)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var nodes    = Poe1ConversationParser.ParseXml(result);
        Assert.Equal("new-guid", nodes[0].SpeakerGuid);
    }

    [Fact]
    public void Serialize_PreservesOriginalConditions()
    {
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var doc      = XDocument.Parse(result);
        var conds    = doc.Descendants("FlowChartNode").First()
                          .Element("Conditionals")!.Element("Components")!
                          .Elements("ExpressionComponent");
        Assert.NotEmpty(conds);
    }

    [Fact]
    public void Serialize_DeletesRemovedNode()
    {
        var snapshot = new ConversationEditSnapshot([Node(0)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var nodes    = Poe1ConversationParser.ParseXml(result);
        Assert.DoesNotContain(nodes, n => n.NodeId == 1);
    }

    [Fact]
    public void Serialize_AddsNewNode()
    {
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1), Node(99)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var nodes    = Poe1ConversationParser.ParseXml(result);
        Assert.Contains(nodes, n => n.NodeId == 99);
    }
}
```

- [ ] **Step 2: Run — confirm failure**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe1ConversationSerializerTests"
```

- [ ] **Step 3: Implement Poe1ConversationSerializer**

```csharp
// DialogEditor.Core/Serialization/Poe1ConversationSerializer.cs
using System.Text;
using System.Xml.Linq;
using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Serialization;

public static class Poe1ConversationSerializer
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public static string Serialize(string originalXml, ConversationEditSnapshot snapshot)
    {
        var doc   = XDocument.Parse(originalXml);
        var nodes = doc.Descendants("Nodes").First();

        var origByNodeId = nodes.Elements("FlowChartNode")
            .ToDictionary(e => (int)e.Element("NodeID")!);

        var snapshotIds = snapshot.Nodes.Select(n => n.NodeId).ToHashSet();

        foreach (var id in origByNodeId.Keys.Where(id => !snapshotIds.Contains(id)).ToList())
            origByNodeId[id].Remove();

        foreach (var snap in snapshot.Nodes)
        {
            if (origByNodeId.TryGetValue(snap.NodeId, out var elem))
                ApplyNodeSnapshot(elem, snap);
            else
                nodes.Add(BuildNewNode(snap));
        }

        return doc.ToString(SaveOptions.None);
    }

    private static void ApplyNodeSnapshot(XElement node, NodeEditSnapshot snap)
    {
        node.SetAttributeValue(Xsi + "type", snap.IsPlayerChoice ? "PlayerResponseNode" : "TalkNode");
        SetOrAdd(node, "SpeakerGuid",    snap.SpeakerGuid);
        SetOrAdd(node, "ListenerGuid",   snap.ListenerGuid);
        SetOrAdd(node, "DisplayType",    snap.DisplayType);
        SetOrAdd(node, "Persistence",    snap.Persistence);
        SetOrAdd(node, "ActorDirection", snap.ActorDirection);
        SetOrAdd(node, "Comments",       snap.Comments);
        SetOrAdd(node, "VOFilename",     snap.ExternalVO);

        var linksElem = node.Element("Links") ?? new XElement("Links");
        if (node.Element("Links") is null) node.Add(linksElem);
        var origLinks = linksElem.Elements("FlowChartLink").ToList();
        linksElem.RemoveAll();

        foreach (var link in snap.Links)
        {
            var orig = origLinks.FirstOrDefault(l =>
                (int)l.Element("FromNodeID")! == link.FromNodeId &&
                (int)l.Element("ToNodeID")!   == link.ToNodeId);

            if (orig is not null)
            {
                orig.Element("RandomWeight")!.Value            = link.RandomWeight.ToString();
                orig.Element("QuestionNodeTextDisplay")!.Value = link.QuestionNodeTextDisplay;
                linksElem.Add(orig);
            }
            else
            {
                linksElem.Add(BuildNewLink(link));
            }
        }
    }

    private static XElement BuildNewNode(NodeEditSnapshot snap) => new("FlowChartNode",
        new XAttribute(Xsi + "type", snap.IsPlayerChoice ? "PlayerResponseNode" : "TalkNode"),
        new XElement("NodeID",       snap.NodeId),
        new XElement("SpeakerGuid",  snap.SpeakerGuid),
        new XElement("ListenerGuid", snap.ListenerGuid),
        new XElement("Links"),
        new XElement("Conditionals", new XElement("Components")),
        new XElement("OnEnterScripts"),
        new XElement("OnExitScripts"),
        new XElement("OnUpdateScripts"),
        new XElement("DisplayType",    snap.DisplayType),
        new XElement("Persistence",    snap.Persistence),
        new XElement("ActorDirection", snap.ActorDirection),
        new XElement("Comments",       snap.Comments),
        new XElement("VOFilename",     snap.ExternalVO));

    private static XElement BuildNewLink(LinkEditSnapshot link) => new("FlowChartLink",
        new XElement("FromNodeID",              link.FromNodeId),
        new XElement("ToNodeID",                link.ToNodeId),
        new XElement("RandomWeight",            link.RandomWeight),
        new XElement("QuestionNodeTextDisplay", link.QuestionNodeTextDisplay),
        new XElement("Conditionals",            new XElement("Components")));

    private static void SetOrAdd(XElement parent, string name, string value)
    {
        var elem = parent.Element(name);
        if (elem is not null) elem.Value = value;
        else parent.Add(new XElement(name, value));
    }

    public static void SaveToFile(string path, ConversationEditSnapshot snapshot)
    {
        var original = File.ReadAllText(path);
        File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, Serialize(original, snapshot), Encoding.UTF8);
    }
}
```

- [ ] **Step 4: Run — confirm pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe1ConversationSerializerTests"
```

- [ ] **Step 5: Run all tests**

```
dotnet test DialogEditor.Tests
```

- [ ] **Step 6: Commit**

```
git add DialogEditor.Core/Serialization/Poe1ConversationSerializer.cs
git add DialogEditor.Tests/Serialization/Poe1ConversationSerializerTests.cs
git commit -m "feat: Poe1ConversationSerializer — in-place XML update"
```

---

### Task 10: IGameDataProvider.SaveConversation + both providers

Wire the serializers into the provider interface and both implementations.

**Files:**
- Modify: `DialogEditor.Core/GameData/IGameDataProvider.cs`
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`
- Modify: `DialogEditor.Core/GameData/Poe1GameDataProvider.cs`

- [ ] **Step 1: Add SaveConversation to IGameDataProvider**

```csharp
// DialogEditor.Core/GameData/IGameDataProvider.cs
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.GameData;

public interface IGameDataProvider
{
    string GameName { get; }
    IReadOnlyList<string> AvailableLanguages { get; }
    string Language { get; set; }
    IReadOnlyList<ConversationFile> EnumerateConversations();
    Conversation LoadConversation(ConversationFile file);
    IReadOnlyDictionary<string, string> LoadSpeakerNames();
    void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot);
}
```

- [ ] **Step 2: Implement in Poe2GameDataProvider**

Add at the top of `Poe2GameDataProvider.cs`:
```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Serialization;
```

Add the method:
```csharp
public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot)
{
    Poe2ConversationSerializer.SaveToFile(file.ConversationPath, snapshot);
    var stPath = StringTablePathFor(file.ConversationPath);
    Directory.CreateDirectory(Path.GetDirectoryName(stPath)!);
    StringTableSerializer.SaveToFile(stPath, snapshot.Nodes);
}
```

- [ ] **Step 3: Implement in Poe1GameDataProvider**

Add at the top of `Poe1GameDataProvider.cs`:
```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Serialization;
```

Add the method:
```csharp
public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot)
{
    Poe1ConversationSerializer.SaveToFile(file.ConversationPath, snapshot);
    var stPath = StringTablePathFor(file.ConversationPath);
    Directory.CreateDirectory(Path.GetDirectoryName(stPath)!);
    StringTableSerializer.SaveToFile(stPath, snapshot.Nodes);
}
```

- [ ] **Step 4: Run all tests**

```
dotnet test DialogEditor.Tests
```

Expected: all pass.

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/GameData/IGameDataProvider.cs
git add DialogEditor.Core/GameData/Poe2GameDataProvider.cs
git add DialogEditor.Core/GameData/Poe1GameDataProvider.cs
git commit -m "feat: IGameDataProvider.SaveConversation — wire serializers into both providers"
```

---
