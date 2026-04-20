# Dialog Editor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a read-only WPF dialog viewer for PoE1 and PoE2 Deadfire that renders conversation files as a Missouri-style node canvas with color-coded cards, connections, and a detail panel.

**Architecture:** Two-project solution (`DialogEditor.Core` — zero UI deps, `DialogEditor.WPF` — WPF + Nodify). Core owns all parsing, models, and layout math. WPF owns all ViewModels and Views. A separate `DialogEditor.Tests` xUnit project tests Core.

**Tech Stack:** C# 12 / .NET 8, WPF, Nodify 6.1.0, CommunityToolkit.Mvvm 8.2.2, xUnit 2.9.0, System.Text.Json (built-in), System.Xml.Linq (built-in).

---

## File Map

```
DialogEditor.sln
DialogEditor.Core/
  DialogEditor.Core.csproj
  Models/
    ConversationNode.cs
    NodeLink.cs
    Conversation.cs
    StringEntry.cs
    StringTable.cs
  Parsing/
    ConditionFormatter.cs
    StringTableParser.cs
    Poe1ConversationParser.cs
    Poe2ConversationParser.cs
  GameData/
    ConversationFile.cs
    IGameDataProvider.cs
    GameDataProviderFactory.cs
    Poe1GameDataProvider.cs
    Poe2GameDataProvider.cs
  Layout/
    AutoLayoutService.cs
DialogEditor.Tests/
  DialogEditor.Tests.csproj
  Parsing/
    ConditionFormatterTests.cs
    StringTableParserTests.cs
    Poe1ConversationParserTests.cs
    Poe2ConversationParserTests.cs
  GameData/
    GameDataProviderFactoryTests.cs
  Layout/
    AutoLayoutServiceTests.cs
DialogEditor.WPF/
  DialogEditor.WPF.csproj
  App.xaml / App.xaml.cs
  Converters/
    BoolToHeaderBrushConverter.cs
    InverseBoolToVisibilityConverter.cs
    NullOrEmptyToVisibilityConverter.cs
  Services/
    SpeakerNameService.cs
  ViewModels/
    MainWindowViewModel.cs
    GameBrowserViewModel.cs
    ConversationFolderViewModel.cs
    ConversationItemViewModel.cs
    ConversationViewModel.cs
    ConnectorViewModel.cs
    NodeViewModel.cs
    ConnectionViewModel.cs
    NodeDetailViewModel.cs
  Views/
    MainWindow.xaml / MainWindow.xaml.cs
    GameBrowserView.xaml / GameBrowserView.xaml.cs
    ConversationView.xaml / ConversationView.xaml.cs
    NodeDetailView.xaml / NodeDetailView.xaml.cs
```

---

## Task 1: Solution Scaffold

**Files:**
- Create: `DialogEditor.sln`
- Create: `DialogEditor.Core/DialogEditor.Core.csproj`
- Create: `DialogEditor.Tests/DialogEditor.Tests.csproj`
- Create: `DialogEditor.WPF/DialogEditor.WPF.csproj`

- [ ] **Step 1: Create solution and projects**

Run from the repo root (`C:\Users\kjmik\Documents\Programming\Deadfire\Dialog Editor`):

```bash
dotnet new sln -n DialogEditor
dotnet new classlib -n DialogEditor.Core --framework net8.0
dotnet new xunit -n DialogEditor.Tests --framework net8.0-windows
dotnet new wpf -n DialogEditor.WPF --framework net8.0-windows
```

Expected: three project folders created, each with a `.csproj`.

- [ ] **Step 2: Wire solution and project references**

```bash
dotnet sln add DialogEditor.Core/DialogEditor.Core.csproj
dotnet sln add DialogEditor.Tests/DialogEditor.Tests.csproj
dotnet sln add DialogEditor.WPF/DialogEditor.WPF.csproj
dotnet add DialogEditor.Tests/DialogEditor.Tests.csproj reference DialogEditor.Core/DialogEditor.Core.csproj
dotnet add DialogEditor.WPF/DialogEditor.WPF.csproj reference DialogEditor.Core/DialogEditor.Core.csproj
```

- [ ] **Step 3: Add NuGet packages**

```bash
dotnet add DialogEditor.WPF/DialogEditor.WPF.csproj package Nodify --version 6.1.0
dotnet add DialogEditor.WPF/DialogEditor.WPF.csproj package CommunityToolkit.Mvvm --version 8.2.2
dotnet add DialogEditor.Tests/DialogEditor.Tests.csproj package coverlet.collector
```

- [ ] **Step 4: Clean up boilerplate**

Delete `DialogEditor.Core/Class1.cs`.
Delete `DialogEditor.Tests/UnitTest1.cs`.

- [ ] **Step 5: Verify build**

```bash
dotnet build DialogEditor.sln
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Create folder structure and commit**

```bash
mkdir DialogEditor.Core/Models DialogEditor.Core/Parsing DialogEditor.Core/GameData DialogEditor.Core/Layout
mkdir DialogEditor.Tests/Parsing DialogEditor.Tests/GameData DialogEditor.Tests/Layout
mkdir DialogEditor.WPF/ViewModels DialogEditor.WPF/Views DialogEditor.WPF/Services DialogEditor.WPF/Converters
git add -A
git commit -m "chore: solution scaffold — Core, Tests, WPF projects"
```

---

## Task 2: Core Models

**Files:**
- Create: `DialogEditor.Core/Models/NodeLink.cs`
- Create: `DialogEditor.Core/Models/StringEntry.cs`
- Create: `DialogEditor.Core/Models/StringTable.cs`
- Create: `DialogEditor.Core/Models/ConversationNode.cs`
- Create: `DialogEditor.Core/Models/Conversation.cs`

No tests needed — these are pure data containers with no logic.

- [ ] **Step 1: Write NodeLink and StringEntry**

`DialogEditor.Core/Models/NodeLink.cs`:
```csharp
namespace DialogEditor.Core.Models;

public record NodeLink(
    int FromNodeId,
    int ToNodeId,
    bool HasConditions
);
```

`DialogEditor.Core/Models/StringEntry.cs`:
```csharp
namespace DialogEditor.Core.Models;

public record StringEntry(
    int Id,
    string DefaultText,
    string FemaleText
);
```

- [ ] **Step 2: Write StringTable**

`DialogEditor.Core/Models/StringTable.cs`:
```csharp
namespace DialogEditor.Core.Models;

public class StringTable
{
    private readonly Dictionary<int, StringEntry> _entries;

    public static readonly StringTable Empty = new([]);

    public StringTable(IEnumerable<StringEntry> entries)
        => _entries = entries.ToDictionary(e => e.Id);

    public StringEntry? Get(int id) => _entries.GetValueOrDefault(id);

    public int Count => _entries.Count;
}
```

- [ ] **Step 3: Write ConversationNode and Conversation**

`DialogEditor.Core/Models/ConversationNode.cs`:
```csharp
namespace DialogEditor.Core.Models;

public record ConversationNode(
    int NodeId,
    bool IsPlayerChoice,
    string SpeakerGuid,
    string ListenerGuid,
    IReadOnlyList<NodeLink> Links,
    IReadOnlyList<string> ConditionStrings,
    int ScriptCount,
    string DisplayType,
    string Persistence
)
{
    public bool HasConditions => ConditionStrings.Count > 0;
    public bool HasScripts => ScriptCount > 0;
}
```

`DialogEditor.Core/Models/Conversation.cs`:
```csharp
namespace DialogEditor.Core.Models;

public record Conversation(
    string Name,
    IReadOnlyList<ConversationNode> Nodes,
    StringTable Strings
);
```

- [ ] **Step 4: Build and commit**

```bash
dotnet build DialogEditor.Core/DialogEditor.Core.csproj
git add -A
git commit -m "feat: core data models — ConversationNode, NodeLink, StringEntry, StringTable, Conversation"
```

Expected: `Build succeeded. 0 Error(s)`

---

## Task 3: ConditionFormatter

**Files:**
- Create: `DialogEditor.Core/Parsing/ConditionFormatter.cs`
- Create: `DialogEditor.Tests/Parsing/ConditionFormatterTests.cs`

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/Parsing/ConditionFormatterTests.cs`:
```csharp
using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class ConditionFormatterTests
{
    [Fact]
    public void Format_SimpleFunction_ReturnsFunctionWithParams()
    {
        var result = ConditionFormatter.Format(
            "Boolean IsGlobalValue(String, Operator, Int32)",
            ["some_flag", "EqualTo", "1"],
            not: false);

        Assert.Equal("IsGlobalValue(some_flag, EqualTo, 1)", result);
    }

    [Fact]
    public void Format_WithNot_PrefixesNOT()
    {
        var result = ConditionFormatter.Format(
            "Boolean IsCompanionActiveInParty(Guid)",
            ["b1a7e803-0000-0000-0000-000000000000"],
            not: true);

        Assert.Equal("NOT IsCompanionActiveInParty(b1a7e803-0000-0000-0000-000000000000)", result);
    }

    [Fact]
    public void Format_NoParameters_ReturnsEmptyParens()
    {
        var result = ConditionFormatter.Format(
            "Boolean SomeCheck()",
            [],
            not: false);

        Assert.Equal("SomeCheck()", result);
    }

    [Fact]
    public void Format_FunctionNameWithoutReturnType_ReturnsFullName()
    {
        var result = ConditionFormatter.Format(
            "IsReady",
            [],
            not: false);

        Assert.Equal("IsReady()", result);
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConditionFormatter"
```

Expected: compile error — `ConditionFormatter` does not exist yet.

- [ ] **Step 3: Implement ConditionFormatter**

`DialogEditor.Core/Parsing/ConditionFormatter.cs`:
```csharp
namespace DialogEditor.Core.Parsing;

public static class ConditionFormatter
{
    public static string Format(string fullName, IReadOnlyList<string> parameters, bool not)
    {
        var funcName = ExtractFunctionName(fullName);
        var paramStr = string.Join(", ", parameters);
        var condition = $"{funcName}({paramStr})";
        return not ? $"NOT {condition}" : condition;
    }

    private static string ExtractFunctionName(string fullName)
    {
        // "Boolean IsGlobalValue(String, Operator, Int32)" → "IsGlobalValue"
        var parenIdx = fullName.IndexOf('(');
        var nameSection = parenIdx > 0 ? fullName[..parenIdx] : fullName;
        var lastSpace = nameSection.LastIndexOf(' ');
        return lastSpace >= 0 ? nameSection[(lastSpace + 1)..] : nameSection;
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConditionFormatter"
```

Expected: `Passed: 4, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: ConditionFormatter — extracts and formats condition strings from OEI function signatures"
```

---

## Task 4: StringTableParser

**Files:**
- Create: `DialogEditor.Core/Parsing/StringTableParser.cs`
- Create: `DialogEditor.Tests/Parsing/StringTableParserTests.cs`

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/Parsing/StringTableParserTests.cs`:
```csharp
using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class StringTableParserTests
{
    private static readonly string MinimalXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <StringTableFile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                         xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <Name>test</Name>
          <EntryCount>2</EntryCount>
          <Entries>
            <Entry>
              <Language>english</Language>
              <ID>1</ID>
              <DefaultText>"Hello there."</DefaultText>
              <FemaleText />
            </Entry>
            <Entry>
              <Language>english</Language>
              <ID>2</ID>
              <DefaultText>"General Kenobi."</DefaultText>
              <FemaleText>"General Kenobi, you are a bold one."</FemaleText>
            </Entry>
          </Entries>
        </StringTableFile>
        """;

    [Fact]
    public void ParseXml_ReturnsCorrectEntryCount()
    {
        var table = StringTableParser.ParseXml(MinimalXml);
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void ParseXml_Entry1_HasCorrectDefaultText()
    {
        var table = StringTableParser.ParseXml(MinimalXml);
        Assert.Equal("\"Hello there.\"", table.Get(1)!.DefaultText);
    }

    [Fact]
    public void ParseXml_Entry2_HasFemaleText()
    {
        var table = StringTableParser.ParseXml(MinimalXml);
        Assert.Equal("\"General Kenobi, you are a bold one.\"", table.Get(2)!.FemaleText);
    }

    [Fact]
    public void ParseXml_Entry1_EmptyFemaleTextIsEmptyString()
    {
        var table = StringTableParser.ParseXml(MinimalXml);
        Assert.Equal(string.Empty, table.Get(1)!.FemaleText);
    }

    [Fact]
    public void ParseXml_MissingId_ReturnsNull()
    {
        var table = StringTableParser.ParseXml(MinimalXml);
        Assert.Null(table.Get(99));
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~StringTableParser"
```

Expected: compile error — `StringTableParser` not found.

- [ ] **Step 3: Implement StringTableParser**

`DialogEditor.Core/Parsing/StringTableParser.cs`:
```csharp
using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class StringTableParser
{
    public static StringTable ParseFile(string path)
        => ParseXml(File.ReadAllText(path));

    public static StringTable ParseXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var entries = doc.Descendants("Entry")
            .Select(e => new StringEntry(
                Id: (int)e.Element("ID")!,
                DefaultText: (string?)e.Element("DefaultText") ?? string.Empty,
                FemaleText: (string?)e.Element("FemaleText") ?? string.Empty
            ));
        return new StringTable(entries);
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~StringTableParser"
```

Expected: `Passed: 5, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: StringTableParser — parses .stringtable XML shared by both PoE1 and PoE2"
```

---

## Task 5: Poe1ConversationParser

**Files:**
- Create: `DialogEditor.Core/Parsing/Poe1ConversationParser.cs`
- Create: `DialogEditor.Tests/Parsing/Poe1ConversationParserTests.cs`

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/Parsing/Poe1ConversationParserTests.cs`:
```csharp
using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class Poe1ConversationParserTests
{
    private const string TwoNodeXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <ConversationData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <NextNodeID>2</NextNodeID>
          <Nodes>
            <FlowChartNode xsi:type="TalkNode">
              <NodeID>0</NodeID>
              <Comments />
              <PackageID>1</PackageID>
              <ContainerNodeID>-1</ContainerNodeID>
              <Links>
                <FlowChartLink xsi:type="DialogueLink">
                  <FromNodeID>0</FromNodeID>
                  <ToNodeID>1</ToNodeID>
                  <PointsToGhost>false</PointsToGhost>
                  <ClassExtender><ExtendedProperties /></ClassExtender>
                  <RandomWeight>1</RandomWeight>
                  <PlayQuestionNodeVO>true</PlayQuestionNodeVO>
                  <QuestionNodeTextDisplay>ShowOnce</QuestionNodeTextDisplay>
                </FlowChartLink>
              </Links>
              <ClassExtender><ExtendedProperties /></ClassExtender>
              <Conditionals><Operator>And</Operator><Components /></Conditionals>
              <OnEnterScripts />
              <OnExitScripts />
              <OnUpdateScripts />
              <NotSkippable>false</NotSkippable>
              <IsQuestionNode>false</IsQuestionNode>
              <IsTempText>false</IsTempText>
              <PlayVOAs3DSound>false</PlayVOAs3DSound>
              <PlayType>Normal</PlayType>
              <Persistence>None</Persistence>
              <NoPlayRandomWeight>0</NoPlayRandomWeight>
              <DisplayType>Conversation</DisplayType>
              <VOFilename /><VoiceType />
              <ExcludedSpeakerClasses /><ExcludedListenerClasses />
              <IncludedSpeakerClasses /><IncludedListenerClasses />
              <ActorDirection />
              <SpeakerGuid>fb6a7cbb-80b6-4b9c-8a99-41c8a031f380</SpeakerGuid>
              <ListenerGuid>b1a8e901-0000-0000-0000-000000000000</ListenerGuid>
            </FlowChartNode>
            <FlowChartNode xsi:type="TalkNode">
              <NodeID>1</NodeID>
              <Comments />
              <PackageID>1</PackageID>
              <ContainerNodeID>-1</ContainerNodeID>
              <Links />
              <ClassExtender><ExtendedProperties /></ClassExtender>
              <Conditionals>
                <Operator>And</Operator>
                <Components>
                  <ExpressionComponent xsi:type="ConditionalCall">
                    <Data>
                      <FullName>Boolean IsGlobalValue(String, Operator, Int32)</FullName>
                      <Parameters>
                        <string>some_flag</string>
                        <string>EqualTo</string>
                        <string>1</string>
                      </Parameters>
                    </Data>
                    <Not>false</Not>
                    <Operator>And</Operator>
                  </ExpressionComponent>
                </Components>
              </Conditionals>
              <OnEnterScripts />
              <OnExitScripts />
              <OnUpdateScripts />
              <NotSkippable>false</NotSkippable>
              <IsQuestionNode>true</IsQuestionNode>
              <IsTempText>false</IsTempText>
              <PlayVOAs3DSound>false</PlayVOAs3DSound>
              <PlayType>Normal</PlayType>
              <Persistence>OnceEver</Persistence>
              <NoPlayRandomWeight>0</NoPlayRandomWeight>
              <DisplayType>Bark</DisplayType>
              <VOFilename /><VoiceType />
              <ExcludedSpeakerClasses /><ExcludedListenerClasses />
              <IncludedSpeakerClasses /><IncludedListenerClasses />
              <ActorDirection />
              <SpeakerGuid>b1a8e901-0000-0000-0000-000000000000</SpeakerGuid>
              <ListenerGuid>fb6a7cbb-80b6-4b9c-8a99-41c8a031f380</ListenerGuid>
            </FlowChartNode>
          </Nodes>
        </ConversationData>
        """;

    [Fact]
    public void Parse_ReturnsTwoNodes()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.Equal(2, nodes.Count);
    }

    [Fact]
    public void Parse_Node0_IsNotPlayerChoice()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.False(nodes[0].IsPlayerChoice);
    }

    [Fact]
    public void Parse_Node1_IsPlayerChoice()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.True(nodes[1].IsPlayerChoice);
    }

    [Fact]
    public void Parse_Node0_HasOneLink()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.Single(nodes[0].Links);
        Assert.Equal(1, nodes[0].Links[0].ToNodeId);
    }

    [Fact]
    public void Parse_Node1_HasCondition()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.True(nodes[1].HasConditions);
        Assert.Single(nodes[1].ConditionStrings);
        Assert.Equal("IsGlobalValue(some_flag, EqualTo, 1)", nodes[1].ConditionStrings[0]);
    }

    [Fact]
    public void Parse_Node1_HasCorrectDisplayTypeAndPersistence()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.Equal("Bark", nodes[1].DisplayType);
        Assert.Equal("OnceEver", nodes[1].Persistence);
    }

    [Fact]
    public void Parse_Node0_SpeakerGuidCorrect()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.Equal("fb6a7cbb-80b6-4b9c-8a99-41c8a031f380", nodes[0].SpeakerGuid);
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe1ConversationParser"
```

Expected: compile error — `Poe1ConversationParser` not found.

- [ ] **Step 3: Implement Poe1ConversationParser**

`DialogEditor.Core/Parsing/Poe1ConversationParser.cs`:
```csharp
using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe1ConversationParser
{
    public static IReadOnlyList<ConversationNode> ParseFile(string path)
        => ParseXml(File.ReadAllText(path));

    public static IReadOnlyList<ConversationNode> ParseXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants("FlowChartNode")
            .Select(ParseNode)
            .ToList();
    }

    private static ConversationNode ParseNode(XElement node)
    {
        var links = node.Element("Links")?.Elements("FlowChartLink")
            .Select(ParseLink)
            .ToList() ?? [];

        var conditions = node.Element("Conditionals")?.Element("Components")
            ?.Elements("ExpressionComponent")
            .Select(ParseCondition)
            .ToList() ?? [];

        var scriptCount =
            (node.Element("OnEnterScripts")?.HasElements == true ? 1 : 0) +
            (node.Element("OnExitScripts")?.HasElements == true ? 1 : 0) +
            (node.Element("OnUpdateScripts")?.HasElements == true ? 1 : 0);

        return new ConversationNode(
            NodeId: (int)node.Element("NodeID")!,
            IsPlayerChoice: (bool)node.Element("IsQuestionNode")!,
            SpeakerGuid: (string?)node.Element("SpeakerGuid") ?? string.Empty,
            ListenerGuid: (string?)node.Element("ListenerGuid") ?? string.Empty,
            Links: links,
            ConditionStrings: conditions,
            ScriptCount: scriptCount,
            DisplayType: (string?)node.Element("DisplayType") ?? string.Empty,
            Persistence: (string?)node.Element("Persistence") ?? string.Empty
        );
    }

    private static NodeLink ParseLink(XElement link)
        => new(
            FromNodeId: (int)link.Element("FromNodeID")!,
            ToNodeId: (int)link.Element("ToNodeID")!,
            HasConditions: link.Element("Conditionals")?.Element("Components")?.HasElements == true
        );

    private static string ParseCondition(XElement component)
    {
        var data = component.Element("Data")!;
        var fullName = (string)data.Element("FullName")!;
        var parameters = data.Element("Parameters")?.Elements("string")
            .Select(e => (string)e)
            .ToList() ?? [];
        var not = (bool?)component.Element("Not") ?? false;
        return ConditionFormatter.Format(fullName, parameters, not);
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe1ConversationParser"
```

Expected: `Passed: 7, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: Poe1ConversationParser — parses .conversation XML into ConversationNode list"
```

---

## Task 6: Poe2ConversationParser

**Files:**
- Create: `DialogEditor.Core/Parsing/Poe2ConversationParser.cs`
- Create: `DialogEditor.Tests/Parsing/Poe2ConversationParserTests.cs`

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/Parsing/Poe2ConversationParserTests.cs`:
```csharp
using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class Poe2ConversationParserTests
{
    private const string TwoNodeJson = """
        {"Conversations": [{
          "ID": "daa2b624-875f-49bb-a041-ded56da97bea",
          "Filename": "Conversations/Test/test.conversation",
          "ConversationScriptNode": {
            "NodeID": -200, "ContainerNodeID": -1, "Links": [],
            "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
            "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
            "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0, "VOPositioning": 0
          },
          "ConversationType": 0,
          "CharacterMappings": [],
          "Nodes": [
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "fbeeeff7-ec6a-4a40-a47f-1843eaffc6ae",
              "ListenerGuid": "b1a8e901-0000-0000-0000-000000000000",
              "IsQuestionNode": false,
              "DisplayType": 0,
              "Persistence": 0,
              "NodeID": 0, "ContainerNodeID": -1,
              "Links": [{
                "$type": "OEIFormats.FlowCharts.Conversations.DialogueLink, OEIFormats",
                "FromNodeID": 0, "ToNodeID": 1, "PointsToGhost": false,
                "Conditionals": {"Operator": 0, "Components": []},
                "ClassExtender": {"ExtendedProperties": []},
                "RandomWeight": 1, "PlayQuestionNodeVO": true, "QuestionNodeTextDisplay": 0
              }],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
              "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0,
              "VOPositioning": 0, "EmotionType": "", "EmotionStrength": 0.5,
              "PersistEmotion": true, "EmotionDelay": 0.0, "ExternalVO": "", "HasVO": false
            },
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "b1a8e901-0000-0000-0000-000000000000",
              "ListenerGuid": "fbeeeff7-ec6a-4a40-a47f-1843eaffc6ae",
              "IsQuestionNode": true,
              "DisplayType": 1,
              "Persistence": 1,
              "NodeID": 1, "ContainerNodeID": -1,
              "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {
                "Operator": 0,
                "Components": [{
                  "$type": "OEIFormats.FlowCharts.ConditionalCall, OEIFormats",
                  "Data": {
                    "FullName": "Boolean IsGlobalValue(String, Operator, Int32)",
                    "Parameters": ["some_flag", "EqualTo", "1"]
                  },
                  "Not": false,
                  "Operator": 0
                }]
              },
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
              "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0,
              "VOPositioning": 0, "EmotionType": "", "EmotionStrength": 0.5,
              "PersistEmotion": true, "EmotionDelay": 0.0, "ExternalVO": "", "HasVO": false
            }
          ]
        }]}
        """;

    [Fact]
    public void Parse_SkipsScriptNode_ReturnsTwoRealNodes()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.Equal(2, nodes.Count);
    }

    [Fact]
    public void Parse_Node0_IsNotPlayerChoice()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.False(nodes[0].IsPlayerChoice);
    }

    [Fact]
    public void Parse_Node1_IsPlayerChoice()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.True(nodes[1].IsPlayerChoice);
    }

    [Fact]
    public void Parse_Node1_DisplayTypeAndPersistenceMappedToStrings()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.Equal("Bark", nodes[1].DisplayType);
        Assert.Equal("OnceEver", nodes[1].Persistence);
    }

    [Fact]
    public void Parse_Node0_HasOneLink()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.Single(nodes[0].Links);
        Assert.Equal(1, nodes[0].Links[0].ToNodeId);
    }

    [Fact]
    public void Parse_Node1_HasCondition()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.True(nodes[1].HasConditions);
        Assert.Equal("IsGlobalValue(some_flag, EqualTo, 1)", nodes[1].ConditionStrings[0]);
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2ConversationParser"
```

Expected: compile error — `Poe2ConversationParser` not found.

- [ ] **Step 3: Implement Poe2ConversationParser**

`DialogEditor.Core/Parsing/Poe2ConversationParser.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe2ConversationParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<ConversationNode> ParseFile(string path)
        => ParseJson(File.ReadAllText(path));

    public static IReadOnlyList<ConversationNode> ParseJson(string json)
    {
        var root = JsonNode.Parse(json)!;
        var firstConversation = root["Conversations"]![0]!;
        var nodes = firstConversation["Nodes"]!.AsArray();

        return nodes
            .Where(n => (n!["NodeID"]?.GetValue<int>() ?? 0) >= 0)
            .Select(ParseNode)
            .ToList();
    }

    private static ConversationNode ParseNode(JsonNode? node)
    {
        var links = node!["Links"]?.AsArray()
            .Select(ParseLink)
            .ToList() ?? [];

        var conditions = node["Conditionals"]?["Components"]?.AsArray()
            .Select(ParseCondition)
            .ToList() ?? [];

        var scriptCount =
            (node["OnEnterScripts"]?.AsArray().Count > 0 ? 1 : 0) +
            (node["OnExitScripts"]?.AsArray().Count > 0 ? 1 : 0) +
            (node["OnUpdateScripts"]?.AsArray().Count > 0 ? 1 : 0);

        return new ConversationNode(
            NodeId: node["NodeID"]!.GetValue<int>(),
            IsPlayerChoice: node["IsQuestionNode"]?.GetValue<bool>() ?? false,
            SpeakerGuid: node["SpeakerGuid"]?.GetValue<string>() ?? string.Empty,
            ListenerGuid: node["ListenerGuid"]?.GetValue<string>() ?? string.Empty,
            Links: links,
            ConditionStrings: conditions,
            ScriptCount: scriptCount,
            DisplayType: MapDisplayType(node["DisplayType"]?.GetValue<int>() ?? 0),
            Persistence: MapPersistence(node["Persistence"]?.GetValue<int>() ?? 0)
        );
    }

    private static NodeLink ParseLink(JsonNode? link)
        => new(
            FromNodeId: link!["FromNodeID"]!.GetValue<int>(),
            ToNodeId: link["ToNodeID"]!.GetValue<int>(),
            HasConditions: link["Conditionals"]?["Components"]?.AsArray().Count > 0
        );

    private static string ParseCondition(JsonNode? component)
    {
        var data = component!["Data"]!;
        var fullName = data["FullName"]!.GetValue<string>();
        var parameters = data["Parameters"]?.AsArray()
            .Select(p => p!.GetValue<string>())
            .ToList() ?? [];
        var not = component["Not"]?.GetValue<bool>() ?? false;
        return ConditionFormatter.Format(fullName, parameters, not);
    }

    private static string MapDisplayType(int value) => value switch
    {
        0 => "Conversation",
        1 => "Bark",
        _ => $"Unknown({value})"
    };

    private static string MapPersistence(int value) => value switch
    {
        0 => "None",
        1 => "OnceEver",
        _ => $"Unknown({value})"
    };
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2ConversationParser"
```

Expected: `Passed: 6, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: Poe2ConversationParser — parses .conversationbundle JSON into ConversationNode list"
```

---

## Task 7: GameData — ConversationFile, IGameDataProvider, Factory, and Providers

**Files:**
- Create: `DialogEditor.Core/GameData/ConversationFile.cs`
- Create: `DialogEditor.Core/GameData/IGameDataProvider.cs`
- Create: `DialogEditor.Core/GameData/GameDataProviderFactory.cs`
- Create: `DialogEditor.Core/GameData/Poe1GameDataProvider.cs`
- Create: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`
- Create: `DialogEditor.Tests/GameData/GameDataProviderFactoryTests.cs`

- [ ] **Step 1: Write models and interface**

`DialogEditor.Core/GameData/ConversationFile.cs`:
```csharp
namespace DialogEditor.Core.GameData;

public record ConversationFile(
    string Name,
    string FolderPath,
    string ConversationPath,
    string StringTablePath
);
```

`DialogEditor.Core/GameData/IGameDataProvider.cs`:
```csharp
using DialogEditor.Core.Models;

namespace DialogEditor.Core.GameData;

public interface IGameDataProvider
{
    string GameName { get; }
    IReadOnlyList<ConversationFile> EnumerateConversations();
    Conversation LoadConversation(ConversationFile file);
}
```

- [ ] **Step 2: Write factory tests**

`DialogEditor.Tests/GameData/GameDataProviderFactoryTests.cs`:
```csharp
using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.GameData;

public class GameDataProviderFactoryTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Fact]
    public void Detect_Poe1DataFolder_ReturnsPoe1Provider()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "PillarsOfEternity_Data"));
        var provider = GameDataProviderFactory.Detect(_tempRoot);
        Assert.NotNull(provider);
        Assert.Equal("Pillars of Eternity", provider.GameName);
    }

    [Fact]
    public void Detect_Poe2DataFolder_ReturnsPoe2Provider()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "PillarsOfEternityII_Data"));
        var provider = GameDataProviderFactory.Detect(_tempRoot);
        Assert.NotNull(provider);
        Assert.Equal("Pillars of Eternity II: Deadfire", provider.GameName);
    }

    [Fact]
    public void Detect_UnrecognisedFolder_ReturnsNull()
    {
        Directory.CreateDirectory(_tempRoot);
        var provider = GameDataProviderFactory.Detect(_tempRoot);
        Assert.Null(provider);
    }
}
```

- [ ] **Step 3: Run tests — expect failure**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~GameDataProviderFactory"
```

Expected: compile error — `GameDataProviderFactory` not found.

- [ ] **Step 4: Implement factory and providers**

`DialogEditor.Core/GameData/GameDataProviderFactory.cs`:
```csharp
namespace DialogEditor.Core.GameData;

public static class GameDataProviderFactory
{
    public static IGameDataProvider? Detect(string rootPath)
    {
        if (Directory.Exists(Path.Combine(rootPath, "PillarsOfEternity_Data")))
            return new Poe1GameDataProvider(rootPath);
        if (Directory.Exists(Path.Combine(rootPath, "PillarsOfEternityII_Data")))
            return new Poe2GameDataProvider(rootPath);
        return null;
    }
}
```

`DialogEditor.Core/GameData/Poe1GameDataProvider.cs`:
```csharp
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;

namespace DialogEditor.Core.GameData;

public class Poe1GameDataProvider(string rootPath) : IGameDataProvider
{
    public string GameName => "Pillars of Eternity";

    private string DataRoot => Path.Combine(rootPath, "PillarsOfEternity_Data", "data");
    private string ConversationsRoot => Path.Combine(DataRoot, "conversations");
    private string StringTablesRoot => Path.Combine(DataRoot, "localized", "en", "text", "conversations");

    public IReadOnlyList<ConversationFile> EnumerateConversations()
    {
        if (!Directory.Exists(ConversationsRoot)) return [];

        return Directory
            .EnumerateFiles(ConversationsRoot, "*.conversation", SearchOption.AllDirectories)
            .Select(BuildConversationFile)
            .OrderBy(f => f.FolderPath)
            .ThenBy(f => f.Name)
            .ToList();
    }

    private ConversationFile BuildConversationFile(string convPath)
    {
        var relative = Path.GetRelativePath(ConversationsRoot, convPath);
        var withoutExt = Path.ChangeExtension(relative, null);
        var stPath = Path.Combine(StringTablesRoot, withoutExt + ".stringtable");
        return new ConversationFile(
            Name: Path.GetFileNameWithoutExtension(convPath),
            FolderPath: Path.GetDirectoryName(relative) ?? string.Empty,
            ConversationPath: convPath,
            StringTablePath: stPath
        );
    }

    public Conversation LoadConversation(ConversationFile file)
    {
        var nodes = Poe1ConversationParser.ParseFile(file.ConversationPath);
        var strings = File.Exists(file.StringTablePath)
            ? StringTableParser.ParseFile(file.StringTablePath)
            : StringTable.Empty;
        return new Conversation(file.Name, nodes, strings);
    }
}
```

`DialogEditor.Core/GameData/Poe2GameDataProvider.cs`:
```csharp
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;

namespace DialogEditor.Core.GameData;

public class Poe2GameDataProvider(string rootPath) : IGameDataProvider
{
    public string GameName => "Pillars of Eternity II: Deadfire";

    private string ExportedRoot => Path.Combine(rootPath, "PillarsOfEternityII_Data", "exported");
    private string ConversationsRoot => Path.Combine(ExportedRoot, "design", "conversations");
    private string StringTablesRoot => Path.Combine(ExportedRoot, "localized", "en", "text", "conversations");

    public IReadOnlyList<ConversationFile> EnumerateConversations()
    {
        if (!Directory.Exists(ConversationsRoot)) return [];

        return Directory
            .EnumerateFiles(ConversationsRoot, "*.conversationbundle", SearchOption.AllDirectories)
            .Select(BuildConversationFile)
            .OrderBy(f => f.FolderPath)
            .ThenBy(f => f.Name)
            .ToList();
    }

    private ConversationFile BuildConversationFile(string convPath)
    {
        var relative = Path.GetRelativePath(ConversationsRoot, convPath);
        var withoutExt = Path.ChangeExtension(relative, null);
        var stPath = Path.Combine(StringTablesRoot, withoutExt + ".stringtable");
        return new ConversationFile(
            Name: Path.GetFileNameWithoutExtension(convPath),
            FolderPath: Path.GetDirectoryName(relative) ?? string.Empty,
            ConversationPath: convPath,
            StringTablePath: stPath
        );
    }

    public Conversation LoadConversation(ConversationFile file)
    {
        var nodes = Poe2ConversationParser.ParseFile(file.ConversationPath);
        var strings = File.Exists(file.StringTablePath)
            ? StringTableParser.ParseFile(file.StringTablePath)
            : StringTable.Empty;
        return new Conversation(file.Name, nodes, strings);
    }
}
```

- [ ] **Step 5: Run tests — expect pass**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~GameDataProviderFactory"
```

Expected: `Passed: 3, Failed: 0`

- [ ] **Step 6: Run all tests**

```bash
dotnet test DialogEditor.Tests
```

Expected: all tests green.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: game data providers — detect game, enumerate conversations, resolve stringtable paths"
```

---

## Task 8: AutoLayoutService

**Files:**
- Create: `DialogEditor.Core/Layout/AutoLayoutService.cs`
- Create: `DialogEditor.Tests/Layout/AutoLayoutServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/Layout/AutoLayoutServiceTests.cs`:
```csharp
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Layout;

public class AutoLayoutServiceTests
{
    private const double NodeWidth = 220;
    private const double NodeHeight = 110;
    private const double HGap = 60;
    private const double VGap = 20;

    private static ConversationNode Node(int id, params int[] toIds) =>
        new(id, false, "", "", toIds.Select(t => new NodeLink(id, t, false)).ToList(),
            [], 0, "", "");

    [Fact]
    public void Apply_SingleNode_PlacedAtOrigin()
    {
        var nodes = new[] { Node(0) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        Assert.Equal(0, positions[0].X);
        Assert.Equal(0, positions[0].Y);
    }

    [Fact]
    public void Apply_TwoConnectedNodes_SecondIsRightOfFirst()
    {
        var nodes = new[] { Node(0, 1), Node(1) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        Assert.Equal(0, positions[0].X);
        Assert.True(positions[1].X > positions[0].X);
    }

    [Fact]
    public void Apply_TwoNodesInSameLayer_VerticallySpaced()
    {
        // Both node 1 and node 2 are children of node 0 — same layer
        var nodes = new[] { Node(0, 1, 2), Node(1), Node(2) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        var yDiff = Math.Abs(positions[1].Y - positions[2].Y);
        Assert.True(yDiff >= NodeHeight + VGap);
    }

    [Fact]
    public void Apply_ThreeNodeChain_XPositionsIncreaseMonotonically()
    {
        var nodes = new[] { Node(0, 1), Node(1, 2), Node(2) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        Assert.True(positions[1].X > positions[0].X);
        Assert.True(positions[2].X > positions[1].X);
    }

    [Fact]
    public void Apply_AllNodesReceivePosition()
    {
        var nodes = new[] { Node(0, 1), Node(1, 2), Node(2) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        Assert.Equal(3, positions.Count);
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AutoLayoutService"
```

Expected: compile error — `AutoLayoutService` not found.

- [ ] **Step 3: Implement AutoLayoutService**

`DialogEditor.Core/Layout/AutoLayoutService.cs`:
```csharp
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Layout;

public static class AutoLayoutService
{
    private const double NodeWidth = 220;
    private const double NodeHeight = 110;
    private const double HorizontalGap = 60;
    private const double VerticalGap = 20;

    public static void Apply(
        IReadOnlyList<ConversationNode> nodes,
        Action<int, double, double> setLocation)
    {
        var layers = AssignLayers(nodes);
        var byLayer = layers
            .GroupBy(kv => kv.Value, kv => kv.Key)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var layer in byLayer)
        {
            var nodeIds = layer.ToList();
            var x = layer.Key * (NodeWidth + HorizontalGap);
            var totalHeight = nodeIds.Count * NodeHeight + (nodeIds.Count - 1) * VerticalGap;
            var startY = -totalHeight / 2.0;

            for (var i = 0; i < nodeIds.Count; i++)
            {
                var y = startY + i * (NodeHeight + VerticalGap);
                setLocation(nodeIds[i], x, y);
            }
        }
    }

    private static Dictionary<int, int> AssignLayers(IReadOnlyList<ConversationNode> nodes)
    {
        var targeted = nodes
            .SelectMany(n => n.Links.Select(l => l.ToNodeId))
            .ToHashSet();

        var layers = new Dictionary<int, int>();
        var queue = new Queue<int>();

        foreach (var node in nodes.Where(n => !targeted.Contains(n.NodeId)))
        {
            layers[node.NodeId] = 0;
            queue.Enqueue(node.NodeId);
        }

        // If no roots found (circular graph), start from first node
        if (layers.Count == 0 && nodes.Count > 0)
        {
            layers[nodes[0].NodeId] = 0;
            queue.Enqueue(nodes[0].NodeId);
        }

        var nodeMap = nodes.ToDictionary(n => n.NodeId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!nodeMap.TryGetValue(current, out var node)) continue;

            foreach (var link in node.Links)
            {
                if (!layers.ContainsKey(link.ToNodeId))
                {
                    layers[link.ToNodeId] = layers[current] + 1;
                    queue.Enqueue(link.ToNodeId);
                }
            }
        }

        // Assign remaining disconnected nodes to their own layer
        var maxLayer = layers.Count > 0 ? layers.Values.Max() : 0;
        foreach (var node in nodes.Where(n => !layers.ContainsKey(n.NodeId)))
            layers[node.NodeId] = ++maxLayer;

        return layers;
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AutoLayoutService"
```

Expected: `Passed: 5, Failed: 0`

- [ ] **Step 5: Run all tests**

```bash
dotnet test DialogEditor.Tests
```

Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: AutoLayoutService — layered left-to-right layout with callback pattern (no WPF dependency)"
```

---

## Task 9: WPF Shell

**Files:**
- Modify: `DialogEditor.WPF/App.xaml`
- Modify: `DialogEditor.WPF/App.xaml.cs`
- Modify: `DialogEditor.WPF/Views/MainWindow.xaml`
- Modify: `DialogEditor.WPF/Views/MainWindow.xaml.cs`

- [ ] **Step 1: Move MainWindow to Views folder**

Move the generated `MainWindow.xaml` and `MainWindow.xaml.cs` into `DialogEditor.WPF/Views/`. Update `App.xaml` to reference the new location.

`DialogEditor.WPF/App.xaml`:
```xml
<Application x:Class="DialogEditor.WPF.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="Views/MainWindow.xaml">
    <Application.Resources />
</Application>
```

- [ ] **Step 2: Set up MainWindow three-panel skeleton**

`DialogEditor.WPF/Views/MainWindow.xaml`:
```xml
<Window x:Class="DialogEditor.WPF.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:DialogEditor.WPF.Views"
        Title="Pillars Dialog Editor"
        Width="1400" Height="850"
        Background="#1e1e1e">

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="220" MinWidth="150"/>
            <ColumnDefinition Width="4"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="4"/>
            <ColumnDefinition Width="240" MinWidth="180"/>
        </Grid.ColumnDefinitions>

        <!-- Left panel: conversation browser -->
        <views:GameBrowserView Grid.Column="0"
                               DataContext="{Binding Browser}"/>

        <GridSplitter Grid.Column="1" HorizontalAlignment="Stretch" Background="#333"/>

        <!-- Center: Nodify canvas -->
        <views:ConversationView Grid.Column="2"
                                DataContext="{Binding Canvas}"/>

        <GridSplitter Grid.Column="3" HorizontalAlignment="Stretch" Background="#333"/>

        <!-- Right panel: node detail -->
        <views:NodeDetailView Grid.Column="4"
                              DataContext="{Binding Detail}"/>
    </Grid>
</Window>
```

`DialogEditor.WPF/Views/MainWindow.xaml.cs`:
```csharp
using System.Windows;
using DialogEditor.WPF.ViewModels;

namespace DialogEditor.WPF.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build DialogEditor.WPF/DialogEditor.WPF.csproj
```

Expected: `Build succeeded` (views don't exist yet — add them as empty UserControls if needed to unblock the build).

Create stub views so the project compiles. For each stub, create the `.xaml` and `.xaml.cs`:

`DialogEditor.WPF/Views/GameBrowserView.xaml`:
```xml
<UserControl x:Class="DialogEditor.WPF.Views.GameBrowserView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Border Background="#2d2d2d"/>
</UserControl>
```

`DialogEditor.WPF/Views/GameBrowserView.xaml.cs`:
```csharp
using System.Windows.Controls;
namespace DialogEditor.WPF.Views;
public partial class GameBrowserView : UserControl
{
    public GameBrowserView() => InitializeComponent();
}
```

Repeat for `ConversationView.xaml` and `NodeDetailView.xaml` (same pattern, different class names).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: WPF shell — three-panel MainWindow with stub views"
```

---

## Task 10: SpeakerNameService

**Files:**
- Create: `DialogEditor.WPF/Services/SpeakerNameService.cs`

- [ ] **Step 1: Implement SpeakerNameService**

`DialogEditor.WPF/Services/SpeakerNameService.cs`:
```csharp
namespace DialogEditor.WPF.Services;

public static class SpeakerNameService
{
    private static readonly Dictionary<string, string> KnownGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        // Special
        { "b1a8e901-0000-0000-0000-000000000000", "Player" },

        // PoE1 companions
        { "fb6a7cbb-80b6-4b9c-8a99-41c8a031f380", "Aloth" },
        { "7c720723-c4eb-48d3-b082-498e9454c92e", "Iselmyr" },
        { "b1a7e803-0000-0000-0000-000000000000", "Eder" },
        { "b1a7e808-0000-0000-0000-000000000000", "Durance" },
        { "b1a7e804-0000-0000-0000-000000000000", "Grieving Mother" },
        { "b1a7e805-0000-0000-0000-000000000000", "Hiravias" },
        { "b1a7e806-0000-0000-0000-000000000000", "Kana Rua" },
        { "b1a7e807-0000-0000-0000-000000000000", "Sagani" },
        { "b1a7e809-0000-0000-0000-000000000000", "Pallegina" },
        { "b1a7e80a-0000-0000-0000-000000000000", "Devil of Caroc" },
        { "b1a7e80b-0000-0000-0000-000000000000", "Zahua" },
        { "b1a7e80c-0000-0000-0000-000000000000", "Maneha" },

        // PoE2 companions
        { "fbeeeff7-ec6a-4a40-a47f-1843eaffc6ae", "Vela" },
        { "5529e4b7-42dc-4895-b9f8-23375a945413", "Aloth (PoE2)" },
        { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "Eder (PoE2)" },
        { "f1504d00-eb4f-423a-9c9b-e71b5b23adcc", "Xoti" },
        { "4d0750be-85ea-4838-8e52-666448927e83", "Serafen" },
        { "e41c506b-abcc-45f8-98ab-bba00a0ebc16", "Pallegina (PoE2)" },
        { "09b41c25-ce0a-4568-8f6b-2263f8a7493c", "Maia Rua" },
        { "688aa86c-fbe6-4a7f-9dd0-7ef3f8c943f4", "Tekehu" },
    };

    public static string Resolve(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return "Unknown";
        if (KnownGuids.TryGetValue(guid, out var name)) return name;
        return guid.Length >= 8 ? guid[..8] + "…" : guid;
    }
}
```

- [ ] **Step 2: Build and commit**

```bash
dotnet build DialogEditor.WPF/DialogEditor.WPF.csproj
git add -A
git commit -m "feat: SpeakerNameService — resolves companion GUIDs to readable names for both games"
```

---

## Task 11: NodeViewModel, ConnectorViewModel, ConnectionViewModel

**Files:**
- Create: `DialogEditor.WPF/ViewModels/ConnectorViewModel.cs`
- Create: `DialogEditor.WPF/ViewModels/NodeViewModel.cs`
- Create: `DialogEditor.WPF/ViewModels/ConnectionViewModel.cs`
- Create: `DialogEditor.WPF/Converters/BoolToHeaderBrushConverter.cs`

- [ ] **Step 1: Create ConnectorViewModel**

`DialogEditor.WPF/ViewModels/ConnectorViewModel.cs`:
```csharp
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.WPF.ViewModels;

public partial class ConnectorViewModel : ObservableObject
{
    [ObservableProperty]
    private Point _anchor;
}
```

- [ ] **Step 2: Create NodeViewModel**

`DialogEditor.WPF/ViewModels/NodeViewModel.cs`:
```csharp
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;
using DialogEditor.WPF.Services;

namespace DialogEditor.WPF.ViewModels;

public partial class NodeViewModel : ObservableObject
{
    public int NodeId { get; }
    public bool IsPlayerChoice { get; }
    public string SpeakerGuid { get; }
    public string ListenerGuid { get; }
    public string SpeakerName { get; }
    public string ListenerName { get; }
    public string Title { get; }
    public string TextPreview { get; }
    public string DefaultText { get; }
    public string FemaleText { get; }
    public string FooterText { get; }
    public string DisplayType { get; }
    public string Persistence { get; }
    public IReadOnlyList<string> ConditionStrings { get; }
    public int ScriptCount { get; }
    public IReadOnlyList<NodeLink> Links { get; }

    public ConnectorViewModel Input { get; } = new();
    public ConnectorViewModel Output { get; } = new();
    public IReadOnlyList<ConnectorViewModel> Inputs { get; }
    public IReadOnlyList<ConnectorViewModel> Outputs { get; }

    [ObservableProperty]
    private Point _location;

    [ObservableProperty]
    private bool _isSelected;

    internal Action<NodeViewModel>? OnSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) OnSelected?.Invoke(this);
    }

    public NodeViewModel(ConversationNode node, StringEntry? entry)
    {
        NodeId = node.NodeId;
        IsPlayerChoice = node.IsPlayerChoice;
        SpeakerGuid = node.SpeakerGuid;
        ListenerGuid = node.ListenerGuid;
        SpeakerName = SpeakerNameService.Resolve(node.SpeakerGuid);
        ListenerName = SpeakerNameService.Resolve(node.ListenerGuid);
        ConditionStrings = node.ConditionStrings;
        ScriptCount = node.ScriptCount;
        DisplayType = node.DisplayType;
        Persistence = node.Persistence;
        Links = node.Links;

        var suffix = node.IsPlayerChoice ? " ✦" : string.Empty;
        Title = $"Node {node.NodeId} · {SpeakerName}{suffix}";

        DefaultText = entry?.DefaultText ?? "[text unavailable — stringtable not found]";
        FemaleText = entry?.FemaleText ?? string.Empty;
        TextPreview = DefaultText.Length > 80 ? DefaultText[..80] + "…" : DefaultText;

        FooterText = node.ConditionStrings.Count > 0
            ? $"⚙ {node.ConditionStrings.Count} condition{(node.ConditionStrings.Count == 1 ? "" : "s")}"
            : "[ No conditions ]";

        Inputs = [Input];
        Outputs = [Output];
    }
}
```

- [ ] **Step 3: Create ConnectionViewModel**

`DialogEditor.WPF/ViewModels/ConnectionViewModel.cs`:
```csharp
namespace DialogEditor.WPF.ViewModels;

public record ConnectionViewModel(ConnectorViewModel Source, ConnectorViewModel Target);
```

- [ ] **Step 4: Create BoolToHeaderBrushConverter**

`DialogEditor.WPF/Converters/BoolToHeaderBrushConverter.cs`:
```csharp
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DialogEditor.WPF.Converters;

[ValueConversion(typeof(bool), typeof(SolidColorBrush))]
public class BoolToHeaderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NpcBrush =
        new(Color.FromRgb(0x7b, 0x24, 0x1c));
    private static readonly SolidColorBrush PlayerBrush =
        new(Color.FromRgb(0x1a, 0x52, 0x76));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? PlayerBrush : NpcBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

- [ ] **Step 5: Build and commit**

```bash
dotnet build DialogEditor.WPF/DialogEditor.WPF.csproj
git add -A
git commit -m "feat: NodeViewModel, ConnectorViewModel, ConnectionViewModel, BoolToHeaderBrushConverter"
```

---

## Task 12: GameBrowserViewModel and ConversationViewModel

**Files:**
- Create: `DialogEditor.WPF/ViewModels/ConversationFolderViewModel.cs`
- Create: `DialogEditor.WPF/ViewModels/ConversationItemViewModel.cs`
- Create: `DialogEditor.WPF/ViewModels/GameBrowserViewModel.cs`
- Create: `DialogEditor.WPF/ViewModels/ConversationViewModel.cs`

- [ ] **Step 1: Create folder/item ViewModels**

`DialogEditor.WPF/ViewModels/ConversationItemViewModel.cs`:
```csharp
using DialogEditor.Core.GameData;

namespace DialogEditor.WPF.ViewModels;

public class ConversationItemViewModel(ConversationFile file)
{
    public string Name { get; } = file.Name;
    public ConversationFile File { get; } = file;
}
```

`DialogEditor.WPF/ViewModels/ConversationFolderViewModel.cs`:
```csharp
using System.Collections.ObjectModel;

namespace DialogEditor.WPF.ViewModels;

public class ConversationFolderViewModel(string folderPath)
{
    public string FolderPath { get; } = folderPath;
    public string DisplayName { get; } = string.IsNullOrEmpty(folderPath) ? "(root)" : folderPath;
    public ObservableCollection<ConversationItemViewModel> Items { get; } = [];
}
```

- [ ] **Step 2: Create GameBrowserViewModel**

`DialogEditor.WPF/ViewModels/GameBrowserViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.GameData;

namespace DialogEditor.WPF.ViewModels;

public partial class GameBrowserViewModel : ObservableObject
{
    public ObservableCollection<ConversationFolderViewModel> Folders { get; } = [];

    [ObservableProperty]
    private string _gameName = string.Empty;

    public event Action<ConversationFile>? ConversationSelected;

    [ObservableProperty]
    private ConversationItemViewModel? _selectedItem;

    partial void OnSelectedItemChanged(ConversationItemViewModel? value)
    {
        if (value is not null)
            ConversationSelected?.Invoke(value.File);
    }

    public void Load(IGameDataProvider provider)
    {
        GameName = provider.GameName;
        Folders.Clear();

        var byFolder = provider.EnumerateConversations()
            .GroupBy(f => f.FolderPath)
            .OrderBy(g => g.Key);

        foreach (var group in byFolder)
        {
            var folder = new ConversationFolderViewModel(group.Key);
            foreach (var file in group)
                folder.Items.Add(new ConversationItemViewModel(file));
            Folders.Add(folder);
        }
    }
}
```

- [ ] **Step 3: Create ConversationViewModel**

`DialogEditor.WPF/ViewModels/ConversationViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;

namespace DialogEditor.WPF.ViewModels;

public partial class ConversationViewModel : ObservableObject
{
    public ObservableCollection<NodeViewModel> Nodes { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    [ObservableProperty]
    private NodeViewModel? _selectedNode;

    public void Load(Conversation conversation)
    {
        Nodes.Clear();
        Connections.Clear();
        SelectedNode = null;

        var nodeMap = new Dictionary<int, NodeViewModel>();

        foreach (var node in conversation.Nodes)
        {
            var entry = conversation.Strings.Get(node.NodeId);
            var vm = new NodeViewModel(node, entry);
            vm.OnSelected = n => SelectedNode = n;
            nodeMap[node.NodeId] = vm;
            Nodes.Add(vm);
        }

        AutoLayoutService.Apply(conversation.Nodes, (id, x, y) =>
        {
            if (nodeMap.TryGetValue(id, out var vm))
                vm.Location = new System.Windows.Point(x, y);
        });

        foreach (var node in conversation.Nodes)
        {
            foreach (var link in node.Links)
            {
                if (nodeMap.TryGetValue(link.FromNodeId, out var src) &&
                    nodeMap.TryGetValue(link.ToNodeId, out var tgt))
                {
                    Connections.Add(new ConnectionViewModel(src.Output, tgt.Input));
                }
            }
        }
    }
}
```

- [ ] **Step 4: Build and commit**

```bash
dotnet build DialogEditor.WPF/DialogEditor.WPF.csproj
git add -A
git commit -m "feat: GameBrowserViewModel, ConversationFolderViewModel, ConversationViewModel"
```

---

## Task 13: NodeDetailViewModel

**Files:**
- Create: `DialogEditor.WPF/ViewModels/NodeDetailViewModel.cs`

- [ ] **Step 1: Implement NodeDetailViewModel**

`DialogEditor.WPF/ViewModels/NodeDetailViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.WPF.ViewModels;

public partial class NodeDetailViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasContent;
    [ObservableProperty] private int _nodeId;
    [ObservableProperty] private string _nodeType = string.Empty;
    [ObservableProperty] private string _speakerName = string.Empty;
    [ObservableProperty] private string _speakerGuid = string.Empty;
    [ObservableProperty] private string _listenerName = string.Empty;
    [ObservableProperty] private string _defaultText = string.Empty;
    [ObservableProperty] private string _femaleText = string.Empty;
    [ObservableProperty] private string _conditionsText = string.Empty;
    [ObservableProperty] private string _displayType = string.Empty;
    [ObservableProperty] private string _persistence = string.Empty;
    [ObservableProperty] private string _linksTo = string.Empty;
    [ObservableProperty] private int _scriptCount;

    public void Load(NodeViewModel? node)
    {
        if (node is null) { HasContent = false; return; }

        NodeId = node.NodeId;
        NodeType = node.IsPlayerChoice ? "Player Choice" : "NPC Line";
        SpeakerName = node.SpeakerName;
        SpeakerGuid = node.SpeakerGuid;
        ListenerName = node.ListenerName;
        DefaultText = node.DefaultText;
        FemaleText = node.FemaleText;
        ConditionsText = node.ConditionStrings.Count > 0
            ? string.Join(Environment.NewLine, node.ConditionStrings)
            : "(none)";
        DisplayType = node.DisplayType;
        Persistence = node.Persistence;
        LinksTo = node.Links.Count > 0
            ? string.Join(", ", node.Links.Select(l => $"→ {l.ToNodeId}"))
            : "(none)";
        ScriptCount = node.ScriptCount;
        HasContent = true;
    }

    public void Clear() => HasContent = false;
}
```

- [ ] **Step 2: Build and commit**

```bash
dotnet build DialogEditor.WPF/DialogEditor.WPF.csproj
git add -A
git commit -m "feat: NodeDetailViewModel — exposes selected node data to right panel"
```

---

## Task 14: MainWindowViewModel

**Files:**
- Create: `DialogEditor.WPF/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Implement MainWindowViewModel**

`DialogEditor.WPF/ViewModels/MainWindowViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;
using Microsoft.Win32;

namespace DialogEditor.WPF.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public GameBrowserViewModel Browser { get; } = new();
    public ConversationViewModel Canvas { get; } = new();
    public NodeDetailViewModel Detail { get; } = new();

    private IGameDataProvider? _provider;

    [ObservableProperty]
    private string _statusText = "Open a game folder to begin.";

    public MainWindowViewModel()
    {
        Browser.ConversationSelected += OnConversationSelected;
        Canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversationViewModel.SelectedNode))
                Detail.Load(Canvas.SelectedNode);
        };
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select game root folder" };
        if (dialog.ShowDialog() != true) return;

        var provider = GameDataProviderFactory.Detect(dialog.FolderName);
        if (provider is null)
        {
            StatusText = "Folder not recognized as PoE1 or PoE2 root.";
            return;
        }

        _provider = provider;
        Browser.Load(provider);
        StatusText = $"{provider.GameName} — {dialog.FolderName}";
    }

    private void OnConversationSelected(ConversationFile file)
    {
        if (_provider is null) return;
        try
        {
            var conversation = _provider.LoadConversation(file);
            Canvas.Load(conversation);
            Detail.Clear();
            StatusText = $"{file.Name} — {conversation.Nodes.Count} nodes";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading {file.Name}: {ex.Message}";
        }
    }
}
```

- [ ] **Step 2: Build and commit**

```bash
dotnet build DialogEditor.WPF/DialogEditor.WPF.csproj
git add -A
git commit -m "feat: MainWindowViewModel — folder detection, conversation loading, status bar"
```

---

## Task 15: XAML Views

**Files:**
- Modify: `DialogEditor.WPF/Views/GameBrowserView.xaml`
- Modify: `DialogEditor.WPF/Views/ConversationView.xaml`
- Modify: `DialogEditor.WPF/Views/NodeDetailView.xaml`
- Modify: `DialogEditor.WPF/Views/MainWindow.xaml` (add toolbar + status bar)

- [ ] **Step 1: Complete MainWindow with toolbar and status bar**

`DialogEditor.WPF/Views/MainWindow.xaml`:
```xml
<Window x:Class="DialogEditor.WPF.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:DialogEditor.WPF.Views"
        Title="Pillars Dialog Editor"
        Width="1400" Height="850"
        Background="#1e1e1e">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Toolbar -->
        <ToolBar Grid.Row="0" Background="#252525" Foreground="#ccc">
            <Button Command="{Binding OpenFolderCommand}"
                    Background="#444" Foreground="White" Padding="8,3"
                    BorderBrush="#666" Content="Open Game Folder…"/>
        </ToolBar>

        <!-- Three panels -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="220" MinWidth="150"/>
                <ColumnDefinition Width="4"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="4"/>
                <ColumnDefinition Width="240" MinWidth="180"/>
            </Grid.ColumnDefinitions>

            <views:GameBrowserView Grid.Column="0" DataContext="{Binding Browser}"/>
            <GridSplitter Grid.Column="1" HorizontalAlignment="Stretch" Background="#333"/>
            <views:ConversationView Grid.Column="2" DataContext="{Binding Canvas}"/>
            <GridSplitter Grid.Column="3" HorizontalAlignment="Stretch" Background="#333"/>
            <views:NodeDetailView Grid.Column="4" DataContext="{Binding Detail}"/>
        </Grid>

        <!-- Status bar -->
        <StatusBar Grid.Row="2" Background="#1a1a1a">
            <TextBlock Text="{Binding StatusText}" Foreground="#888" FontSize="11"/>
        </StatusBar>
    </Grid>
</Window>
```

- [ ] **Step 2: Implement GameBrowserView**

`DialogEditor.WPF/Views/GameBrowserView.xaml`:
```xml
<UserControl x:Class="DialogEditor.WPF.Views.GameBrowserView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:DialogEditor.WPF.ViewModels">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="#252525" Padding="8,6">
            <TextBlock Text="Conversations" Foreground="#888"
                       FontSize="10" FontWeight="Bold"
                       CharacterSpacing="100"/>
        </Border>

        <TreeView Grid.Row="1" Background="#2d2d2d"
                  BorderThickness="0"
                  ItemsSource="{Binding Folders}"
                  SelectedItemChanged="TreeView_SelectedItemChanged">
            <TreeView.Resources>
                <HierarchicalDataTemplate DataType="{x:Type vm:ConversationFolderViewModel}"
                                          ItemsSource="{Binding Items}">
                    <TextBlock Text="{Binding DisplayName}"
                               Foreground="#888" FontSize="10" Padding="2,2"/>
                </HierarchicalDataTemplate>
                <DataTemplate DataType="{x:Type vm:ConversationItemViewModel}">
                    <TextBlock Text="{Binding Name}"
                               Foreground="#ccc" FontSize="10" Padding="2,2"/>
                </DataTemplate>
            </TreeView.Resources>
            <TreeView.ItemContainerStyle>
                <Style TargetType="TreeViewItem">
                    <Setter Property="Background" Value="Transparent"/>
                    <Setter Property="Foreground" Value="#ccc"/>
                    <Style.Triggers>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter Property="Background" Value="#1a5276"/>
                        </Trigger>
                    </Style.Triggers>
                </Style>
            </TreeView.ItemContainerStyle>
        </TreeView>

        <Border Grid.Row="2" Background="#1a1a1a" Padding="8,5">
            <TextBlock Text="{Binding GameName, StringFormat='🎮 {0}'}"
                       Foreground="#555" FontSize="9"/>
        </Border>
    </Grid>
</UserControl>
```

`DialogEditor.WPF/Views/GameBrowserView.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using DialogEditor.WPF.ViewModels;

namespace DialogEditor.WPF.Views;

public partial class GameBrowserView : UserControl
{
    public GameBrowserView() => InitializeComponent();

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is GameBrowserViewModel vm && e.NewValue is ConversationItemViewModel item)
            vm.SelectedItem = item;
    }
}
```

- [ ] **Step 3: Implement ConversationView with Nodify canvas**

First, register the Nodify namespace. Add converter to App.xaml resources.

`DialogEditor.WPF/App.xaml`:
```xml
<Application x:Class="DialogEditor.WPF.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:DialogEditor.WPF.Converters"
             StartupUri="Views/MainWindow.xaml">
    <Application.Resources>
        <converters:BoolToHeaderBrushConverter x:Key="BoolToHeaderBrush"/>
    </Application.Resources>
</Application>
```

`DialogEditor.WPF/Views/ConversationView.xaml`:
```xml
<UserControl x:Class="DialogEditor.WPF.Views.ConversationView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:nodify="clr-namespace:Nodify;assembly=Nodify"
             xmlns:vm="clr-namespace:DialogEditor.WPF.ViewModels">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Toolbar -->
        <Border Grid.Row="0" Background="#252525" Padding="8,4">
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <TextBlock Text="{Binding Nodes.Count, StringFormat='{}{0} nodes'}"
                           Foreground="#888" FontSize="10" VerticalAlignment="Center"/>
            </StackPanel>
        </Border>

        <!-- Nodify canvas + minimap overlay -->
        <Grid Grid.Row="1">
        <nodify:NodifyEditor x:Name="Editor"
                             Background="#7a6a8e"
                             ItemsSource="{Binding Nodes}"
                             Connections="{Binding Connections}">

            <nodify:NodifyEditor.ItemContainerStyle>
                <Style TargetType="{x:Type nodify:ItemContainer}">
                    <Setter Property="Location" Value="{Binding Location, Mode=TwoWay}"/>
                    <Setter Property="IsSelected" Value="{Binding IsSelected, Mode=TwoWay}"/>
                </Style>
            </nodify:NodifyEditor.ItemContainerStyle>

            <nodify:NodifyEditor.ItemTemplate>
                <DataTemplate DataType="{x:Type vm:NodeViewModel}">
                    <nodify:Node Input="{Binding Inputs}"
                                 Output="{Binding Outputs}">

                        <nodify:Node.InputConnectorTemplate>
                            <DataTemplate DataType="{x:Type vm:ConnectorViewModel}">
                                <nodify:NodeInput Anchor="{Binding Anchor, Mode=OneWayToSource}"
                                                 IsConnected="True"/>
                            </DataTemplate>
                        </nodify:Node.InputConnectorTemplate>

                        <nodify:Node.OutputConnectorTemplate>
                            <DataTemplate DataType="{x:Type vm:ConnectorViewModel}">
                                <nodify:NodeOutput Anchor="{Binding Anchor, Mode=OneWayToSource}"
                                                  IsConnected="True"/>
                            </DataTemplate>
                        </nodify:Node.OutputConnectorTemplate>

                        <!-- Node card content -->
                        <Grid Width="200">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>

                            <!-- Coloured header -->
                            <Border Grid.Row="0"
                                    Background="{Binding IsPlayerChoice,
                                        Converter={StaticResource BoolToHeaderBrush}}"
                                    CornerRadius="2,2,0,0">
                                <TextBlock Text="{Binding Title}"
                                           Foreground="White" FontWeight="Bold"
                                           FontSize="10" Padding="6,3"
                                           TextTrimming="CharacterEllipsis"/>
                            </Border>

                            <!-- Text preview -->
                            <TextBlock Grid.Row="1"
                                       Text="{Binding TextPreview}"
                                       TextWrapping="Wrap"
                                       Padding="6,4"
                                       FontSize="10"
                                       Background="#F5F0D0"
                                       Foreground="#333"
                                       MaxHeight="70"/>

                            <!-- Footer indicator -->
                            <Border Grid.Row="2" Background="#E8E0B0" Padding="6,2">
                                <TextBlock Text="{Binding FooterText}"
                                           FontSize="9" Foreground="#666"/>
                            </Border>
                        </Grid>

                    </nodify:Node>
                </DataTemplate>
            </nodify:NodifyEditor.ItemTemplate>

            <nodify:NodifyEditor.ConnectionTemplate>
                <DataTemplate DataType="{x:Type vm:ConnectionViewModel}">
                    <nodify:Connection Source="{Binding Source.Anchor}"
                                       Target="{Binding Target.Anchor}"/>
                </DataTemplate>
            </nodify:NodifyEditor.ConnectionTemplate>

        </nodify:NodifyEditor>

        <!-- Minimap overlay -->
        <nodify:Minimap HorizontalAlignment="Right" VerticalAlignment="Bottom"
                        Margin="0,0,8,8" Width="150" Height="100"
                        ViewportLocation="{Binding ElementName=Editor, Path=ViewportLocation}"
                        ViewportSize="{Binding ElementName=Editor, Path=ViewportSize}"
                        Extent="{Binding ElementName=Editor, Path=ItemsExtent}"/>
        </Grid>
    </Grid>
</UserControl>
```

`DialogEditor.WPF/Views/ConversationView.xaml.cs`:
```csharp
using System.Windows.Controls;
namespace DialogEditor.WPF.Views;
public partial class ConversationView : UserControl
{
    public ConversationView() => InitializeComponent();
}
```

- [ ] **Step 4: Implement NodeDetailView**

`DialogEditor.WPF/Views/NodeDetailView.xaml`:
```xml
<UserControl x:Class="DialogEditor.WPF.Views.NodeDetailView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="#252525" Padding="8,6">
            <TextBlock Text="Node Details" Foreground="#888"
                       FontSize="10" FontWeight="Bold" CharacterSpacing="100"/>
        </Border>

        <!-- Empty state -->
        <TextBlock Grid.Row="1"
                   Text="Select a node to view details."
                   Foreground="#555" FontSize="11"
                   HorizontalAlignment="Center" VerticalAlignment="Center"
                   Visibility="{Binding HasContent, Converter={StaticResource InverseBoolToVis}}"/>

        <!-- Content -->
        <ScrollViewer Grid.Row="1" Background="#2d2d2d"
                      Visibility="{Binding HasContent, Converter={StaticResource BoolToVis}}">
            <StackPanel Margin="8" Spacing="10">

                <StackPanel>
                    <TextBlock Text="NODE" Style="{StaticResource LabelStyle}"/>
                    <TextBlock Foreground="White" FontSize="11">
                        <Run Text="{Binding NodeId}"/>
                        <Run Text=" — "/>
                        <Run Text="{Binding NodeType}"/>
                    </TextBlock>
                </StackPanel>

                <StackPanel>
                    <TextBlock Text="SPEAKER" Style="{StaticResource LabelStyle}"/>
                    <TextBlock Text="{Binding SpeakerName}" Foreground="#5dade2" FontSize="11"/>
                    <TextBlock Text="{Binding SpeakerGuid}" Foreground="#555" FontSize="9"/>
                </StackPanel>

                <StackPanel>
                    <TextBlock Text="LISTENER" Style="{StaticResource LabelStyle}"/>
                    <TextBlock Text="{Binding ListenerName}" Foreground="#aaa" FontSize="11"/>
                </StackPanel>

                <StackPanel>
                    <TextBlock Text="DEFAULT TEXT" Style="{StaticResource LabelStyle}"/>
                    <Border Background="#1a1a1a" Padding="6" CornerRadius="3">
                        <TextBlock Text="{Binding DefaultText}"
                                   Foreground="#e8e8e8" FontSize="10"
                                   TextWrapping="Wrap" LineHeight="16"/>
                    </Border>
                </StackPanel>

                <StackPanel Visibility="{Binding FemaleText, Converter={StaticResource NullOrEmptyToVis}}">
                    <TextBlock Text="FEMALE TEXT" Style="{StaticResource LabelStyle}"/>
                    <Border Background="#1a1a1a" Padding="6" CornerRadius="3">
                        <TextBlock Text="{Binding FemaleText}"
                                   Foreground="#e8e8e8" FontSize="10"
                                   TextWrapping="Wrap" LineHeight="16"/>
                    </Border>
                </StackPanel>

                <StackPanel>
                    <TextBlock Text="CONDITIONS" Style="{StaticResource LabelStyle}"/>
                    <Border Background="#1a1a1a" Padding="6" CornerRadius="3">
                        <TextBlock Text="{Binding ConditionsText}"
                                   Foreground="#e8a020" FontSize="10"
                                   TextWrapping="Wrap" LineHeight="16"/>
                    </Border>
                </StackPanel>

                <StackPanel>
                    <TextBlock Text="DISPLAY / PERSIST" Style="{StaticResource LabelStyle}"/>
                    <TextBlock Foreground="#aaa" FontSize="11">
                        <Run Text="{Binding DisplayType}"/>
                        <Run Text=" · "/>
                        <Run Text="{Binding Persistence}"/>
                    </TextBlock>
                </StackPanel>

                <StackPanel>
                    <TextBlock Text="SCRIPTS" Style="{StaticResource LabelStyle}"/>
                    <TextBlock Text="{Binding ScriptCount, StringFormat='{}{0} script(s)'}"
                               Foreground="#aaa" FontSize="11"/>
                </StackPanel>

                <StackPanel>
                    <TextBlock Text="LINKS TO" Style="{StaticResource LabelStyle}"/>
                    <TextBlock Text="{Binding LinksTo}"
                               Foreground="#5dade2" FontSize="11" TextWrapping="Wrap"/>
                </StackPanel>

            </StackPanel>
        </ScrollViewer>
    </Grid>
</UserControl>
```

For the converters and styles used in NodeDetailView, add to `App.xaml`:

```xml
<Application.Resources>
    <converters:BoolToHeaderBrushConverter x:Key="BoolToHeaderBrush"/>
    <BooleanToVisibilityConverter x:Key="BoolToVis"/>
    <converters:InverseBoolToVisibilityConverter x:Key="InverseBoolToVis"/>
    <converters:NullOrEmptyToVisibilityConverter x:Key="NullOrEmptyToVis"/>

    <Style x:Key="LabelStyle" TargetType="TextBlock">
        <Setter Property="Foreground" Value="#888"/>
        <Setter Property="FontSize" Value="8"/>
        <Setter Property="FontWeight" Value="Bold"/>
        <Setter Property="Margin" Value="0,0,0,2"/>
    </Style>
</Application.Resources>
```

Add `NullOrEmptyToVisibilityConverter` and `InverseBoolToVisibilityConverter`:

`DialogEditor.WPF/Converters/NullOrEmptyToVisibilityConverter.cs`:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DialogEditor.WPF.Converters;

[ValueConversion(typeof(string), typeof(Visibility))]
public class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

`DialogEditor.WPF/Converters/InverseBoolToVisibilityConverter.cs`:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DialogEditor.WPF.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

`DialogEditor.WPF/Views/NodeDetailView.xaml.cs`:
```csharp
using System.Windows.Controls;
namespace DialogEditor.WPF.Views;
public partial class NodeDetailView : UserControl
{
    public NodeDetailView() => InitializeComponent();
}
```

- [ ] **Step 5: Build the solution**

```bash
dotnet build DialogEditor.sln
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: complete XAML views — GameBrowserView, ConversationView (Nodify), NodeDetailView"
```

---

## Task 16: Smoke Test

- [ ] **Step 1: Run all unit tests**

```bash
dotnet test DialogEditor.Tests
```

Expected: all tests green.

- [ ] **Step 2: Launch the app against a real game folder**

```bash
dotnet run --project DialogEditor.WPF/DialogEditor.WPF.csproj
```

1. Click **Open Game Folder…**
2. Navigate to `D:\Program Files (x86)\GOG Galaxy\Games\PillarsOfEternity` (PoE1 root — the folder that contains `PillarsOfEternity_Data`)
3. Status bar should show: `Pillars of Eternity — D:\...`
4. Left panel tree should populate with conversation folders
5. Click `companion_bs_aloth_banter` in `companions`
6. Canvas should render 38 nodes in a layered left-to-right layout
7. Click any node — right panel should show text, speaker name, conditions
8. Try the same with the PoE2 root (`D:\Program Files (x86)\GOG Galaxy\Games\Pillars of Eternity II Deadfire`)

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore: smoke test confirmed — PoE1 and PoE2 conversations render correctly"
git push
```

---

## Notes for Future Sessions

- **Avalonia port**: create `DialogEditor.Avalonia` project, swap Nodify for `BAndysc/nodify-avalonia`, adjust XAML namespaces. `DialogEditor.Core` reuses entirely.
- **Speaker GUIDs**: `SpeakerNameService` has a starter set. Add more GUIDs as encountered by cross-referencing `CharacterMappings` in `.conversationbundle` files and the game's `gamedata/characters` XML files.
- **PoE2 integer enums**: `DisplayType` and `Persistence` mapping in `Poe2ConversationParser` may need expansion as more values are encountered in the wild.
