# Export Conversations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a one-shot export path from the editor to CSV, JSON, and Yarn Spinner formats via a dedicated dialog, clearly differentiated from the existing translation export.

**Architecture:** A core `Export/` layer mirrors the existing `Import/` layer: `IDialogExporter` interface + three format implementations + a factory. A new `ExportConversationsViewModel` drives a modal `ExportConversationsWindow` that lists all conversations for selection. `MainWindowViewModel` owns the command and wires the dialog via a callback.

**Tech Stack:** C# 12 / .NET 8, CommunityToolkit.Mvvm 8.2.2, Avalonia 11.3.14, xUnit 2.5.3, `Avalonia.Headless.XUnit` for UI tests, `System.Text.Json` for JSON export.

---

### Task 1: Core export interface and record

**Files:**
- Create: `DialogEditor.Core/Export/IDialogExporter.cs`

No test needed — this is an interface definition only. Commit after creating.

- [ ] **Step 1: Create the file**

```csharp
using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Export;

public record ConversationExport(
    string Name,
    IReadOnlyList<NodeEditSnapshot> Nodes
);

public interface IDialogExporter
{
    /// File extension including the leading dot, e.g. ".csv".
    string FileExtension { get; }

    void Export(ConversationExport conversation, string path);
}
```

- [ ] **Step 2: Build to verify no errors**

```
dotnet build DialogEditor.Core
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Core/Export/IDialogExporter.cs
git commit -m "feat: add IDialogExporter interface and ConversationExport record"
```

---

### Task 2: CsvDialogExporter and tests

**Files:**
- Create: `DialogEditor.Tests/Export/CsvDialogExporterTests.cs`
- Create: `DialogEditor.Core/Export/CsvDialogExporter.cs`

The CSV format is: header row `NodeId,SpeakerCategory,DefaultText,FemaleText,LinksTo,DisplayType,Persistence` with `LinksTo` as semicolon-separated target IDs. Fields containing commas, quotes, or newlines must be RFC 4180 quoted. Round-trip via `CsvDialogImporter` must be lossless for these fields.

- [ ] **Step 1: Write failing tests**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Export;
using DialogEditor.Core.Import;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Export;

public class CsvDialogExporterTests
{
    private static NodeEditSnapshot MakeNode(
        int id,
        SpeakerCategory category,
        string defaultText,
        string femaleText = "",
        List<LinkEditSnapshot>? links = null) =>
        new(
            NodeId: id,
            IsPlayerChoice: category == SpeakerCategory.Player,
            SpeakerCategory: category,
            SpeakerGuid: "",
            ListenerGuid: "",
            DefaultText: defaultText,
            FemaleText: femaleText,
            DisplayType: "Conversation",
            Persistence: "None",
            ActorDirection: "",
            Comments: "",
            ExternalVO: "",
            HasVO: false,
            HideSpeaker: false,
            Links: links ?? [],
            Conditions: [],
            Scripts: []);

    private static LinkEditSnapshot MakeLink(int from, int to) =>
        new(FromNodeId: from, ToNodeId: to, RandomWeight: 1f,
            QuestionNodeTextDisplay: "", HasConditions: false)
        { Conditions = null };

    [Fact]
    public void Export_ThenImport_RoundTripsNodeCountLinksAndSpeakerCategory()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hello.", links: [MakeLink(1, 2), MakeLink(1, 3)]),
            MakeNode(2, SpeakerCategory.Player, "Go left.", links: [MakeLink(2, 4)]),
            MakeNode(3, SpeakerCategory.Player, "Go right.", links: [MakeLink(3, 4)]),
        };
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            new CsvDialogExporter().Export(new ConversationExport("test", nodes), path);
            var imported = new CsvDialogImporter().Import(path);

            Assert.Equal(3, imported.Nodes.Count);
            Assert.Equal(SpeakerCategory.Npc,    imported.Nodes[0].SpeakerCategory);
            Assert.Equal(SpeakerCategory.Player, imported.Nodes[1].SpeakerCategory);
            Assert.Equal(2, imported.Nodes[0].Links.Count);
            Assert.Equal(2, imported.Nodes[0].Links[0].ToNodeId);
            Assert.Equal(3, imported.Nodes[0].Links[1].ToNodeId);
            Assert.Equal(4, imported.Nodes[1].Links[0].ToNodeId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_FieldWithComma_IsQuoted()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hello, world."),
        };
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            new CsvDialogExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("\"Hello, world.\"", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_FieldWithQuote_IsDoubledAndWrapped()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "She said \"hello\"."),
        };
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            new CsvDialogExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            // RFC 4180: embedded quote doubled inside wrapper quotes
            Assert.Contains("\"She said \"\"hello\"\".\"", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_FemaleText_RoundTrips()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hello.", femaleText: "Greetings."),
        };
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            new CsvDialogExporter().Export(new ConversationExport("test", nodes), path);
            var imported = new CsvDialogImporter().Import(path);
            Assert.Equal("Greetings.", imported.Nodes[0].FemaleText);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CsvDialogExporterTests"
```

Expected: compile error — `CsvDialogExporter` does not exist yet.

- [ ] **Step 3: Implement CsvDialogExporter**

```csharp
namespace DialogEditor.Core.Export;

public class CsvDialogExporter : IDialogExporter
{
    public string FileExtension => ".csv";

    public void Export(ConversationExport conversation, string path)
    {
        using var writer = new StreamWriter(path, append: false,
            encoding: System.Text.Encoding.UTF8);
        writer.WriteLine(
            "NodeId,SpeakerCategory,DefaultText,FemaleText,LinksTo,DisplayType,Persistence");

        foreach (var node in conversation.Nodes)
        {
            var linksTo = string.Join(";", node.Links.Select(l => l.ToNodeId));
            writer.WriteLine(string.Join(",",
                node.NodeId.ToString(),
                Escape(node.SpeakerCategory.ToString()),
                Escape(node.DefaultText),
                Escape(node.FemaleText),
                Escape(linksTo),
                Escape(node.DisplayType),
                Escape(node.Persistence)));
        }
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') ||
            value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CsvDialogExporterTests"
```

Expected: 4 passed.

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Export/CsvDialogExporter.cs DialogEditor.Tests/Export/CsvDialogExporterTests.cs
git commit -m "feat: CsvDialogExporter with round-trip and RFC 4180 quoting"
```

---

### Task 3: JsonDialogExporter and tests

**Files:**
- Create: `DialogEditor.Tests/Export/JsonDialogExporterTests.cs`
- Create: `DialogEditor.Core/Export/JsonDialogExporter.cs`

Writes the same schema `JsonDialogImporter` reads: `{ "name": "...", "nodes": [{ "id": int, "speakerCategory": str, "defaultText": str, "femaleText": str, "links": [int], "displayType": str, "persistence": str }] }`.

- [ ] **Step 1: Write failing tests**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Export;
using DialogEditor.Core.Import;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Export;

public class JsonDialogExporterTests
{
    private static NodeEditSnapshot MakeNode(
        int id, SpeakerCategory category, string defaultText,
        string femaleText = "", List<LinkEditSnapshot>? links = null) =>
        new(
            NodeId: id, IsPlayerChoice: category == SpeakerCategory.Player,
            SpeakerCategory: category, SpeakerGuid: "", ListenerGuid: "",
            DefaultText: defaultText, FemaleText: femaleText,
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "", ExternalVO: "",
            HasVO: false, HideSpeaker: false,
            Links: links ?? [], Conditions: [], Scripts: []);

    private static LinkEditSnapshot MakeLink(int from, int to) =>
        new(FromNodeId: from, ToNodeId: to, RandomWeight: 1f,
            QuestionNodeTextDisplay: "", HasConditions: false)
        { Conditions = null };

    [Fact]
    public void Export_ThenImport_RoundTripsAllFields()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hello.", "Greetings.",
                [MakeLink(1, 2), MakeLink(1, 3)]),
            MakeNode(2, SpeakerCategory.Player, "Go left.", links: [MakeLink(2, 4)]),
            MakeNode(3, SpeakerCategory.Player, "Go right.", link: [MakeLink(3, 4)]),
        };
        var path = Path.GetTempFileName() + ".json";
        try
        {
            new JsonDialogExporter().Export(new ConversationExport("my_conv", nodes), path);
            var imported = new JsonDialogImporter().Import(path);

            Assert.Equal("my_conv", imported.SuggestedName);
            Assert.Equal(3, imported.Nodes.Count);
            Assert.Equal(SpeakerCategory.Npc,    imported.Nodes[0].SpeakerCategory);
            Assert.Equal("Hello.",               imported.Nodes[0].DefaultText);
            Assert.Equal("Greetings.",           imported.Nodes[0].FemaleText);
            Assert.Equal(2, imported.Nodes[0].Links.Count);
            Assert.Equal(2, imported.Nodes[0].Links[0].ToNodeId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_WritesConversationName()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hi."),
        };
        var path = Path.GetTempFileName() + ".json";
        try
        {
            new JsonDialogExporter().Export(new ConversationExport("city_market", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("\"name\"", content);
            Assert.Contains("city_market", content);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~JsonDialogExporterTests"
```

Expected: compile error — `JsonDialogExporter` does not exist yet.

- [ ] **Step 3: Implement JsonDialogExporter**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DialogEditor.Core.Export;

public class JsonDialogExporter : IDialogExporter
{
    public string FileExtension => ".json";

    public void Export(ConversationExport conversation, string path)
    {
        var nodesArray = new JsonArray(conversation.Nodes.Select(n => (JsonNode)new JsonObject
        {
            ["id"]              = n.NodeId,
            ["speakerCategory"] = n.SpeakerCategory.ToString(),
            ["defaultText"]     = n.DefaultText,
            ["femaleText"]      = n.FemaleText,
            ["links"]           = new JsonArray(
                n.Links.Select(l => (JsonNode)JsonValue.Create(l.ToNodeId)).ToArray()),
            ["displayType"]     = n.DisplayType,
            ["persistence"]     = n.Persistence,
        }).ToArray());

        var doc = new JsonObject
        {
            ["name"]  = conversation.Name,
            ["nodes"] = nodesArray,
        };

        File.WriteAllText(path,
            doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

Note: The test code in Step 1 has a typo (`link:` instead of `links:`) — fix it while implementing:

Corrected node 3 constructor call:
```csharp
MakeNode(3, SpeakerCategory.Player, "Go right.", links: [MakeLink(3, 4)]),
```

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~JsonDialogExporterTests"
```

Expected: 2 passed.

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Export/JsonDialogExporter.cs DialogEditor.Tests/Export/JsonDialogExporterTests.cs
git commit -m "feat: JsonDialogExporter with round-trip parity to JsonDialogImporter"
```

---

### Task 4: YarnSpinnerExporter and tests

**Files:**
- Create: `DialogEditor.Tests/Export/YarnSpinnerExporterTests.cs`
- Create: `DialogEditor.Core/Export/YarnSpinnerExporter.cs`

Each node becomes one `title:` block named by its `NodeId`. Non-player-choice nodes emit `SpeakerCategory: DefaultText`. Player-choice nodes emit `-> DefaultText [[ToNodeId]]` for each outgoing link (or `-> DefaultText` if no links). This is a lossy export — conditions, scripts, and FemaleText are silently omitted, matching the spec.

- [ ] **Step 1: Write failing tests**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Export;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Export;

public class YarnSpinnerExporterTests
{
    private static NodeEditSnapshot MakeNode(
        int id, SpeakerCategory category, string defaultText,
        List<LinkEditSnapshot>? links = null) =>
        new(
            NodeId: id, IsPlayerChoice: category == SpeakerCategory.Player,
            SpeakerCategory: category, SpeakerGuid: "", ListenerGuid: "",
            DefaultText: defaultText, FemaleText: "",
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "", ExternalVO: "",
            HasVO: false, HideSpeaker: false,
            Links: links ?? [], Conditions: [], Scripts: []);

    private static LinkEditSnapshot MakeLink(int from, int to) =>
        new(FromNodeId: from, ToNodeId: to, RandomWeight: 1f,
            QuestionNodeTextDisplay: "", HasConditions: false)
        { Conditions = null };

    [Fact]
    public void Export_NpcNode_WritesTitleAndSpeakerLine()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hello there."),
        };
        var path = Path.GetTempFileName() + ".yarn";
        try
        {
            new YarnSpinnerExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("title: 1", content);
            Assert.Contains("---",      content);
            Assert.Contains("Npc: Hello there.", content);
            Assert.Contains("===",      content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_PlayerChoiceNode_WritesChoiceLineWithTarget()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(2, SpeakerCategory.Player, "I need work.", [MakeLink(2, 3)]),
        };
        var path = Path.GetTempFileName() + ".yarn";
        try
        {
            new YarnSpinnerExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("title: 2",              content);
            Assert.Contains("-> I need work. [[3]]", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_PlayerChoiceWithNoLinks_WritesChoiceLineWithoutTarget()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(5, SpeakerCategory.Player, "Farewell."),
        };
        var path = Path.GetTempFileName() + ".yarn";
        try
        {
            new YarnSpinnerExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("-> Farewell.", content);
            Assert.DoesNotContain("[[", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_MultipleNodes_AllGetTitleBlocks()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc,    "Hello.",      [MakeLink(1, 2)]),
            MakeNode(2, SpeakerCategory.Player, "I need work.",[MakeLink(2, 3)]),
            MakeNode(3, SpeakerCategory.Npc,    "Here's work."),
        };
        var path = Path.GetTempFileName() + ".yarn";
        try
        {
            new YarnSpinnerExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("title: 1", content);
            Assert.Contains("title: 2", content);
            Assert.Contains("title: 3", content);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~YarnSpinnerExporterTests"
```

Expected: compile error — `YarnSpinnerExporter` does not exist yet.

- [ ] **Step 3: Implement YarnSpinnerExporter**

```csharp
using System.Text;

namespace DialogEditor.Core.Export;

public class YarnSpinnerExporter : IDialogExporter
{
    public string FileExtension => ".yarn";

    public void Export(ConversationExport conversation, string path)
    {
        var sb = new StringBuilder();

        foreach (var node in conversation.Nodes)
        {
            sb.AppendLine($"title: {node.NodeId}");
            sb.AppendLine("---");

            if (node.IsPlayerChoice)
            {
                if (node.Links.Count > 0)
                {
                    foreach (var link in node.Links)
                        sb.AppendLine($"-> {node.DefaultText} [[{link.ToNodeId}]]");
                }
                else
                {
                    sb.AppendLine($"-> {node.DefaultText}");
                }
            }
            else
            {
                sb.AppendLine($"{node.SpeakerCategory}: {node.DefaultText}");
            }

            sb.AppendLine("===");
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~YarnSpinnerExporterTests"
```

Expected: 4 passed.

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Export/YarnSpinnerExporter.cs DialogEditor.Tests/Export/YarnSpinnerExporterTests.cs
git commit -m "feat: YarnSpinnerExporter — one title block per node, choices as -> lines"
```

---

### Task 5: DialogExporterFactory

**Files:**
- Create: `DialogEditor.Core/Export/DialogExporterFactory.cs`

Simple lookup: format name string → exporter. No separate tests needed — coverage comes from the ViewModel tests in Task 6 which call `GetForFormat`.

- [ ] **Step 1: Create the factory**

```csharp
namespace DialogEditor.Core.Export;

public static class DialogExporterFactory
{
    private static readonly Dictionary<string, IDialogExporter> _byFormat =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["csv"]  = new CsvDialogExporter(),
            ["json"] = new JsonDialogExporter(),
            ["yarn"] = new YarnSpinnerExporter(),
        };

    /// All supported (format key, file extension, human-readable description) tuples.
    public static IReadOnlyList<(string Format, string Extension, string Description)> AllFormats =>
    [
        ("csv",  ".csv",  "CSV"),
        ("json", ".json", "JSON"),
        ("yarn", ".yarn", "Yarn Spinner"),
    ];

    /// Returns the exporter for the given format key, or null if unknown.
    public static IDialogExporter? GetForFormat(string format) =>
        _byFormat.TryGetValue(format, out var exporter) ? exporter : null;
}
```

- [ ] **Step 2: Build and run all export tests**

```
dotnet build DialogEditor.Core && dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DialogEditor.Tests.Export"
```

Expected: `Build succeeded. 0 Error(s)` and all export tests pass.

- [ ] **Step 3: Commit**

```
git add DialogEditor.Core/Export/DialogExporterFactory.cs
git commit -m "feat: DialogExporterFactory — format string to exporter lookup"
```

---

### Task 6: ExportConversationsViewModel and tests

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/ExportConversationsViewModel.cs`
- Create: `DialogEditor.Tests/ViewModels/ExportConversationsViewModelTests.cs`

`ConversationExportItem` is a nested `ObservableObject` whose `IsChecked` changes trigger `ExportCommand.NotifyCanExecuteChanged()` via subscription in the ViewModel constructor. The item matching `currentConversationName` is pre-checked; all others start unchecked.

- [ ] **Step 1: Write failing tests**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Export;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class ExportConversationsViewModelTests
{
    public ExportConversationsViewModelTests() =>
        Loc.Configure(new StubStringProvider());

    private static ExportConversationsViewModel MakeVm(
        string? currentConversation = null,
        string? saveResult = null,
        string? folderResult = null) =>
        new(
            ["conv_a", "conv_b", "conv_c"],
            currentConversation,
            _ => [],
            new StubFilePicker(saveResult: saveResult),
            new StubFolderPicker(folderResult));

    [Fact]
    public void Constructor_CurrentConversation_IsPreChecked()
    {
        var vm = MakeVm(currentConversation: "conv_b");
        Assert.True(vm.ConversationItems.Single(i => i.Name == "conv_b").IsChecked);
    }

    [Fact]
    public void Constructor_OtherConversations_AreNotPreChecked()
    {
        var vm = MakeVm(currentConversation: "conv_b");
        Assert.False(vm.ConversationItems.Single(i => i.Name == "conv_a").IsChecked);
        Assert.False(vm.ConversationItems.Single(i => i.Name == "conv_c").IsChecked);
    }

    [Fact]
    public void Constructor_NullCurrentConversation_NoneChecked()
    {
        var vm = MakeVm(currentConversation: null);
        Assert.All(vm.ConversationItems, i => Assert.False(i.IsChecked));
    }

    [Fact]
    public void SelectAll_ChecksEveryItem()
    {
        var vm = MakeVm();
        vm.SelectAllCommand.Execute(null);
        Assert.All(vm.ConversationItems, i => Assert.True(i.IsChecked));
    }

    [Fact]
    public void SelectNone_UnchecksEveryItem()
    {
        var vm = MakeVm(currentConversation: "conv_a");
        vm.SelectNoneCommand.Execute(null);
        Assert.All(vm.ConversationItems, i => Assert.False(i.IsChecked));
    }

    [Fact]
    public void ExportCommand_CannotExecute_WhenNothingChecked()
    {
        var vm = MakeVm();
        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void ExportCommand_CanExecute_WhenOneItemChecked()
    {
        var vm = MakeVm();
        vm.ConversationItems[0].IsChecked = true;
        Assert.True(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void ExportCommand_CannotExecute_AfterUnchecking()
    {
        var vm = MakeVm(currentConversation: "conv_a");
        vm.ConversationItems[0].IsChecked = false;
        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void SelectedFormat_DefaultsTo_Csv()
    {
        var vm = MakeVm();
        Assert.Equal("csv", vm.SelectedFormat);
    }

    [Fact]
    public async Task ExportCommand_SingleItem_WritesFile()
    {
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            var nodes = new List<NodeEditSnapshot>
            {
                new(NodeId: 1, IsPlayerChoice: false,
                    SpeakerCategory: Core.Models.SpeakerCategory.Npc,
                    SpeakerGuid: "", ListenerGuid: "",
                    DefaultText: "Hi.", FemaleText: "",
                    DisplayType: "Conversation", Persistence: "None",
                    ActorDirection: "", Comments: "", ExternalVO: "",
                    HasVO: false, HideSpeaker: false,
                    Links: [], Conditions: [], Scripts: [])
            };
            var vm = new ExportConversationsViewModel(
                ["test_conv"],
                "test_conv",
                name => nodes,
                new StubFilePicker(saveResult: path),
                new StubFolderPicker());

            await vm.ExportCommand.ExecuteAsync(null);
            Assert.True(File.Exists(path));
            Assert.NotEmpty(File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ExportConversationsViewModelTests"
```

Expected: compile error — `ExportConversationsViewModel` does not exist yet.

- [ ] **Step 3: Implement ConversationExportItem and ExportConversationsViewModel**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Export;
using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class ConversationExportItem : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isChecked;

    public ConversationExportItem(string name, bool isChecked = false)
    {
        Name = name;
        _isChecked = isChecked;
    }
}

public partial class ExportConversationsViewModel : ObservableObject
{
    private readonly Func<string, IReadOnlyList<NodeEditSnapshot>> _nodesFetch;
    private readonly IFilePicker   _filePicker;
    private readonly IFolderPicker _folderPicker;

    public ObservableCollection<ConversationExportItem> ConversationItems { get; }

    [ObservableProperty] private string _selectedFormat = "csv";
    [ObservableProperty] private string _statusText     = "";

    public ExportConversationsViewModel(
        IReadOnlyList<string> conversationNames,
        string? currentConversationName,
        Func<string, IReadOnlyList<NodeEditSnapshot>> nodesFetch,
        IFilePicker filePicker,
        IFolderPicker folderPicker)
    {
        _nodesFetch   = nodesFetch;
        _filePicker   = filePicker;
        _folderPicker = folderPicker;

        ConversationItems = new ObservableCollection<ConversationExportItem>(
            conversationNames.Select(n =>
                new ConversationExportItem(n, n == currentConversationName)));

        foreach (var item in ConversationItems)
            item.PropertyChanged += (_, _) => ExportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in ConversationItems)
            item.IsChecked = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in ConversationItems)
            item.IsChecked = false;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        var selected = ConversationItems.Where(i => i.IsChecked).ToList();
        var exporter = DialogExporterFactory.GetForFormat(SelectedFormat);
        if (exporter is null) return;

        try
        {
            if (selected.Count == 1)
            {
                var item = selected[0];
                var path = await _filePicker.PickSaveFileAsync(
                    "Export Conversation",
                    item.Name,
                    exporter.FileExtension,
                    exporter.FileExtension.TrimStart('.').ToUpperInvariant());
                if (path is null) return;
                exporter.Export(new ConversationExport(item.Name, _nodesFetch(item.Name)), path);
                StatusText = string.Format(
                    Loc.Get("Status_ExportConversationsSaved"), 1, path);
            }
            else
            {
                var folder = await _folderPicker.PickFolderAsync("Export Conversations");
                if (folder is null) return;
                foreach (var item in selected)
                {
                    var path = Path.Combine(folder, item.Name + exporter.FileExtension);
                    exporter.Export(new ConversationExport(item.Name, _nodesFetch(item.Name)), path);
                }
                StatusText = string.Format(
                    Loc.Get("Status_ExportConversationsSaved"), selected.Count, folder);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Export conversations failed", ex);
            StatusText = string.Format(Loc.Get("Status_ExportConversationsError"), ex.Message);
        }
    }

    private bool CanExport() => ConversationItems.Any(i => i.IsChecked);
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ExportConversationsViewModelTests"
```

Expected: all pass.

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/ViewModels/ExportConversationsViewModel.cs
git add DialogEditor.Tests/ViewModels/ExportConversationsViewModelTests.cs
git commit -m "feat: ExportConversationsViewModel with SelectAll/None and ExportCommand"
```

---

### Task 7: ExportConversationsWindow AXAML and headless tests

**Files:**
- Create: `DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml.cs`
- Create: `DialogEditor.Tests/Views/ExportConversationsWindowTests.cs`

Three zones: (1) conversation list with Select All / Select None links and a scrollable `ItemsControl` of checkboxes, (2) format radio buttons (CSV, JSON, Yarn Spinner, and Articy disabled), (3) footer with `StatusText`, Export button, and Close button. The window is modal (`ShowDialog`).

- [ ] **Step 1: Write failing headless tests**

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Views;
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.Views;

public class ExportConversationsWindowTests
{
    private static ExportConversationsViewModel MakeVm(string? currentConversation = null) =>
        new(
            ["conv_a", "conv_b"],
            currentConversation,
            _ => [],
            new Helpers.StubFilePicker(),
            new Helpers.StubFolderPicker());

    [AvaloniaFact]
    public void ArticyRadioButton_IsDisabled()
    {
        var vm     = MakeVm();
        var window = new ExportConversationsWindow(vm);
        window.Show();
        Assert.False(window.FindControl<RadioButton>("ArticyRadioButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public void ExportButton_IsDisabled_WhenNothingChecked()
    {
        var vm     = MakeVm(currentConversation: null);
        var window = new ExportConversationsWindow(vm);
        window.Show();
        Assert.False(window.FindControl<Button>("ExportButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public void ExportButton_Enables_AfterCheckingItem()
    {
        var vm     = MakeVm(currentConversation: null);
        var window = new ExportConversationsWindow(vm);
        window.Show();
        vm.ConversationItems[0].IsChecked = true;
        Assert.True(window.FindControl<Button>("ExportButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public void SelectAllButton_ChecksAllItems()
    {
        var vm     = MakeVm(currentConversation: null);
        var window = new ExportConversationsWindow(vm);
        window.Show();
        window.FindControl<Button>("SelectAllButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.All(vm.ConversationItems, i => Assert.True(i.IsChecked));
    }

    [AvaloniaFact]
    public void SelectNoneButton_UnchecksAllItems()
    {
        var vm     = MakeVm(currentConversation: "conv_a");
        var window = new ExportConversationsWindow(vm);
        window.Show();
        window.FindControl<Button>("SelectNoneButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.All(vm.ConversationItems, i => Assert.False(i.IsChecked));
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ExportConversationsWindowTests"
```

Expected: compile error — `ExportConversationsWindow` does not exist yet.

- [ ] **Step 3: Create ExportConversationsWindow.axaml**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        x:Class="DialogEditor.Avalonia.Views.ExportConversationsWindow"
        Title="{StaticResource ExportConversations_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="480" Height="520"
        Background="#1e1e1e"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        x:CompileBindings="False">

    <DockPanel Margin="16">

        <!-- ── Conversation list ──────────────────────────────────────── -->
        <StackPanel DockPanel.Dock="Top" Margin="0,0,0,12">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                <Button x:Name="SelectAllButton"
                        Content="{StaticResource ExportConversations_SelectAll}"
                        Command="{Binding SelectAllCommand}"
                        Theme="{StaticResource LinkButton}"
                        ToolTip.Tip="{StaticResource ToolTip_ExportConversations_SelectAll}"/>
                <TextBlock Text=" / " Foreground="#888" VerticalAlignment="Center"/>
                <Button x:Name="SelectNoneButton"
                        Content="{StaticResource ExportConversations_SelectNone}"
                        Command="{Binding SelectNoneCommand}"
                        Theme="{StaticResource LinkButton}"
                        ToolTip.Tip="{StaticResource ToolTip_ExportConversations_SelectNone}"/>
            </StackPanel>
            <Border Height="200" BorderBrush="#444" BorderThickness="1" CornerRadius="4">
                <ScrollViewer>
                    <ItemsControl ItemsSource="{Binding ConversationItems}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate DataType="{x:Type vm:ConversationExportItem}">
                                <CheckBox Content="{Binding Name}"
                                          IsChecked="{Binding IsChecked}"
                                          Foreground="#ddd"
                                          Padding="8,4"/>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Border>
        </StackPanel>

        <!-- ── Footer ────────────────────────────────────────────────── -->
        <Grid DockPanel.Dock="Bottom" Margin="0,8,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="8"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0"
                       Text="{Binding StatusText}"
                       Foreground="#888"
                       VerticalAlignment="Center"
                       TextWrapping="Wrap"/>
            <Button Grid.Column="1"
                    x:Name="ExportButton"
                    Content="{StaticResource ExportConversations_Export}"
                    Command="{Binding ExportCommand}"
                    ToolTip.Tip="{StaticResource ToolTip_ExportConversations_Export}"
                    MinWidth="80"/>
            <Button Grid.Column="3"
                    x:Name="CloseButton"
                    Content="{StaticResource ExportConversations_Close}"
                    Click="CloseButton_Click"
                    MinWidth="80"/>
        </Grid>

        <!-- ── Format selector ───────────────────────────────────────── -->
        <StackPanel Margin="0,0,0,12">
            <TextBlock Text="{StaticResource ExportConversations_Format}"
                       Foreground="#aaa"
                       Margin="0,0,0,6"/>
            <RadioButton Content="CSV"
                         GroupName="Format"
                         IsChecked="{Binding SelectedFormat, Converter={StaticResource StringEqualsBoolConverter}, ConverterParameter=csv}"
                         Tag="csv"
                         Checked="FormatRadio_Checked"
                         ToolTip.Tip="{StaticResource ToolTip_ExportConversations_Csv}"/>
            <RadioButton Content="JSON"
                         GroupName="Format"
                         IsChecked="{Binding SelectedFormat, Converter={StaticResource StringEqualsBoolConverter}, ConverterParameter=json}"
                         Tag="json"
                         Checked="FormatRadio_Checked"
                         ToolTip.Tip="{StaticResource ToolTip_ExportConversations_Json}"/>
            <RadioButton Content="Yarn Spinner"
                         GroupName="Format"
                         IsChecked="{Binding SelectedFormat, Converter={StaticResource StringEqualsBoolConverter}, ConverterParameter=yarn}"
                         Tag="yarn"
                         Checked="FormatRadio_Checked"
                         ToolTip.Tip="{StaticResource ToolTip_ExportConversations_Yarn}"/>
            <RadioButton x:Name="ArticyRadioButton"
                         Content="Articy Draft XML"
                         GroupName="Format"
                         IsEnabled="False"
                         ToolTip.Tip="{StaticResource ExportConversations_ArticyNote}"/>
        </StackPanel>

    </DockPanel>
</Window>
```

**Note on radio button binding:** The `SelectedFormat` string property does not bind natively to `RadioButton.IsChecked`. The AXAML above uses a `StringEqualsBoolConverter` — check whether that converter already exists in the project. If it does not exist, simplify the approach: handle `Checked` events in code-behind and set `SelectedFormat` directly (remove the `IsChecked` bindings). The `SelectAllButton` and `SelectNoneButton` use `Theme="{StaticResource LinkButton}"` — verify that resource exists; if not, remove the `Theme` attribute and style them as plain Buttons.

- [ ] **Step 4: Create ExportConversationsWindow.axaml.cs**

```csharp
using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class ExportConversationsWindow : Window
{
    public ExportConversationsWindow(ExportConversationsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void FormatRadio_Checked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string format } &&
            DataContext is ExportConversationsViewModel vm)
            vm.SelectedFormat = format;
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
```

- [ ] **Step 5: Check for StringEqualsBoolConverter — add if missing**

Search for the converter:
```
grep -r "StringEqualsBoolConverter" DialogEditor.Avalonia
```

If it does not exist, add it to `DialogEditor.Avalonia/Converters/StringEqualsBoolConverter.cs`:

```csharp
using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

public class StringEqualsBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && parameter is string p && s.Equals(p, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter : Avalonia.Data.BindingOperations.DoNothing;
}
```

Then register it in `App.axaml` or the window's resources:
```xml
<converters:StringEqualsBoolConverter x:Key="StringEqualsBoolConverter"/>
```

If this converter already exists under a different name, use that name in the AXAML instead.

- [ ] **Step 6: Build**

```
dotnet build DialogEditor.Avalonia
```

Expected: `Build succeeded. 0 Error(s)`. Fix any AXAML compile errors (missing resource keys, unknown converters) before proceeding.

- [ ] **Step 7: Run headless tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ExportConversationsWindowTests"
```

Expected: 5 passed.

- [ ] **Step 8: Commit**

```
git add DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml
git add DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml.cs
git add DialogEditor.Tests/Views/ExportConversationsWindowTests.cs
git commit -m "feat: ExportConversationsWindow — conversation list, format selector, footer"
```

---

### Task 8: Strings, menu item, MainWindowViewModel command, MainWindow wiring

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` — add new keys
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` — add menu item
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` — wire `ShowExportConversations` callback
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` — add command + callback
- Modify: `Gaps.md` — remove "Export to External Formats" gap

This task is integration-only; the ViewModel and window are already tested. No new test files.

- [ ] **Step 1: Add new string keys to Strings.axaml**

Add after the `<!-- ─── Import Conversation ... ─── -->` block (after line 619, before the localization section):

```xml
    <!-- ─── Export Conversation (to external formats) ──────────────── -->
    <sys:String x:Key="Menu_ExportConversation">Export Conversation…</sys:String>
    <sys:String x:Key="ToolTip_ExportConversation">Export selected conversations to CSV, JSON, or Yarn Spinner. This exports conversation structure (nodes, links, speaker categories), not translated text.</sys:String>
    <sys:String x:Key="ExportConversations_Title">Export Conversations</sys:String>
    <sys:String x:Key="ExportConversations_SelectAll">Select All</sys:String>
    <sys:String x:Key="ExportConversations_SelectNone">Select None</sys:String>
    <sys:String x:Key="ExportConversations_Format">Format</sys:String>
    <sys:String x:Key="ExportConversations_ArticyNote">Articy Draft XML export is not supported. Articy's format requires proprietary internal IDs and template definitions that the editor does not store.</sys:String>
    <sys:String x:Key="ExportConversations_Export">Export…</sys:String>
    <sys:String x:Key="ExportConversations_Close">Close</sys:String>
    <sys:String x:Key="ToolTip_ExportConversations_SelectAll">Check all conversations in the list.</sys:String>
    <sys:String x:Key="ToolTip_ExportConversations_SelectNone">Uncheck all conversations in the list.</sys:String>
    <sys:String x:Key="ToolTip_ExportConversations_Export">Export the checked conversations to the selected format.</sys:String>
    <sys:String x:Key="ToolTip_ExportConversations_Csv">Export to comma-separated values. Round-trips cleanly through Import Conversation.</sys:String>
    <sys:String x:Key="ToolTip_ExportConversations_Json">Export to JSON. Round-trips cleanly through Import Conversation.</sys:String>
    <sys:String x:Key="ToolTip_ExportConversations_Yarn">Export to Yarn Spinner .yarn format. Conditions, scripts, and female text are omitted.</sys:String>
    <!-- {0} = count, {1} = path or folder -->
    <sys:String x:Key="Status_ExportConversationsSaved">Exported {0} conversation(s) to {1}.</sys:String>
    <!-- {0} = error message -->
    <sys:String x:Key="Status_ExportConversationsError">Export failed: {0}</sys:String>
```

- [ ] **Step 2: Add menu item to MainWindow.axaml**

In `MainWindow.axaml`, find the `ImportConversation` menu item block (around line 60) and add the Export Conversation item immediately after it, before the `<Separator/>`:

```xml
                        <MenuItem Header="{StaticResource Menu_ImportConversation}"
                                  Command="{Binding ImportConversationCommand}"
                                  ToolTip.Tip="{StaticResource ToolTip_ImportConversation}"/>
                        <MenuItem Header="{StaticResource Menu_ExportConversation}"
                                  Command="{Binding ExportConversationsCommand}"
                                  ToolTip.Tip="{StaticResource ToolTip_ExportConversation}"/>
                        <Separator/>
```

- [ ] **Step 3: Add command and callback to MainWindowViewModel.cs**

Add the callback property after the existing `RequestConversationNameWithSuggestion` property (around line 52):

```csharp
    /// Set by the UI layer to show the Export Conversations dialog.
    public Func<ExportConversationsViewModel, Task>? ShowExportConversations { get; set; }
```

Add the command method. Place it alongside the other file-operation commands (e.g., near `ImportConversation`):

```csharp
    [RelayCommand(CanExecute = nameof(IsProjectLoaded))]
    private async Task ExportConversations()
    {
        if (_project is null) return;
        var evm = new ExportConversationsViewModel(
            _project.Patches.Keys.ToList(),
            CurrentConversationName,
            name => _project.Patches[name].Nodes,
            _filePicker,
            _folderPicker);
        if (ShowExportConversations is not null)
            await ShowExportConversations(evm);
    }
```

Add `IsProjectLoaded()` check — this predicate already exists in the codebase. Verify by searching:
```
grep -n "bool IsProjectLoaded" DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
```

In `SetProject()`, add the new command to the `NotifyCanExecuteChanged` block alongside the existing commands:
```csharp
        ExportConversationsCommand.NotifyCanExecuteChanged();
```

- [ ] **Step 4: Wire the callback in MainWindow.axaml.cs**

In `MainWindow.axaml.cs`, find the constructor or the method that wires the other callbacks (e.g., `RequestConversationName`, `RequestConflictResolution`) and add:

```csharp
        vm.ShowExportConversations = async evm =>
        {
            var window = new ExportConversationsWindow(evm);
            await window.ShowDialog(this);
        };
```

- [ ] **Step 5: Build the full solution**

```
dotnet build
```

Expected: `Build succeeded. 0 Error(s)`. Fix any remaining compile errors before proceeding.

- [ ] **Step 6: Run all tests**

```
dotnet test DialogEditor.Tests
```

Expected: all tests pass. If any fail, diagnose and fix before committing.

- [ ] **Step 7: Update Gaps.md**

Remove the "Export to External Formats" entry from `Gaps.md`. It has been implemented.

- [ ] **Step 8: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml
git add DialogEditor.Avalonia/Views/MainWindow.axaml
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git add Gaps.md
git commit -m "feat: wire ExportConversationsCommand into MainWindow — menu item, dialog callback, CanExecute"
```

---

## Verification Checklist

After all tasks complete:

1. `dotnet test DialogEditor.Tests` — all tests pass (0 failures)
2. `dotnet build DialogEditor.Avalonia` — 0 errors
3. **Manual**: open the app, File → Export Conversation… — dialog opens with conversation list; current conversation is pre-checked
4. **Manual**: check one conversation, select CSV, click Export… — save dialog appears; file is written; status text shows success message
5. **Manual**: check two conversations, select JSON, click Export… — folder picker appears; two `.json` files written to chosen folder
6. **Manual**: verify Articy radio button is greyed out and shows tooltip
7. **Manual**: verify the Export Conversation… menu item sits between Import Conversation… and the separator, not among the translation export items
