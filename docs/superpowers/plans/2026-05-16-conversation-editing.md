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

## Part C — ViewModel Layer

---

### Task 11: Mutable NodeViewModel

`NodeViewModel` currently sets all properties once in the constructor and exposes them read-only. This task adds editable backing fields and command-generating setters for every property the user can change. Derived display strings (`Title`, `TextPreview`, `FooterText`, `SpeakerName`) become computed properties so they update automatically.

The `_undoStack` field starts as `null`. During `Load()` in `ConversationViewModel`, after construction the stack is wired up. Setters called before wiring (i.e., during construction) write directly to the backing field without creating commands.

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs`

No new test file — `NodeDetailViewModelTests.cs` already exercises the display side. ViewModel edit tests will be added in Task 13 alongside `ConversationViewModel`.

- [ ] **Step 1: Replace NodeViewModel with mutable version**

Replace the entire file:

```csharp
// DialogEditor.ViewModels/ViewModels/NodeViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class NodeViewModel : ObservableObject
{
    // ── Read-only identity ────────────────────────────────────────────────
    public int NodeId { get; }

    // ── Undo stack (wired after construction by ConversationViewModel) ───
    internal UndoRedoStack? UndoStack { get; set; }

    // ── Editable backing fields ───────────────────────────────────────────
    private bool   _isPlayerChoice;
    private SpeakerCategory _speakerCategory;
    private string _speakerGuid     = string.Empty;
    private string _listenerGuid    = string.Empty;
    private string _defaultText     = string.Empty;
    private string _femaleText      = string.Empty;
    private string _displayType     = string.Empty;
    private string _persistence     = string.Empty;
    private string _actorDirection  = string.Empty;
    private string _comments        = string.Empty;
    private string _externalVO      = string.Empty;
    private bool   _hasVO;
    private bool   _hideSpeaker;

    // ── Editable properties ───────────────────────────────────────────────
    public bool IsPlayerChoice
    {
        get => _isPlayerChoice;
        set => Push(ref _isPlayerChoice, value, "Edit node type",
            () => { OnPropertyChanged(nameof(Title)); });
    }

    public SpeakerCategory SpeakerCategory
    {
        get => _speakerCategory;
        set => Push(ref _speakerCategory, value, "Edit speaker category");
    }

    public string SpeakerGuid
    {
        get => _speakerGuid;
        set => Push(ref _speakerGuid, value, "Edit speaker GUID",
            () => OnPropertyChanged(nameof(SpeakerName)));
    }

    public string ListenerGuid
    {
        get => _listenerGuid;
        set => Push(ref _listenerGuid, value, "Edit listener GUID",
            () => OnPropertyChanged(nameof(ListenerName)));
    }

    public string DefaultText
    {
        get => _defaultText;
        set => Push(ref _defaultText, value, "Edit dialog text",
            () => OnPropertyChanged(nameof(TextPreview)));
    }

    public string FemaleText
    {
        get => _femaleText;
        set => Push(ref _femaleText, value, "Edit female text",
            () => { OnPropertyChanged(nameof(HasFemaleText)); OnPropertyChanged(nameof(FooterText)); });
    }

    public string DisplayType
    {
        get => _displayType;
        set => Push(ref _displayType, value, "Edit display type");
    }

    public string Persistence
    {
        get => _persistence;
        set => Push(ref _persistence, value, "Edit persistence");
    }

    public string ActorDirection
    {
        get => _actorDirection;
        set => Push(ref _actorDirection, value, "Edit actor direction");
    }

    public string Comments
    {
        get => _comments;
        set => Push(ref _comments, value, "Edit comments");
    }

    public string ExternalVO
    {
        get => _externalVO;
        set => Push(ref _externalVO, value, "Edit external VO");
    }

    public bool HasVO
    {
        get => _hasVO;
        set => Push(ref _hasVO, value, "Edit HasVO");
    }

    public bool HideSpeaker
    {
        get => _hideSpeaker;
        set => Push(ref _hideSpeaker, value, "Edit HideSpeaker");
    }

    // ── Computed display properties ───────────────────────────────────────
    public string SpeakerName =>
        SpeakerNameService.Resolve(_speakerGuid) ?? _speakerCategory switch
        {
            SpeakerCategory.Player   => Loc.Get("Speaker_Player"),
            SpeakerCategory.Narrator => Loc.Get("Speaker_Narrator"),
            SpeakerCategory.Script   => Loc.Get("Speaker_Script"),
            _                        => Loc.Get("Speaker_Unknown")
        };

    public string ListenerName =>
        SpeakerNameService.Resolve(_listenerGuid) ?? string.Empty;

    public string Title =>
        Loc.Format("Node_Title", NodeId, SpeakerName,
            _isPlayerChoice ? Loc.Get("Node_PlayerChoiceSuffix") : string.Empty);

    public string TextPreview =>
        _defaultText.Length > 80 ? _defaultText[..80] + "…" : _defaultText;

    public bool HasFemaleText => !string.IsNullOrEmpty(_femaleText);

    public string FooterText
    {
        get
        {
            var count = ConditionStrings.Count;
            var condPart = count > 0
                ? Loc.Format(count == 1 ? "Node_ConditionSingular" : "Node_ConditionPlural", count)
                : Loc.Get("Node_NoConditions");
            return HasFemaleText ? condPart + Loc.Get("Node_FemaleTextSuffix") : condPart;
        }
    }

    // ── Read-only (Phase 1 — no editing of conditions/scripts) ───────────
    public IReadOnlyList<string> ConditionStrings  { get; }
    public string ConditionExpression              { get; }
    public IReadOnlyList<string> Scripts           { get; }
    public IReadOnlyList<NodeLink> Links           { get; }

    // ── Nodify connector anchors ──────────────────────────────────────────
    public ConnectorViewModel Input   { get; } = new();
    public ConnectorViewModel Output  { get; } = new();
    public IReadOnlyList<ConnectorViewModel> Inputs  { get; }
    public IReadOnlyList<ConnectorViewModel> Outputs { get; }

    // ── Canvas state ──────────────────────────────────────────────────────
    [ObservableProperty] private LayoutPoint _location;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isSearchMatch = true;

    internal Action<NodeViewModel>? OnSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) OnSelected?.Invoke(this);
    }

    // ── Constructor ───────────────────────────────────────────────────────
    public NodeViewModel(ConversationNode node, StringEntry? entry)
    {
        NodeId          = node.NodeId;
        _isPlayerChoice = node.IsPlayerChoice;
        _speakerCategory = node.SpeakerCategory;
        _speakerGuid    = node.SpeakerGuid;
        _listenerGuid   = node.ListenerGuid;
        _displayType    = node.DisplayType;
        _persistence    = node.Persistence;
        _actorDirection = node.ActorDirection;
        _comments       = node.Comments;
        _externalVO     = node.ExternalVO;
        _hasVO          = node.HasVO;
        _hideSpeaker    = node.HideSpeaker;

        _defaultText    = entry?.DefaultText ?? Loc.Get("Node_TextUnavailable");
        _femaleText     = entry?.FemaleText  ?? string.Empty;

        ConditionStrings  = node.ConditionStrings;
        ConditionExpression = node.ConditionExpression;
        Scripts           = node.Scripts;
        Links             = node.Links;

        Inputs  = [Input];
        Outputs = [Output];
    }

    // ── Command-generating setter helper ──────────────────────────────────
    private void Push<T>(ref T field, T value, string description, Action? onApplied = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;

        if (UndoStack is null)
        {
            field = value;
            OnPropertyChanged(GetPropertyName<T>(description));
            onApplied?.Invoke();
            return;
        }

        var captured = field;  // capture current value before closure
        var fieldRef = new ValueHolder<T>(captured, value);

        UndoStack.Execute(new SetPropertyCommand<T>(
            description,
            apply: v =>
            {
                field = v;
                OnPropertyChanged(GetPropertyName<T>(description));
                onApplied?.Invoke();
            },
            oldValue: captured,
            newValue: value));
    }

    // Maps description prefix to property name for OnPropertyChanged
    private static string GetPropertyName<T>(string description) => description switch
    {
        "Edit dialog text"     => nameof(DefaultText),
        "Edit female text"     => nameof(FemaleText),
        "Edit node type"       => nameof(IsPlayerChoice),
        "Edit speaker category"=> nameof(SpeakerCategory),
        "Edit speaker GUID"    => nameof(SpeakerGuid),
        "Edit listener GUID"   => nameof(ListenerGuid),
        "Edit display type"    => nameof(DisplayType),
        "Edit persistence"     => nameof(Persistence),
        "Edit actor direction" => nameof(ActorDirection),
        "Edit comments"        => nameof(Comments),
        "Edit external VO"     => nameof(ExternalVO),
        "Edit HasVO"           => nameof(HasVO),
        "Edit HideSpeaker"     => nameof(HideSpeaker),
        _                      => string.Empty
    };

    // ── Snapshot helper ───────────────────────────────────────────────────
    public NodeEditSnapshot ToSnapshot(IReadOnlyList<LinkEditSnapshot> links) =>
        new(NodeId, _isPlayerChoice, _speakerCategory,
            _speakerGuid, _listenerGuid,
            _defaultText, _femaleText,
            _displayType, _persistence,
            _actorDirection, _comments, _externalVO,
            _hasVO, _hideSpeaker, links);
}

// Private helper — avoids closure-over-ref limitation
file sealed class ValueHolder<T>(T old, T @new) { public T Old = old; public T New = @new; }
```

> **Note on Push\<T\>:** the `ref T field` trick works because all calls to `Push` are in the same class and the lambda captures by reference to the instance's field. The `GetPropertyName` mapping is verbose but avoids storing the property name separately or using `[CallerMemberName]` (which wouldn't work through the helper). If this feels brittle, an alternative is to pass `nameof(...)` explicitly as a fourth parameter.

- [ ] **Step 2: Run all existing tests**

```
dotnet test DialogEditor.Tests
```

Expected: all pass. (The `NodeDetailViewModelTests` and `NodeDetailViewModel` code reads `NodeViewModel` properties — verify they still work.)

- [ ] **Step 3: Commit**

```
git add DialogEditor.ViewModels/ViewModels/NodeViewModel.cs
git commit -m "feat: NodeViewModel — mutable observable properties with undo stack wiring"
```

---

### Task 12: Structural Edit Commands

Four commands for adding/removing nodes and connections. These operate on `ConversationViewModel`'s observable collections.

**Files:**
- Create: `DialogEditor.ViewModels/Editing/AddNodeCommand.cs`
- Create: `DialogEditor.ViewModels/Editing/DeleteNodeCommand.cs`
- Create: `DialogEditor.ViewModels/Editing/AddConnectionCommand.cs`
- Create: `DialogEditor.ViewModels/Editing/DeleteConnectionCommand.cs`

No dedicated tests — these are exercised through `ConversationViewModelEditTests` in Task 13.

- [ ] **Step 1: AddNodeCommand**

```csharp
// DialogEditor.ViewModels/Editing/AddNodeCommand.cs
using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Editing;

internal sealed class AddNodeCommand(ConversationViewModel conversation, NodeViewModel node)
    : IEditCommand
{
    public string Description => $"Add node {node.NodeId}";
    public void Execute() => conversation.Nodes.Add(node);
    public void Undo()    => conversation.Nodes.Remove(node);
}
```

- [ ] **Step 2: DeleteNodeCommand**

```csharp
// DialogEditor.ViewModels/Editing/DeleteNodeCommand.cs
using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Editing;

internal sealed class DeleteNodeCommand(
    ConversationViewModel conversation,
    NodeViewModel node,
    IReadOnlyList<ConnectionViewModel> removedConnections) : IEditCommand
{
    public string Description => $"Delete node {node.NodeId}";

    public void Execute()
    {
        foreach (var c in removedConnections)
            conversation.Connections.Remove(c);
        conversation.Nodes.Remove(node);
        if (conversation.SelectedNode == node)
            conversation.SelectedNode = null;
    }

    public void Undo()
    {
        conversation.Nodes.Add(node);
        foreach (var c in removedConnections)
            conversation.Connections.Add(c);
    }
}
```

- [ ] **Step 3: AddConnectionCommand**

```csharp
// DialogEditor.ViewModels/Editing/AddConnectionCommand.cs
using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Editing;

internal sealed class AddConnectionCommand(
    ConversationViewModel conversation,
    ConnectionViewModel connection) : IEditCommand
{
    public string Description =>
        $"Add connection {connection.Source.GetNodeId()} → {connection.Target.GetNodeId()}";

    public void Execute() => conversation.Connections.Add(connection);
    public void Undo()    => conversation.Connections.Remove(connection);
}
```

- [ ] **Step 4: DeleteConnectionCommand**

```csharp
// DialogEditor.ViewModels/Editing/DeleteConnectionCommand.cs
using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Editing;

internal sealed class DeleteConnectionCommand(
    ConversationViewModel conversation,
    ConnectionViewModel connection) : IEditCommand
{
    public string Description =>
        $"Delete connection → {connection.Target.GetNodeId()}";

    public void Execute() => conversation.Connections.Remove(connection);
    public void Undo()    => conversation.Connections.Add(connection);
}
```

> `ConnectorViewModel.GetNodeId()` does not yet exist — it will be added in Task 13 as a helper.

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/Editing/AddNodeCommand.cs
git add DialogEditor.ViewModels/Editing/DeleteNodeCommand.cs
git add DialogEditor.ViewModels/Editing/AddConnectionCommand.cs
git add DialogEditor.ViewModels/Editing/DeleteConnectionCommand.cs
git commit -m "feat: structural edit commands — add/delete node and connection"
```

---

### Task 13: ConversationViewModel — edit operations

Wires the undo stack into `ConversationViewModel`, adds commands for structural editing, exposes `IsModified`, and provides the `BuildSnapshot()` method used by the save path.

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/ConnectorViewModel.cs`
- Create: `DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.ViewModels;

public class ConversationViewModelEditTests
{
    private static ConversationViewModel MakeVm() =>
        new(new StubDispatcher());

    private static NodeViewModel MakeNode(int id) =>
        new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);

    [Fact]
    public void AddNode_AppearsInNodes()
    {
        var vm = MakeVm();
        var node = MakeNode(5);
        vm.AddNode(node, new Core.Models.LayoutPoint(0, 0));
        Assert.Contains(node, vm.Nodes);
    }

    [Fact]
    public void AddNode_SetsIsModified()
    {
        var vm = MakeVm();
        vm.AddNode(MakeNode(1), new Core.Models.LayoutPoint(0, 0));
        Assert.True(vm.IsModified);
    }

    [Fact]
    public void UndoAddNode_RemovesNode()
    {
        var vm = MakeVm();
        var node = MakeNode(1);
        vm.AddNode(node, new Core.Models.LayoutPoint(0, 0));
        vm.Undo();
        Assert.DoesNotContain(node, vm.Nodes);
    }

    [Fact]
    public void DeleteNode_RemovesNodeAndItsConnections()
    {
        var vm   = MakeVm();
        var n1   = MakeNode(1);
        var n2   = MakeNode(2);
        vm.AddNode(n1, new Core.Models.LayoutPoint(0, 0));
        vm.AddNode(n2, new Core.Models.LayoutPoint(200, 0));
        vm.AddConnection(n1.Output, n2.Input);
        vm.DeleteNode(n1);
        Assert.DoesNotContain(n1, vm.Nodes);
        Assert.Empty(vm.Connections);
    }

    [Fact]
    public void UndoDeleteNode_RestoresNodeAndConnections()
    {
        var vm = MakeVm();
        var n1 = MakeNode(1);
        var n2 = MakeNode(2);
        vm.AddNode(n1, new Core.Models.LayoutPoint(0, 0));
        vm.AddNode(n2, new Core.Models.LayoutPoint(200, 0));
        vm.AddConnection(n1.Output, n2.Input);
        vm.DeleteNode(n1);
        vm.Undo();
        Assert.Contains(n1, vm.Nodes);
        Assert.Single(vm.Connections);
    }

    [Fact]
    public void Load_ClearsIsModified()
    {
        var vm = MakeVm();
        vm.AddNode(MakeNode(1), new Core.Models.LayoutPoint(0, 0));
        Assert.True(vm.IsModified);
        vm.Load(new Conversation("test", [], Core.Models.StringTable.Empty));
        Assert.False(vm.IsModified);
    }
}
```

- [ ] **Step 2: Run — confirm failure**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewModelEditTests"
```

- [ ] **Step 3: Add GetNodeId helper to ConnectorViewModel**

```csharp
// DialogEditor.ViewModels/ViewModels/ConnectorViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;

namespace DialogEditor.ViewModels;

public partial class ConnectorViewModel : ObservableObject
{
    [ObservableProperty]
    private LayoutPoint _anchor;

    internal NodeViewModel? Owner { get; set; }
    internal int GetNodeId() => Owner?.NodeId ?? -1;
}
```

- [ ] **Step 4: Update ConversationViewModel**

Add the following to `ConversationViewModel.cs`. Keep the existing search, `Load`, `Nodes`, `Connections`, `SelectedNode` members unchanged — add these new members:

```csharp
// At the top — new usings
using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Editing;

// New fields
private readonly UndoRedoStack _undoStack = new();
private ConversationFile? _currentFile;

// New properties
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CanUndo))]
[NotifyPropertyChangedFor(nameof(UndoDescription))]
[NotifyPropertyChangedFor(nameof(CanRedo))]
[NotifyPropertyChangedFor(nameof(RedoDescription))]
private bool _isModified;

public bool CanUndo        => _undoStack.CanUndo;
public bool CanRedo        => _undoStack.CanRedo;
public string? UndoDescription => _undoStack.UndoDescription;
public string? RedoDescription => _undoStack.RedoDescription;

// Update Load() to wire the undo stack and clear IsModified
// In the existing Load() method, after populating Nodes, add:
//     foreach (var vm in Nodes) vm.UndoStack = _undoStack;
// Also add at the start of Load():
//     _undoStack.Clear();
//     IsModified = false;
// (Modify the existing Load method rather than duplicating it)

// New relay commands
[RelayCommand(CanExecute = nameof(CanUndo))]
private void Undo()
{
    _undoStack.Undo();
    IsModified = _undoStack.CanUndo;
    OnPropertyChanged(nameof(CanUndo));
    OnPropertyChanged(nameof(CanRedo));
    OnPropertyChanged(nameof(UndoDescription));
    OnPropertyChanged(nameof(RedoDescription));
}

[RelayCommand(CanExecute = nameof(CanRedo))]
private void Redo()
{
    _undoStack.Redo();
    IsModified = true;
    OnPropertyChanged(nameof(CanUndo));
    OnPropertyChanged(nameof(CanRedo));
    OnPropertyChanged(nameof(UndoDescription));
    OnPropertyChanged(nameof(RedoDescription));
}

// New structural edit methods
public void AddNode(NodeViewModel node, LayoutPoint position)
{
    node.Location  = position;
    node.UndoStack = _undoStack;
    node.OnSelected = n => SelectedNode = n;
    var cmd = new AddNodeCommand(this, node);
    _undoStack.Execute(cmd);
    IsModified = true;
    RefreshUndoRedo();
}

public void DeleteNode(NodeViewModel node)
{
    var removed = Connections
        .Where(c => c.Source.Owner == node || c.Target.Owner == node)
        .ToList();
    var cmd = new DeleteNodeCommand(this, node, removed);
    _undoStack.Execute(cmd);
    IsModified = true;
    RefreshUndoRedo();
}

public void AddConnection(ConnectorViewModel source, ConnectorViewModel target)
{
    var conn = new ConnectionViewModel(source, target);
    var cmd  = new AddConnectionCommand(this, conn);
    _undoStack.Execute(cmd);
    IsModified = true;
    RefreshUndoRedo();
}

public void DeleteConnection(ConnectionViewModel connection)
{
    var cmd = new DeleteConnectionCommand(this, connection);
    _undoStack.Execute(cmd);
    IsModified = true;
    RefreshUndoRedo();
}

public void AddConnectedNode(NodeViewModel parent, LayoutPoint position)
{
    var newId   = NodeIdAllocator.Next(Nodes.Select(n => n.NodeId));
    var newNode = new ConversationNode(newId, false, SpeakerCategory.Npc,
        parent.SpeakerGuid, parent.ListenerGuid, [], [], [],
        parent.DisplayType, parent.Persistence);
    var vm = new NodeViewModel(newNode, null) { Location = position };
    vm.UndoStack  = _undoStack;
    vm.OnSelected = n => SelectedNode = n;
    _undoStack.Execute(new AddNodeCommand(this, vm));
    _undoStack.Execute(new AddConnectionCommand(this, new ConnectionViewModel(parent.Output, vm.Input)));
    IsModified = true;
    RefreshUndoRedo();
    SelectedNode = vm;
}

private void RefreshUndoRedo()
{
    OnPropertyChanged(nameof(CanUndo));
    OnPropertyChanged(nameof(CanRedo));
    OnPropertyChanged(nameof(UndoDescription));
    OnPropertyChanged(nameof(RedoDescription));
}

// Snapshot for save
public ConversationEditSnapshot BuildSnapshot() =>
    new(Nodes.Select(n =>
    {
        var links = Connections
            .Where(c => c.Source.Owner == n)
            .Select(c => new LinkEditSnapshot(
                n.NodeId,
                c.Target.Owner!.NodeId,
                1f,
                c.QuestionNodeTextDisplay,
                c.HasConditions))
            .ToList();
        return n.ToSnapshot(links);
    }).ToList());
```

Also update `Load()` to:
1. Add `_undoStack.Clear(); IsModified = false;` at the top.
2. After `vm.OnSelected = n => SelectedNode = n;`, add `vm.UndoStack = _undoStack;`.
3. After building `nodeMap`, set `vm.Input.Owner = vm; vm.Output.Owner = vm;` for each vm.

And add `ConnectionViewModel.HasConditions` property (returns false for now — links added by the editor never have conditions):
```csharp
// In ConnectionViewModel.cs — add:
public bool HasConditions => false;
```

- [ ] **Step 5: Run tests — confirm pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewModelEditTests"
dotnet test DialogEditor.Tests
```

- [ ] **Step 6: Commit**

```
git add DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs
git add DialogEditor.ViewModels/ViewModels/ConnectorViewModel.cs
git add DialogEditor.ViewModels/ViewModels/ConnectionViewModel.cs
git add DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs
git commit -m "feat: ConversationViewModel — undo/redo, add/delete node and connection, BuildSnapshot"
```

---

### Task 14: NodeDetailViewModel — editable proxy properties

`NodeDetailViewModel` gains a reference to the current `NodeViewModel` and exposes proxy properties for editable fields. Changes in the UI write through to `NodeViewModel`'s setters (which fire commands into the undo stack). Undo/redo changes on `NodeViewModel` are forwarded back to the UI via `PropertyChanged` subscription.

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`
- Modify: `DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs`

- [ ] **Step 1: Write new failing tests**

Add to `NodeDetailViewModelTests.cs`:

```csharp
[Fact]
public void Load_ExposesSpeakerGuid()
{
    var vm   = new NodeDetailViewModel();
    var node = MakeNode(0, speakerGuid: "test-guid-123");
    vm.Load(node);
    Assert.Equal("test-guid-123", vm.SpeakerGuid);
}

[Fact]
public void SetSpeakerGuid_UpdatesNodeViewModel()
{
    var vm   = new NodeDetailViewModel();
    var node = MakeNode(0, speakerGuid: "original");
    vm.Load(node);
    vm.SpeakerGuid = "updated";
    Assert.Equal("updated", node.SpeakerGuid);
}

[Fact]
public void Clear_NullifiesCurrentNode()
{
    var vm = new NodeDetailViewModel();
    vm.Load(MakeNode(0));
    vm.Clear();
    Assert.Equal(string.Empty, vm.SpeakerGuid);
    Assert.False(vm.HasContent);
}
```

Where `MakeNode` is a helper in the test file:

```csharp
private static NodeViewModel MakeNode(int id, string speakerGuid = "") =>
    new(new ConversationNode(id, false, SpeakerCategory.Npc, speakerGuid, "",
        [], [], [], "Conversation", "None"), null);
```

- [ ] **Step 2: Run — confirm failure**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeDetailViewModelTests"
```

- [ ] **Step 3: Update NodeDetailViewModel**

Replace the file content:

```csharp
// DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Models;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

public partial class NodeDetailViewModel : ObservableObject
{
    private NodeViewModel? _node;

    [ObservableProperty] private bool _hasContent;

    // ── Editable proxy properties ─────────────────────────────────────────
    public string DefaultText
    {
        get => _node?.DefaultText ?? string.Empty;
        set { if (_node != null) _node.DefaultText = value; }
    }

    public string FemaleText
    {
        get => _node?.FemaleText ?? string.Empty;
        set { if (_node != null) _node.FemaleText = value; }
    }

    public bool IsPlayerChoice
    {
        get => _node?.IsPlayerChoice ?? false;
        set { if (_node != null) _node.IsPlayerChoice = value; }
    }

    public string SpeakerGuid
    {
        get => _node?.SpeakerGuid ?? string.Empty;
        set { if (_node != null) _node.SpeakerGuid = value; }
    }

    public string ListenerGuid
    {
        get => _node?.ListenerGuid ?? string.Empty;
        set { if (_node != null) _node.ListenerGuid = value; }
    }

    public string DisplayType
    {
        get => _node?.DisplayType ?? string.Empty;
        set { if (_node != null) _node.DisplayType = value; }
    }

    public string Persistence
    {
        get => _node?.Persistence ?? string.Empty;
        set { if (_node != null) _node.Persistence = value; }
    }

    public string ActorDirection
    {
        get => _node?.ActorDirection ?? string.Empty;
        set { if (_node != null) _node.ActorDirection = value; }
    }

    public string Comments
    {
        get => _node?.Comments ?? string.Empty;
        set { if (_node != null) _node.Comments = value; }
    }

    public string ExternalVO
    {
        get => _node?.ExternalVO ?? string.Empty;
        set { if (_node != null) _node.ExternalVO = value; }
    }

    public bool HasVO
    {
        get => _node?.HasVO ?? false;
        set { if (_node != null) _node.HasVO = value; }
    }

    public bool HideSpeaker
    {
        get => _node?.HideSpeaker ?? false;
        set { if (_node != null) _node.HideSpeaker = value; }
    }

    // ── Read-only display ─────────────────────────────────────────────────
    public string FemaleTextDisplay =>
        (_node?.HasFemaleText ?? false)
            ? (_node!.FemaleText)
            : Loc.Get("NodeDetail_SameAsDefault");

    public bool HasFemaleText => _node?.HasFemaleText ?? false;

    [ObservableProperty] private IReadOnlyList<PropertyGroup> _propertyGroups = [];
    [ObservableProperty] private IReadOnlyList<LinkRow> _links = [];

    // ── Load / Clear ──────────────────────────────────────────────────────
    public void Load(NodeViewModel? node)
    {
        if (_node is not null)
            _node.PropertyChanged -= OnNodePropertyChanged;

        _node = node;
        if (node is null) { HasContent = false; return; }

        node.PropertyChanged += OnNodePropertyChanged;

        RefreshReadOnlyGroups(node);
        Links      = node.Links.Select(BuildLinkRow).ToList();
        HasContent = true;

        // Notify all proxy properties
        NotifyAllProxies();
    }

    public void Clear() => Load(null);

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward every change on the node to this VM so the UI updates on undo/redo
        NotifyAllProxies();
        if (_node is not null)
            RefreshReadOnlyGroups(_node);
    }

    private void NotifyAllProxies()
    {
        OnPropertyChanged(nameof(DefaultText));
        OnPropertyChanged(nameof(FemaleText));
        OnPropertyChanged(nameof(FemaleTextDisplay));
        OnPropertyChanged(nameof(HasFemaleText));
        OnPropertyChanged(nameof(IsPlayerChoice));
        OnPropertyChanged(nameof(SpeakerGuid));
        OnPropertyChanged(nameof(ListenerGuid));
        OnPropertyChanged(nameof(DisplayType));
        OnPropertyChanged(nameof(Persistence));
        OnPropertyChanged(nameof(ActorDirection));
        OnPropertyChanged(nameof(Comments));
        OnPropertyChanged(nameof(ExternalVO));
        OnPropertyChanged(nameof(HasVO));
        OnPropertyChanged(nameof(HideSpeaker));
    }

    private void RefreshReadOnlyGroups(NodeViewModel node)
    {
        var none = Loc.Get("NodeDetail_None");
        PropertyGroups =
        [
            new PropertyGroup(Loc.Get("Label_GroupIdentity"),
            [
                new PropertyRow(Loc.Get("PropertyRow_NodeId"), node.NodeId.ToString()),
            ]),
            new PropertyGroup(Loc.Get("Label_GroupLogic"),
            [
                new PropertyRow(Loc.Get("PropertyRow_Conditions"),
                    string.IsNullOrEmpty(node.ConditionExpression) ? none : node.ConditionExpression,
                    PropertyValueStyle.Condition),
                new PropertyRow(Loc.Get("PropertyRow_Scripts"),
                    node.Scripts.Count == 0 ? none : string.Join(Environment.NewLine, node.Scripts),
                    PropertyValueStyle.Script),
            ]),
        ];
    }

    private static LinkRow BuildLinkRow(NodeLink link)
    {
        var extras = new List<string>();
        if (link.RandomWeight != 1f)
            extras.Add($"{Loc.Get("Link_WeightPrefix")}{link.RandomWeight:0.##}");
        if (!string.IsNullOrEmpty(link.QuestionNodeTextDisplay) && link.QuestionNodeTextDisplay != "ShowOnce")
            extras.Add(link.QuestionNodeTextDisplay);
        var arrow  = $"{Loc.Get("Link_Arrow")} {link.ToNodeId}";
        var detail = extras.Count == 0 ? Loc.Get("NodeDetail_None") : $"[{string.Join(", ", extras)}]";
        return new LinkRow(arrow, detail);
    }
}
```

- [ ] **Step 4: Run tests**

```
dotnet test DialogEditor.Tests
```

Expected: all pass.

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs
git add DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs
git commit -m "feat: NodeDetailViewModel — editable proxy properties with undo-forwarding"
```

---

### Task 15: MainWindowViewModel — save, backup, dirty title, unsaved-changes guard

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`

No new tests — this is thin orchestration glue; the underlying pieces are all tested.

- [ ] **Step 1: Add save command**

In `MainWindowViewModel`, add:

```csharp
// New field
private string _currentGameDirectory = string.Empty;

// New observable properties
[ObservableProperty] private bool _isModified;

// Window title updates when conversation name or modified state changes
public string WindowTitle =>
    _isModified && CurrentConversationName is not null
        ? $"● {CurrentConversationName}"
        : (CurrentConversationName ?? Loc.Get("App_Title"));

// Wire IsModified from Canvas
// In the existing Canvas.PropertyChanged subscription, add:
//   case nameof(ConversationViewModel.IsModified):
//       IsModified = Canvas.IsModified;
//       OnPropertyChanged(nameof(WindowTitle));
//       break;

[RelayCommand(CanExecute = nameof(CanSave))]
private void Save()
{
    if (_provider is null || _currentFile is null) return;
    try
    {
        var snapshot = Canvas.BuildSnapshot();
        _provider.SaveConversation(_currentFile, snapshot);
        Canvas.IsModified = false;
        IsModified = false;
        OnPropertyChanged(nameof(WindowTitle));
        AppLog.Info($"Saved {_currentFile.Name}");
        StatusText = Loc.Format("Status_Saved", _currentFile.Name);
    }
    catch (Exception ex)
    {
        AppLog.Error($"Failed to save '{_currentFile?.Name}'", ex);
        StatusText = Loc.Format("Status_SaveError", _currentFile!.Name, ex.Message);
    }
}
private bool CanSave() => _provider is not null && _currentFile is not null && IsModified;
```

- [ ] **Step 2: Add backup offer to LoadDirectory**

In `LoadDirectory`, after the provider is set up and before calling `Browser.Load`, add:

```csharp
_currentGameDirectory = path;
if (!AppSettings.IsKnownGameDirectory(path))
    _ = OfferBackupAsync(path);
AppSettings.MarkGameDirectoryKnown(path);
```

Add the async method:

```csharp
private async Task OfferBackupAsync(string gameDirectory)
{
    var pick = await _folderPicker.PickFolderAsync(Loc.Get("Dialog_SelectBackupFolder"));
    if (pick is null) return;

    var timestamp  = DateTime.Now.ToString("yyyy-MM-ddTHH-mm");
    var backupRoot = Path.Combine(pick, timestamp);
    AppSettings.SetBackupPath(gameDirectory, pick);

    StatusText = Loc.Get("Status_BackupInProgress");
    try
    {
        if (_provider is Poe2GameDataProvider p2)
        {
            await BackupService.BackupAsync(p2.ConversationsRoot, Path.Combine(backupRoot, "conversations"), default);
            await BackupService.BackupAsync(p2.StringTablesRoot,  Path.Combine(backupRoot, "stringtables"),  default);
        }
        else if (_provider is Poe1GameDataProvider p1)
        {
            await BackupService.BackupAsync(p1.ConversationsRoot, Path.Combine(backupRoot, "conversations"), default);
            await BackupService.BackupAsync(p1.StringTablesRoot,  Path.Combine(backupRoot, "stringtables"),  default);
        }
        AppLog.Info($"Backup written to {backupRoot}");
        StatusText = Loc.Format("Status_BackupComplete", backupRoot);
    }
    catch (Exception ex)
    {
        AppLog.Error("Backup failed", ex);
        StatusText = Loc.Format("Status_BackupError", ex.Message);
    }
}
```

> Note: `ConversationsRoot` and `StringTablesRoot` are currently private in both providers. Make them `internal` so `MainWindowViewModel` can access them, OR expose a `BackupRoots` tuple on the interface. The simpler path is making the properties `internal`.

- [ ] **Step 3: Add restore command**

```csharp
[RelayCommand]
private async Task RestoreBackup()
{
    var backupPath = AppSettings.GetBackupPath(_currentGameDirectory);
    if (backupPath is null)
    {
        StatusText = Loc.Get("Status_NoBackupFound");
        return;
    }
    // The unsaved-changes prompt is handled by the unsaved-changes guard (step 4)
    StatusText = Loc.Get("Status_RestoreInProgress");
    try
    {
        if (_provider is Poe2GameDataProvider p2)
        {
            await BackupService.RestoreAsync(
                Path.Combine(backupPath, "conversations"), p2.ConversationsRoot, default);
            await BackupService.RestoreAsync(
                Path.Combine(backupPath, "stringtables"),  p2.StringTablesRoot,  default);
        }
        else if (_provider is Poe1GameDataProvider p1)
        {
            await BackupService.RestoreAsync(
                Path.Combine(backupPath, "conversations"), p1.ConversationsRoot, default);
            await BackupService.RestoreAsync(
                Path.Combine(backupPath, "stringtables"),  p1.StringTablesRoot,  default);
        }
        AppLog.Info("Backup restored");
        StatusText = Loc.Get("Status_RestoreComplete");
        if (_currentFile is not null)
            OnConversationSelected(_currentFile);
    }
    catch (Exception ex)
    {
        AppLog.Error("Restore failed", ex);
        StatusText = Loc.Format("Status_RestoreError", ex.Message);
    }
}
```

- [ ] **Step 4: Unsaved-changes guard in OnConversationSelected**

Before loading the new conversation in `OnConversationSelected`, check `IsModified` and prompt:

```csharp
private void OnConversationSelected(ConversationFile file)
{
    if (_provider is null) return;

    if (IsModified && _currentFile is not null)
    {
        // Prompt is handled by the View layer via a pending action pattern.
        // Store the pending file and raise a request for confirmation.
        _pendingFile = file;
        UnsavedChangesRequested?.Invoke();
        return;
    }

    LoadConversation(file);
}

private ConversationFile? _pendingFile;
public event Action? UnsavedChangesRequested;

// Called by the View after the user chooses Save
public void SaveAndProceed()
{
    Save();
    if (_pendingFile is not null) LoadConversation(_pendingFile);
    _pendingFile = null;
}

// Called by the View after the user chooses Discard
public void DiscardAndProceed()
{
    if (_pendingFile is not null) LoadConversation(_pendingFile);
    _pendingFile = null;
}

// Called by the View after the user chooses Cancel
public void CancelPendingNavigation() => _pendingFile = null;

private void LoadConversation(ConversationFile file)
{
    // Extract the body of the existing OnConversationSelected (the try/catch block)
}
```

- [ ] **Step 5: Run all tests**

```
dotnet test DialogEditor.Tests
```

Expected: all pass.

- [ ] **Step 6: Commit**

```
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat: MainWindowViewModel — save, backup offer, restore, dirty title, unsaved-changes guard"
```

---

### Task 16: Strings.axaml — all new localisation keys

All user-visible strings for the editing features must be defined here before the UI tasks reference them.

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

- [ ] **Step 1: Add new keys**

Append the following sections inside the `<ResourceDictionary>` (before the closing tag):

```xml
    <!-- ─── Editing — toolbar buttons ───────────────────────────────────── -->
    <sys:String x:Key="Button_Undo">↩</sys:String>
    <sys:String x:Key="Button_Redo">↪</sys:String>
    <sys:String x:Key="Button_Save">Save</sys:String>
    <sys:String x:Key="Button_RestoreBackup">Restore Backup…</sys:String>
    <!-- {0} = description of the action that will be undone -->
    <sys:String x:Key="ToolTip_Undo">Undo: {0} (Ctrl+Z)</sys:String>
    <sys:String x:Key="ToolTip_Undo_NoHistory">Nothing to undo (Ctrl+Z)</sys:String>
    <sys:String x:Key="ToolTip_Redo">Redo: {0} (Ctrl+Y)</sys:String>
    <sys:String x:Key="ToolTip_Redo_NoHistory">Nothing to redo (Ctrl+Y)</sys:String>
    <sys:String x:Key="ToolTip_Save">Save conversation to disk (Ctrl+S)</sys:String>
    <sys:String x:Key="ToolTip_RestoreBackup">Restore all conversation files from the original backup</sys:String>

    <!-- ─── Editing — canvas context menus ──────────────────────────────── -->
    <sys:String x:Key="Menu_DeleteNode">Delete node</sys:String>
    <sys:String x:Key="Menu_AddConnectedNode">Add connected node</sys:String>
    <sys:String x:Key="Menu_DeleteConnection">Delete connection</sys:String>

    <!-- ─── Editing — status messages ───────────────────────────────────── -->
    <!-- {0} = conversation name -->
    <sys:String x:Key="Status_Saved">Saved: {0}</sys:String>
    <!-- {0} = conversation name, {1} = error message -->
    <sys:String x:Key="Status_SaveError">Save failed for {0}: {1}</sys:String>
    <sys:String x:Key="Status_BackupInProgress">Backing up conversation files…</sys:String>
    <!-- {0} = backup folder path -->
    <sys:String x:Key="Status_BackupComplete">Backup complete — {0}</sys:String>
    <!-- {0} = error message -->
    <sys:String x:Key="Status_BackupError">Backup failed: {0}</sys:String>
    <sys:String x:Key="Status_RestoreInProgress">Restoring from backup…</sys:String>
    <sys:String x:Key="Status_RestoreComplete">Restore complete — reload to see changes.</sys:String>
    <!-- {0} = error message -->
    <sys:String x:Key="Status_RestoreError">Restore failed: {0}</sys:String>
    <sys:String x:Key="Status_NoBackupFound">No backup found for this game folder.</sys:String>

    <!-- ─── Editing — dialogs ────────────────────────────────────────────── -->
    <sys:String x:Key="Dialog_SelectBackupFolder">Select backup destination folder</sys:String>

    <!-- ─── Editing — unsaved-changes prompt ────────────────────────────── -->
    <sys:String x:Key="UnsavedChanges_Message">You have unsaved changes. Save before switching conversations?</sys:String>
    <sys:String x:Key="UnsavedChanges_Save">Save</sys:String>
    <sys:String x:Key="UnsavedChanges_Discard">Discard</sys:String>
    <sys:String x:Key="UnsavedChanges_Cancel">Cancel</sys:String>

    <!-- ─── Detail panel — editable field labels (new rows) ─────────────── -->
    <sys:String x:Key="PropertyRow_ListenerGuid">Listener GUID</sys:String>
    <sys:String x:Key="Label_GroupEditable">PROPERTIES</sys:String>

    <!-- ─── Detail panel — combobox option values ───────────────────────── -->
    <sys:String x:Key="Option_NpcLine">NPC Line</sys:String>
    <sys:String x:Key="Option_PlayerChoice">Player Choice</sys:String>
    <sys:String x:Key="Option_DisplayConversation">Conversation</sys:String>
    <sys:String x:Key="Option_DisplayBark">Bark</sys:String>
    <sys:String x:Key="Option_PersistenceNone">None</sys:String>
    <sys:String x:Key="Option_PersistenceOnceEver">OnceEver</sys:String>

    <!-- ─── Detail panel — link actions ─────────────────────────────────── -->
    <sys:String x:Key="Button_DeleteLink">✕</sys:String>
    <sys:String x:Key="ToolTip_DeleteLink">Remove this connection</sys:String>
    <sys:String x:Key="Button_AddLink">Add link…</sys:String>
    <sys:String x:Key="ToolTip_AddLink">Add a connection from this node to another</sys:String>
```

- [ ] **Step 2: Build to confirm no typos**

```
dotnet build DialogEditor.Avalonia
```

- [ ] **Step 3: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: Strings.axaml — localisation keys for all editing UI"
```

---

## Part D — UI Layer

No tests in this part — these are XAML and code-behind changes. After each task, manually verify the feature in the running application.

---

### Task 17: NodeDetailView.axaml — editable controls

Replace the read-only detail panel with a mix of editable controls. The `NodeDetailViewModel` proxy properties are the binding targets throughout.

**Files:**
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml`

- [ ] **Step 1: Replace NodeDetailView.axaml**

Replace the entire file with the following. Key changes from the old version:
- DefaultText and FemaleText become multi-line `TextBox` controls
- Node type, display type, persistence become `ComboBox`
- Speaker/listener GUIDs, actor direction, comments, external VO become `TextBox`
- HasVO and HideSpeaker become `CheckBox`
- Conditions/scripts remain read-only `TextBlock`
- Links section gains a Delete button per row and an Add link button

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:models="clr-namespace:DialogEditor.ViewModels.Models;assembly=DialogEditor.ViewModels"
             x:Class="DialogEditor.Avalonia.Views.NodeDetailView" x:CompileBindings="False">

    <UserControl.Styles>
        <Style Selector="TextBlock.group-header">
            <Setter Property="Foreground" Value="#888"/>
            <Setter Property="FontSize"   Value="8"/>
            <Setter Property="FontWeight" Value="Bold"/>
            <Setter Property="Margin"     Value="0,10,0,3"/>
        </Style>
        <Style Selector="TextBox.detail-field">
            <Setter Property="Background"   Value="#1a1a1a"/>
            <Setter Property="Foreground"   Value="#e8e8e8"/>
            <Setter Property="BorderBrush"  Value="#444"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="FontSize"     Value="10"/>
            <Setter Property="Padding"      Value="4,3"/>
            <Setter Property="Margin"       Value="0,0,0,6"/>
        </Style>
        <Style Selector="ComboBox.detail-combo">
            <Setter Property="Background"   Value="#1a1a1a"/>
            <Setter Property="Foreground"   Value="#e8e8e8"/>
            <Setter Property="BorderBrush"  Value="#444"/>
            <Setter Property="FontSize"     Value="10"/>
            <Setter Property="Padding"      Value="4,2"/>
            <Setter Property="Margin"       Value="0,0,0,6"/>
            <Setter Property="HorizontalAlignment" Value="Stretch"/>
        </Style>
        <Style Selector="CheckBox.detail-check">
            <Setter Property="Foreground"   Value="#ccc"/>
            <Setter Property="FontSize"     Value="10"/>
            <Setter Property="Margin"       Value="0,0,0,6"/>
        </Style>
    </UserControl.Styles>

    <UserControl.Resources>
        <DataTemplate x:Key="ReadOnlyRowTemplate" DataType="models:PropertyRow">
            <Grid ColumnDefinitions="110,*" Margin="0,1,0,1">
                <TextBlock Grid.Column="0" Text="{Binding Label}"
                           Foreground="#999" FontSize="10" VerticalAlignment="Top"/>
                <TextBlock Grid.Column="1" Text="{Binding Value}"
                           Foreground="{Binding Style, Converter={StaticResource PropertyValueStyleToBrush}}"
                           FontSize="10" TextWrapping="Wrap" LineHeight="15"/>
            </Grid>
        </DataTemplate>
    </UserControl.Resources>

    <Grid>
        <TextBlock Text="{StaticResource Label_SelectNode}"
                   Foreground="#555" FontSize="11"
                   HorizontalAlignment="Center" VerticalAlignment="Center"
                   IsVisible="{Binding HasContent, Converter={StaticResource InverseBoolToVis}}"/>

        <ScrollViewer Background="#2d2d2d" IsVisible="{Binding HasContent}">
            <StackPanel Margin="8">

                <!-- ── Default / Male text ─────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{StaticResource Label_DefaultMaleText}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding DefaultText, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         AcceptsReturn="True" TextWrapping="Wrap" MinHeight="50"
                         ToolTip.Tip="Edit the default / male dialogue text"/>

                <!-- ── Female text ─────────────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{StaticResource Label_FemaleText}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding FemaleText, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         AcceptsReturn="True" TextWrapping="Wrap" MinHeight="40"
                         Watermark="(same as default — leave blank)"
                         ToolTip.Tip="Optional female-voice variant text"/>

                <!-- ── Identity ────────────────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{StaticResource Label_GroupIdentity}"/>

                <TextBlock Text="{StaticResource PropertyRow_Type}"
                           Foreground="#999" FontSize="9" Margin="0,0,0,2"/>
                <ComboBox Classes="detail-combo"
                          SelectedIndex="{Binding IsPlayerChoice, Mode=TwoWay,
                              Converter={StaticResource BoolToIndexConverter}}"
                          ToolTip.Tip="Whether this is an NPC line or a player response choice">
                    <ComboBoxItem Content="{StaticResource Option_NpcLine}"/>
                    <ComboBoxItem Content="{StaticResource Option_PlayerChoice}"/>
                </ComboBox>

                <TextBlock Text="{StaticResource PropertyRow_SpeakerGuid}"
                           Foreground="#999" FontSize="9" Margin="0,0,0,2"/>
                <TextBox Classes="detail-field"
                         Text="{Binding SpeakerGuid, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         FontFamily="Consolas,Courier New,monospace"
                         ToolTip.Tip="GUID of the speaking character"/>

                <TextBlock Text="{StaticResource PropertyRow_ListenerGuid}"
                           Foreground="#999" FontSize="9" Margin="0,0,0,2"/>
                <TextBox Classes="detail-field"
                         Text="{Binding ListenerGuid, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         FontFamily="Consolas,Courier New,monospace"
                         ToolTip.Tip="GUID of the listening character"/>

                <!-- ── Display ─────────────────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{StaticResource Label_GroupDisplay}"/>

                <TextBlock Text="{StaticResource PropertyRow_DisplayType}"
                           Foreground="#999" FontSize="9" Margin="0,0,0,2"/>
                <ComboBox Classes="detail-combo"
                          SelectedIndex="{Binding DisplayType, Mode=TwoWay,
                              Converter={StaticResource DisplayTypeToIndexConverter}}"
                          ToolTip.Tip="How this node is presented to the player">
                    <ComboBoxItem Content="{StaticResource Option_DisplayConversation}"/>
                    <ComboBoxItem Content="{StaticResource Option_DisplayBark}"/>
                </ComboBox>

                <TextBlock Text="{StaticResource PropertyRow_Persistence}"
                           Foreground="#999" FontSize="9" Margin="0,0,0,2"/>
                <ComboBox Classes="detail-combo"
                          SelectedIndex="{Binding Persistence, Mode=TwoWay,
                              Converter={StaticResource PersistenceToIndexConverter}}"
                          ToolTip.Tip="Whether the node is hidden after it has been shown once">
                    <ComboBoxItem Content="{StaticResource Option_PersistenceNone}"/>
                    <ComboBoxItem Content="{StaticResource Option_PersistenceOnceEver}"/>
                </ComboBox>

                <TextBlock Text="{StaticResource PropertyRow_ActorDirection}"
                           Foreground="#999" FontSize="9" Margin="0,0,0,2"/>
                <TextBox Classes="detail-field"
                         Text="{Binding ActorDirection, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         ToolTip.Tip="Optional actor direction note for the VO recording"/>

                <!-- ── Voice ───────────────────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{StaticResource Label_GroupVoice}"/>

                <TextBlock Text="{StaticResource PropertyRow_ExternalVO}"
                           Foreground="#999" FontSize="9" Margin="0,0,0,2"/>
                <TextBox Classes="detail-field"
                         Text="{Binding ExternalVO, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         FontFamily="Consolas,Courier New,monospace"
                         ToolTip.Tip="External voice-over file path or identifier"/>

                <CheckBox Classes="detail-check"
                          IsChecked="{Binding HasVO, Mode=TwoWay}"
                          Content="{StaticResource PropertyRow_HasVO}"
                          ToolTip.Tip="Whether a voice-over recording exists for this node"/>

                <CheckBox Classes="detail-check"
                          IsChecked="{Binding HideSpeaker, Mode=TwoWay}"
                          Content="{StaticResource PropertyRow_HideSpeaker}"
                          ToolTip.Tip="Whether the speaker name is hidden during playback"/>

                <!-- ── Comments ────────────────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{StaticResource PropertyRow_Comments}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding Comments, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         AcceptsReturn="True" TextWrapping="Wrap" MinHeight="30"
                         ToolTip.Tip="Internal developer notes (not shown to players)"/>

                <!-- ── Read-only groups (conditions, scripts, node ID) ─── -->
                <ItemsControl ItemsSource="{Binding PropertyGroups}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel>
                                <TextBlock Classes="group-header" Text="{Binding Name}"/>
                                <ItemsControl ItemsSource="{Binding Rows}"
                                              ItemTemplate="{StaticResource ReadOnlyRowTemplate}"/>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <!-- ── Links ──────────────────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{StaticResource Label_GroupLinks}"/>
                <ItemsControl ItemsSource="{Binding Links}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="models:LinkRow">
                            <Grid ColumnDefinitions="60,*,Auto" Margin="0,1,0,1">
                                <TextBlock Grid.Column="0" Text="{Binding Arrow}"
                                           Foreground="#5dade2" FontSize="10"/>
                                <TextBlock Grid.Column="1" Text="{Binding Detail}"
                                           Foreground="#666" FontSize="10"/>
                                <Button Grid.Column="2"
                                        Content="{StaticResource Button_DeleteLink}"
                                        Command="{Binding DataContext.DeleteLinkCommand,
                                                  RelativeSource={RelativeSource AncestorType=UserControl}}"
                                        CommandParameter="{Binding}"
                                        Background="Transparent" BorderThickness="0"
                                        Foreground="#666" FontSize="9" Padding="4,1"
                                        ToolTip.Tip="{StaticResource ToolTip_DeleteLink}"/>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <Button Content="{StaticResource Button_AddLink}"
                        Command="{Binding AddLinkCommand}"
                        Background="#333" Foreground="#aaa" BorderThickness="0"
                        Padding="6,2" Margin="0,4,0,0" HorizontalAlignment="Left"
                        ToolTip.Tip="{StaticResource ToolTip_AddLink}"/>

            </StackPanel>
        </ScrollViewer>
    </Grid>
</UserControl>
```

> **Note on converters:** The `BoolToIndexConverter`, `DisplayTypeToIndexConverter`, and `PersistenceToIndexConverter` do not yet exist. Create them in `DialogEditor.Avalonia/Converters/`:
> - `BoolToIndexConverter` — `false → 0`, `true → 1` (and back)
> - `DisplayTypeToIndexConverter` — `"Conversation" → 0`, `"Bark" → 1` (and back)
> - `PersistenceToIndexConverter` — `"None" → 0`, `"OnceEver" → 1` (and back)
>
> Register them in `App.axaml` alongside the existing converters.

> **Note on DeleteLinkCommand / AddLinkCommand:** Add these to `NodeDetailViewModel`. `DeleteLinkCommand` removes the link from `_node.Links` by creating a `DeleteConnectionCommand` on the undo stack via a new `RequestDeleteLink` event wired to `ConversationViewModel`. `AddLinkCommand` opens a simple inline picker — implement as a text prompt in the detail panel (a `TextBox` + confirm button that accepts a target node ID).

- [ ] **Step 2: Build and run the app**

```
dotnet run --project DialogEditor.Avalonia
```

Verify: open a conversation, select a node, confirm text boxes and combos appear and are editable.

- [ ] **Step 3: Commit**

```
git add DialogEditor.Avalonia/Views/NodeDetailView.axaml
git add DialogEditor.Avalonia/Converters/BoolToIndexConverter.cs
git add DialogEditor.Avalonia/Converters/DisplayTypeToIndexConverter.cs
git add DialogEditor.Avalonia/Converters/PersistenceToIndexConverter.cs
git commit -m "feat: NodeDetailView — editable text, combo, checkbox controls"
```

---

### Task 18: ConversationView.axaml — context menus, pending connection, double-click

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml`
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml.cs`

- [ ] **Step 1: Add relay commands to ConversationViewModel for context menus**

In `ConversationViewModel.cs`, expose the structural edit operations as relay commands callable with `NodeViewModel` parameters (context menu can pass `CommandParameter="{Binding}"`):

```csharp
[RelayCommand]
private void DeleteNodeCmd(NodeViewModel? node)
{
    if (node is not null) DeleteNode(node);
}

[RelayCommand]
private void AddConnectedNodeCmd(NodeViewModel? parent)
{
    if (parent is null) return;
    var pos = new LayoutPoint(parent.Location.X + 250, parent.Location.Y);
    AddConnectedNode(parent, pos);
}

[RelayCommand]
private void DeleteConnectionCmd(ConnectionViewModel? connection)
{
    if (connection is not null) DeleteConnection(connection);
}
```

- [ ] **Step 2: Add context menu to each node in the ItemTemplate**

Inside the `<nodify:Node>` element in `ConversationView.axaml`, add:

```xml
<nodify:Node.ContextMenu>
    <ContextMenu>
        <MenuItem Header="{StaticResource Menu_DeleteNode}"
                  Command="{Binding DataContext.DeleteNodeCmdCommand,
                            RelativeSource={RelativeSource AncestorType=UserControl}}"
                  CommandParameter="{Binding}"
                  ToolTip.Tip="Remove this node and all its connections"/>
        <MenuItem Header="{StaticResource Menu_AddConnectedNode}"
                  Command="{Binding DataContext.AddConnectedNodeCmdCommand,
                            RelativeSource={RelativeSource AncestorType=UserControl}}"
                  CommandParameter="{Binding}"
                  ToolTip.Tip="Create a new node linked from this one"/>
    </ContextMenu>
</nodify:Node.ContextMenu>
```

- [ ] **Step 3: Add context menu to each connection**

Inside the `<nodify:Connection>` template, add:

```xml
<nodify:Connection.ContextMenu>
    <ContextMenu>
        <MenuItem Header="{StaticResource Menu_DeleteConnection}"
                  Command="{Binding DataContext.DeleteConnectionCmdCommand,
                            RelativeSource={RelativeSource AncestorType=UserControl}}"
                  CommandParameter="{Binding}"
                  ToolTip.Tip="Remove this connection between nodes"/>
    </ContextMenu>
</nodify:Connection.ContextMenu>
```

- [ ] **Step 4: Add pending connection support (drag to create)**

Add a `PendingConnectionViewModel` to `ConversationViewModel`:

```csharp
// In ConversationViewModel.cs
public PendingConnectionViewModel PendingConnection { get; }

// Constructor: PendingConnection = new PendingConnectionViewModel(this);
```

```csharp
// DialogEditor.ViewModels/ViewModels/PendingConnectionViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DialogEditor.ViewModels;

public partial class PendingConnectionViewModel(ConversationViewModel conversation)
    : ObservableObject
{
    [ObservableProperty] private ConnectorViewModel? _source;

    [RelayCommand]
    private void Start(ConnectorViewModel? connector) => Source = connector;

    [RelayCommand]
    private void Complete(ConnectorViewModel? target)
    {
        if (Source is null || target is null || Source == target) { Source = null; return; }
        // Prevent duplicate connections
        var alreadyExists = conversation.Connections.Any(c =>
            c.Source == Source && c.Target == target);
        if (!alreadyExists)
            conversation.AddConnection(Source, target);
        Source = null;
    }
}
```

In `ConversationView.axaml`, add the pending connection bindings to `NodifyEditor`. The exact Nodify.Avalonia API may vary by version — consult the installed package's documentation. The typical pattern is:

```xml
<nodify:NodifyEditor x:Name="Editor"
    ...
    ConnectionStartedCommand="{Binding PendingConnection.StartCommand}"
    ConnectionCompletedCommand="{Binding PendingConnection.CompleteCommand}">

    <nodify:NodifyEditor.PendingConnection>
        <nodify:PendingConnection
            Source="{Binding PendingConnection.Source.Anchor,
                     Converter={StaticResource LayoutPointConverter}}"
            Stroke="#aaa" StrokeThickness="2"/>
    </nodify:NodifyEditor.PendingConnection>
```

> If `ConnectionStartedCommand` / `ConnectionCompletedCommand` don't exist in your Nodify.Avalonia version, handle the equivalent events (`ConnectionStarted`, `ConnectionCompleted`) in code-behind and call `PendingConnection.StartCommand.Execute(connector)` / `PendingConnection.CompleteCommand.Execute(connector)`.

- [ ] **Step 5: Add double-click to create node**

In `ConversationView.axaml.cs`, add a handler:

```csharp
private void Editor_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
{
    if (DataContext is not ConversationViewModel vm) return;
    // Only handle taps directly on the editor background, not on nodes
    if (e.Source is not Nodify.NodifyEditor) return;

    var pos    = e.GetPosition(Editor);
    var canvas = Editor.ViewportTransform.Inverse().Transform(new global::Avalonia.Point(pos.X, pos.Y));
    var newId  = DialogEditor.Core.Editing.NodeIdAllocator.Next(vm.Nodes.Select(n => n.NodeId));
    var newNode = new NodeViewModel(
        new DialogEditor.Core.Models.ConversationNode(
            newId, false, DialogEditor.Core.Models.SpeakerCategory.Npc,
            string.Empty, string.Empty, [], [], [], "Conversation", "None"),
        null);
    vm.AddNode(newNode, new DialogEditor.Core.Models.LayoutPoint((int)canvas.X, (int)canvas.Y));
    e.Handled = true;
}
```

Wire the event in XAML on the `NodifyEditor`:

```xml
<nodify:NodifyEditor x:Name="Editor"
    ...
    DoubleTapped="Editor_DoubleTapped">
```

> `Editor.ViewportTransform` is the Nodify property that maps screen coordinates to canvas coordinates. Verify the exact property name against your Nodify.Avalonia version — it may be `ViewportLocation` + `ViewportZoom` instead, requiring a manual transform.

- [ ] **Step 6: Build and run**

```
dotnet run --project DialogEditor.Avalonia
```

Verify:
- Right-click on any node shows the context menu with Delete and Add connected node
- Right-click on a connection line shows Delete connection
- Drag from a node's output dot to another node's input dot creates a new connection
- Double-click on the empty canvas background creates a new node

- [ ] **Step 7: Commit**

```
git add DialogEditor.Avalonia/Views/ConversationView.axaml
git add DialogEditor.Avalonia/Views/ConversationView.axaml.cs
git add DialogEditor.ViewModels/ViewModels/PendingConnectionViewModel.cs
git commit -m "feat: ConversationView — context menus, drag-to-connect, double-click to add node"
```

---

### Task 19: MainWindow.axaml — undo/redo/save toolbar, keyboard shortcuts, dirty title

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Update toolbar in MainWindow.axaml**

In the existing top toolbar `<StackPanel>`, add the new buttons after the existing Open Folder button and separator:

```xml
<Rectangle Width="1" Fill="#444" Margin="8,0" VerticalAlignment="Stretch"/>

<Button Command="{Binding Canvas.UndoCommand}"
        Content="{StaticResource Button_Undo}"
        Background="#333" Foreground="#aaa" BorderThickness="0" Padding="8,2"
        ToolTip.Tip="{Binding Canvas.UndoDescription,
                      StringFormat={StaticResource ToolTip_Undo},
                      FallbackValue={StaticResource ToolTip_Undo_NoHistory}}"/>

<Button Command="{Binding Canvas.RedoCommand}"
        Content="{StaticResource Button_Redo}"
        Background="#333" Foreground="#aaa" BorderThickness="0" Padding="8,2" Margin="2,0,0,0"
        ToolTip.Tip="{Binding Canvas.RedoDescription,
                      StringFormat={StaticResource ToolTip_Redo},
                      FallbackValue={StaticResource ToolTip_Redo_NoHistory}}"/>

<Rectangle Width="1" Fill="#444" Margin="8,0" VerticalAlignment="Stretch"/>

<Button Command="{Binding SaveCommand}"
        Content="{StaticResource Button_Save}"
        Background="#333" Foreground="#aaa" BorderThickness="0" Padding="8,2"
        ToolTip.Tip="{StaticResource ToolTip_Save}"/>

<Button Command="{Binding RestoreBackupCommand}"
        Content="{StaticResource Button_RestoreBackup}"
        Background="#333" Foreground="#aaa" BorderThickness="0" Padding="8,2" Margin="2,0,0,0"
        ToolTip.Tip="{StaticResource ToolTip_RestoreBackup}"/>
```

- [ ] **Step 2: Bind window title to WindowTitle**

Change the `<Window>` `Title` attribute from:
```xml
Title="{StaticResource App_Title}"
```
to:
```xml
Title="{Binding WindowTitle}"
```

- [ ] **Step 3: Add keyboard shortcuts in MainWindow.axaml.cs**

In `OnKeyDownTunnel`, extend the existing handler:

```csharp
private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
{
    var vm = (MainWindowViewModel)DataContext!;

    switch (e.Key)
    {
        case Key.F when e.KeyModifiers == KeyModifiers.Control:
            CanvasView.FocusSearch();
            e.Handled = true;
            break;

        case Key.Z when e.KeyModifiers == KeyModifiers.Control:
            vm.Canvas.UndoCommand.Execute(null);
            e.Handled = true;
            break;

        case Key.Y when e.KeyModifiers == KeyModifiers.Control:
            vm.Canvas.RedoCommand.Execute(null);
            e.Handled = true;
            break;

        case Key.S when e.KeyModifiers == KeyModifiers.Control:
            vm.SaveCommand.Execute(null);
            e.Handled = true;
            break;

        case Key.Delete:
            if (vm.Canvas.SelectedNode is not null)
            {
                vm.Canvas.DeleteNodeCmdCommand.Execute(vm.Canvas.SelectedNode);
                e.Handled = true;
            }
            break;

        case Key.Escape when vm.IsBrowserFlyoutOpen:
            vm.IsBrowserExpanded = false;
            e.Handled = true;
            break;
    }
}
```

- [ ] **Step 4: Wire unsaved-changes prompt**

In `MainWindow()` constructor, subscribe to `MainWindowViewModel.UnsavedChangesRequested`:

```csharp
vm.UnsavedChangesRequested += () => _ = ShowUnsavedChangesDialogAsync(vm);
```

Add the async dialog method:

```csharp
private async Task ShowUnsavedChangesDialogAsync(MainWindowViewModel vm)
{
    // Avalonia MessageBox equivalent — use a simple dialog
    var dialog = new Window
    {
        Title = "Unsaved Changes",
        Width = 360, Height = 140,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        CanResize = false,
        Background = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#2d2d2d"))
    };

    TaskCompletionSource<string> tcs = new();

    var panel = new StackPanel { Margin = new Avalonia.Thickness(16) };
    panel.Children.Add(new TextBlock
    {
        Text = "You have unsaved changes. Save before switching conversations?",
        Foreground = Avalonia.Media.Brushes.White,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Margin = new Avalonia.Thickness(0, 0, 0, 16)
    });

    var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
    void Btn(string label, string result)
    {
        var b = new Button { Content = label, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => { tcs.TrySetResult(result); dialog.Close(); };
        buttons.Children.Add(b);
    }
    Btn("Save", "save");
    Btn("Discard", "discard");
    Btn("Cancel", "cancel");

    panel.Children.Add(buttons);
    dialog.Content = panel;

    await dialog.ShowDialog(this);
    var choice = await tcs.Task;

    switch (choice)
    {
        case "save":    vm.SaveAndProceed();           break;
        case "discard": vm.DiscardAndProceed();        break;
        default:        vm.CancelPendingNavigation();  break;
    }
}
```

- [ ] **Step 5: Build and run**

```
dotnet run --project DialogEditor.Avalonia
```

Verify:
- Undo (↩) and Redo (↪) buttons appear and are disabled when nothing to undo/redo
- Ctrl+Z / Ctrl+Y trigger undo/redo
- Ctrl+S saves (check a `.bak` appears next to the original file)
- Window title shows `● ConversationName` when there are unsaved changes
- Switching conversations while dirty shows the Save/Discard/Cancel prompt

- [ ] **Step 6: Commit**

```
git add DialogEditor.Avalonia/Views/MainWindow.axaml
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat: MainWindow — undo/redo/save toolbar, Ctrl+Z/Y/S shortcuts, dirty title, unsaved-changes prompt"
```

---

## Self-Review

Spec sections vs plan tasks:

| Spec section | Covered by |
|---|---|
| Undo/redo stack | Tasks 1–2 |
| NodeIdAllocator | Task 3 |
| BackupService | Task 4 |
| AppSettings backup tracking | Task 5 |
| ConversationEditSnapshot | Task 6 |
| StringTableSerializer | Task 7 |
| Poe2ConversationSerializer | Task 8 |
| Poe1ConversationSerializer | Task 9 |
| IGameDataProvider.SaveConversation | Task 10 |
| Mutable NodeViewModel | Task 11 |
| Structural edit commands | Task 12 |
| ConversationViewModel edit ops | Task 13 |
| NodeDetailViewModel editable | Task 14 |
| MainWindowViewModel save/backup | Task 15 |
| Strings.axaml | Task 16 |
| NodeDetailView editable controls | Task 17 |
| Canvas context menus + drag-connect | Task 18 |
| Toolbar, shortcuts, dirty title | Task 19 |

All spec requirements covered. No TBDs or placeholders.

---
