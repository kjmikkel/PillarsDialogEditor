# Test Coverage — Remaining Gaps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unit tests for the four untested ViewModels, `AutoLayoutService`, and both `IGameDataProvider` implementations, closing the last items in the ViewModel Test Coverage gap.

**Architecture:** Five new test files, one per component, following existing patterns: `Loc.Configure(new StubStringProvider())` in ViewModel test constructors, `StubFolderPicker` for async pickers, `IDisposable` + temp directories for provider integration tests. All tested code already exists — tests should pass green immediately; a red result indicates a discovered bug.

**Tech Stack:** xUnit, CommunityToolkit.Mvvm, `DialogEditor.Tests.Helpers` stubs, `ConversationSnapshotBuilder` for provider round-trip tests.

---

## Files to Create

| File | Component |
|------|-----------|
| `DialogEditor.Tests/ViewModels/SimpleViewModelTests.cs` | ConversationFolderViewModel, ConversationItemViewModel, PatchEntryViewModel |
| `DialogEditor.Tests/ViewModels/SettingsViewModelTests.cs` | SettingsViewModel |
| `DialogEditor.Tests/Services/AutoLayoutServiceTests.cs` | AutoLayoutService |
| `DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs` | Poe1GameDataProvider |
| `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs` | Poe2GameDataProvider |

---

## Task 1: Simple ViewModel Tests

**Files:**
- Create: `DialogEditor.Tests/ViewModels/SimpleViewModelTests.cs`

Covers three trivial ViewModels in one file (precedent: `LowPriorityViewModelTests.cs`).

Key types:
- `ConversationFolderViewModel(string folderPath, IEnumerable<ConversationItemViewModel>? items = null, bool isExpanded = false)` — `DisplayName` returns `Loc.Get("Browser_RootFolder")` when `folderPath` is empty, otherwise the raw path
- `ConversationItemViewModel(ConversationFile file, bool isNew = false)` — `DisplayName` appends `Loc.Get("Label_NewConversation_Suffix")` when `isNew` is true
- `PatchEntryViewModel(string fullPath, DialogProject project)` — success path; `PatchEntryViewModel(string fullPath, string errorMessage)` — error path
- `ConversationFile(string Name, string FolderPath, string ConversationPath, string StringTablePath)` — simple record
- `DialogProject.Empty("Name")` — creates a project with zero patches

- [ ] **Step 1: Create the test file**

Create `DialogEditor.Tests/ViewModels/SimpleViewModelTests.cs`:

```csharp
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class SimpleViewModelTests
{
    public SimpleViewModelTests() => Loc.Configure(new StubStringProvider());

    // ── ConversationFolderViewModel ──────────────────────────────────────

    [Fact]
    public void FolderViewModel_DisplayName_EmptyPath_ReturnsRootFolderKey()
    {
        var vm = new ConversationFolderViewModel(string.Empty);
        // StubStringProvider returns the key itself, so this asserts the right key is used
        Assert.Equal("Browser_RootFolder", vm.DisplayName);
    }

    [Fact]
    public void FolderViewModel_DisplayName_NonEmptyPath_ReturnsPath()
    {
        var vm = new ConversationFolderViewModel("some/folder");
        Assert.Equal("some/folder", vm.DisplayName);
    }

    [Fact]
    public void FolderViewModel_IsExpanded_DefaultsFalse()
    {
        var vm = new ConversationFolderViewModel("x");
        Assert.False(vm.IsExpanded);
    }

    [Fact]
    public void FolderViewModel_IsExpanded_SetTrue_RaisesPropertyChanged()
    {
        var vm = new ConversationFolderViewModel("x");
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsExpanded)) raised = true;
        };
        vm.IsExpanded = true;
        Assert.True(raised);
    }

    // ── ConversationItemViewModel ────────────────────────────────────────

    private static ConversationFile MakeFile(string name)
        => new(name, "", "/path/to/" + name + ".conversation", "");

    [Fact]
    public void ItemViewModel_DisplayName_IsNewFalse_ReturnsName()
    {
        var vm = new ConversationItemViewModel(MakeFile("myconv"), isNew: false);
        Assert.Equal("myconv", vm.DisplayName);
    }

    [Fact]
    public void ItemViewModel_DisplayName_IsNewTrue_AppendsSuffix()
    {
        var vm = new ConversationItemViewModel(MakeFile("myconv"), isNew: true);
        // StubStringProvider returns "Label_NewConversation_Suffix" as the suffix value
        Assert.Contains("myconv", vm.DisplayName);
        Assert.Contains("Label_NewConversation_Suffix", vm.DisplayName);
    }

    // ── PatchEntryViewModel ──────────────────────────────────────────────

    [Fact]
    public void PatchEntry_SuccessPath_IsLoaded_True()
    {
        var project = DialogProject.Empty("MyProject");
        var vm = new PatchEntryViewModel("/projects/my.dialogproject", project);
        Assert.True(vm.IsLoaded);
        Assert.Null(vm.LoadError);
    }

    [Fact]
    public void PatchEntry_SuccessPath_ProjectName_FromProject()
    {
        var project = DialogProject.Empty("MyProject");
        var vm = new PatchEntryViewModel("/projects/my.dialogproject", project);
        Assert.Equal("MyProject", vm.ProjectName);
    }

    [Fact]
    public void PatchEntry_ErrorPath_IsLoaded_False()
    {
        var vm = new PatchEntryViewModel("/projects/bad.dialogproject", "File not found");
        Assert.False(vm.IsLoaded);
        Assert.Equal("File not found", vm.LoadError);
    }

    [Fact]
    public void PatchEntry_ErrorPath_ProjectName_UsesDisplayPath()
    {
        var vm = new PatchEntryViewModel("/projects/bad.dialogproject", "File not found");
        // DisplayPath is Path.GetFileName(fullPath) = "bad.dialogproject"
        Assert.Equal("bad.dialogproject", vm.ProjectName);
    }
}
```

- [ ] **Step 2: Run to confirm all 10 tests pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~SimpleViewModelTests" -v normal
```

Expected: `Passed! - Failed: 0, Passed: 10`

If any test fails, read the failure message carefully — it reveals a bug in the ViewModel. Fix the ViewModel (not the test) and re-run.

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/ViewModels/SimpleViewModelTests.cs
git commit -m "test: add SimpleViewModelTests for Folder, Item, and PatchEntry VMs"
```

---

## Task 2: SettingsViewModel Tests

**Files:**
- Create: `DialogEditor.Tests/ViewModels/SettingsViewModelTests.cs`

Key types:
- `SettingsViewModel(string gameDirectory, IFolderPicker picker)` — `BackupDirectory` initialised from `AppSettings.GetBackupPath(gameDirectory) ?? string.Empty`
- `BrowseBackupDirectoryCommand` (`IAsyncRelayCommand`) — opens picker, on cancellation leaves state unchanged, on pick calls `AppSettings.SetBackupPath(gameDirectory, path)` and updates `BackupDirectory`
- `StubFolderPicker(string? result = null)` — returns fixed result from `PickFolderAsync`

**Side effect note:** The "pick succeeds" test calls `AppSettings.SetBackupPath` for real (writing a config file). This is acceptable.

- [ ] **Step 1: Create the test file**

Create `DialogEditor.Tests/ViewModels/SettingsViewModelTests.cs`:

```csharp
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class SettingsViewModelTests
{
    public SettingsViewModelTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void Constructor_BackupDirectory_InitialisedFromAppSettings()
    {
        var expected = AppSettings.GetBackupPath("/game") ?? string.Empty;
        var vm = new SettingsViewModel("/game", new StubFolderPicker());
        Assert.Equal(expected, vm.BackupDirectory);
    }

    [Fact]
    public async Task BrowseBackupDirectory_PickerCancelled_DirectoryUnchanged()
    {
        var vm = new SettingsViewModel("/game", new StubFolderPicker(result: null));
        var before = vm.BackupDirectory;

        await vm.BrowseBackupDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(before, vm.BackupDirectory);
    }

    [Fact]
    public async Task BrowseBackupDirectory_PickerReturnsPick_DirectoryUpdated()
    {
        var vm = new SettingsViewModel("/game", new StubFolderPicker(result: "/backups/here"));

        await vm.BrowseBackupDirectoryCommand.ExecuteAsync(null);

        Assert.Equal("/backups/here", vm.BackupDirectory);
    }
}
```

- [ ] **Step 2: Run to confirm all 3 tests pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~SettingsViewModelTests" -v normal
```

Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "test: add SettingsViewModelTests"
```

---

## Task 3: AutoLayoutService Tests

**Files:**
- Create: `DialogEditor.Tests/Services/AutoLayoutServiceTests.cs`

Key types:
- `AutoLayoutService.Apply(IReadOnlyList<ConversationNode> nodes, Action<int, double, double> setLocation)` — static, in `DialogEditor.Core.Layout`
- `ConversationNode(int NodeId, bool IsPlayerChoice, SpeakerCategory SpeakerCategory, string SpeakerGuid, string ListenerGuid, IReadOnlyList<NodeLink> Links, IReadOnlyList<ConditionNode> Conditions, IReadOnlyList<ScriptCall> Scripts, string DisplayType, string Persistence)` — minimal construction shown in helpers below
- `NodeLink(int FromNodeId, int ToNodeId, IReadOnlyList<ConditionNode> Conditions)` — use `[]` for conditions
- Layout constants: `NodeWidth=220`, `HorizontalGap=200`, so each layer is at `x = layer * 420`

**Relative assertions:** Tests assert *relative* ordering (`pos[A].x < pos[B].x`) rather than exact pixel values, except where one layer is provably at `x=0`.

- [ ] **Step 1: Create the test file**

Create `DialogEditor.Tests/Services/AutoLayoutServiceTests.cs`:

```csharp
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Services;

public class AutoLayoutServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static ConversationNode Node(int id, params int[] targets)
        => new(id, false, SpeakerCategory.Npc, "", "",
               targets.Select(t => new NodeLink(id, t, [])).ToList(),
               [], [], "Conversation", "None");

    private static Dictionary<int, (double x, double y)> Capture(
        IReadOnlyList<ConversationNode> nodes)
    {
        var pos = new Dictionary<int, (double, double)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => pos[id] = (x, y));
        return pos;
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyList_NoCallbackInvoked()
    {
        var called = false;
        AutoLayoutService.Apply([], (_, _, _) => called = true);
        Assert.False(called);
    }

    [Fact]
    public void Apply_SingleNode_PlacedAtLayer0()
    {
        var pos = Capture([Node(0)]);
        Assert.Equal(0.0, pos[0].x);
    }

    [Fact]
    public void Apply_LinearChain_SuccessiveLayers()
    {
        // 0 → 1 → 2
        var pos = Capture([Node(0, 1), Node(1, 2), Node(2)]);
        Assert.True(pos[0].x < pos[1].x, "root should be left of middle");
        Assert.True(pos[1].x < pos[2].x, "middle should be left of leaf");
    }

    [Fact]
    public void Apply_BranchingTree_SiblingsSameLayer()
    {
        // 0 → 1 and 0 → 2
        var pos = Capture([Node(0, 1, 2), Node(1), Node(2)]);
        Assert.Equal(pos[1].x, pos[2].x);
        Assert.True(pos[0].x < pos[1].x);
    }

    [Fact]
    public void Apply_MultipleRoots_BothAtLayer0()
    {
        // Two disconnected trees: 0 → 1, and 2 → 3
        var pos = Capture([Node(0, 1), Node(1), Node(2, 3), Node(3)]);
        Assert.Equal(pos[0].x, pos[2].x);  // both roots at same x
        Assert.Equal(0.0, pos[0].x);
    }

    [Fact]
    public void Apply_Cycle_BothNodesGetPositions()
    {
        // A↔B: neither has zero incoming links, algorithm seeds from nodes[0]
        var nodes = new List<ConversationNode> { Node(0, 1), Node(1, 0) };
        var pos = Capture(nodes);
        Assert.True(pos.ContainsKey(0));
        Assert.True(pos.ContainsKey(1));
    }

    [Fact]
    public void Apply_TwoDisconnectedChains_RootsAtLayer0()
    {
        // Chain A: 0→1, Chain B: 10→11 — roots 0 and 10 should share x=0
        var pos = Capture([Node(0, 1), Node(1), Node(10, 11), Node(11)]);
        Assert.Equal(pos[0].x, pos[10].x);
        Assert.Equal(0.0, pos[0].x);
        Assert.Equal(pos[1].x, pos[11].x);
    }
}
```

- [ ] **Step 2: Run to confirm all 7 tests pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AutoLayoutServiceTests" -v normal
```

Expected: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/Services/AutoLayoutServiceTests.cs
git commit -m "test: add AutoLayoutServiceTests for BFS layer assignment"
```

---

## Task 4: Poe1GameDataProvider Tests

**Files:**
- Create: `DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs`

Key paths (provider takes `string rootPath`):
- Conversations: `{root}/PillarsOfEternity_Data/data/conversations/`
- Stringtables: `{root}/PillarsOfEternity_Data/data/localized/en/text/conversations/`
- Characters: `{root}/PillarsOfEternity_Data/data/localized/en/text/game/characters.stringtable`

The provider's `Language` defaults to `"en"`.

`SaveConversation` creates stringtable directories itself. `InitializeConversationFile` creates conversation directories itself.

Fixture XML is the same `TwoNodeXml` constant from `Poe1ConversationParserTests` — two nodes (NodeID 0 and 1).

For `LoadSpeakerNames`: the parser scans all `.conversation` files for `<CharacterMapping>` elements. The conversation XML just needs to be valid XML containing `<CharacterMapping>` elements somewhere.

For `ConversationSnapshotBuilder.Build(conversation)` — lives in `DialogEditor.Patch` namespace.

- [ ] **Step 1: Create the test file**

Create `DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs`:

```csharp
using DialogEditor.Core.GameData;
using DialogEditor.Patch;

namespace DialogEditor.Tests.GameData;

public class Poe1GameDataProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly Poe1GameDataProvider _provider;

    public Poe1GameDataProviderTests()
    {
        Directory.CreateDirectory(_root);
        _provider = new Poe1GameDataProvider(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ── Fixtures ──────────────────────────────────────────────────────────

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
              <OnEnterScripts /><OnExitScripts /><OnUpdateScripts />
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
            <FlowChartNode xsi:type="PlayerResponseNode">
              <NodeID>1</NodeID>
              <Comments />
              <PackageID>1</PackageID>
              <ContainerNodeID>-1</ContainerNodeID>
              <Links />
              <ClassExtender><ExtendedProperties /></ClassExtender>
              <Conditionals><Operator>And</Operator><Components /></Conditionals>
              <OnEnterScripts /><OnExitScripts /><OnUpdateScripts />
              <NotSkippable>false</NotSkippable>
              <IsQuestionNode>true</IsQuestionNode>
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
              <SpeakerGuid>b1a8e901-0000-0000-0000-000000000000</SpeakerGuid>
              <ListenerGuid>fb6a7cbb-80b6-4b9c-8a99-41c8a031f380</ListenerGuid>
            </FlowChartNode>
          </Nodes>
        </ConversationData>
        """;

    // Minimal conversation XML that contains a CharacterMapping element
    private const string ConvWithMappingXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <ConversationData>
          <CharacterMappings>
            <CharacterMapping>
              <Guid>aaaaaaaa-0000-0000-0000-000000000001</Guid>
              <InstanceTag>NPC_TestName</InstanceTag>
            </CharacterMapping>
          </CharacterMappings>
          <Nodes />
        </ConversationData>
        """;

    // characters.stringtable with "TestName" as an entry — parser strips "NPC_" prefix and looks here
    private const string CharactersXml = """
        <StringTableFile>
          <Entries>
            <Entry>
              <ID>1</ID>
              <DefaultText>TestName</DefaultText>
            </Entry>
          </Entries>
        </StringTableFile>
        """;

    // ── Helpers ───────────────────────────────────────────────────────────

    private string ConvDir => Path.Combine(_root, "PillarsOfEternity_Data", "data", "conversations");

    private string WriteConv(string name, string xml)
    {
        Directory.CreateDirectory(ConvDir);
        var path = Path.Combine(ConvDir, name + ".conversation");
        File.WriteAllText(path, xml);
        return path;
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void EnumerateConversations_ReturnsConversationFiles()
    {
        WriteConv("conv1", TwoNodeXml);
        WriteConv("conv2", TwoNodeXml);

        var files = _provider.EnumerateConversations();

        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void EnumerateConversations_IgnoresNonConversationFiles()
    {
        WriteConv("valid", TwoNodeXml);
        Directory.CreateDirectory(ConvDir);
        File.WriteAllText(Path.Combine(ConvDir, "notes.txt"), "ignore me");

        var files = _provider.EnumerateConversations();

        Assert.Single(files);
        Assert.Equal("valid", files[0].Name);
    }

    [Fact]
    public void LoadConversation_ReturnsConversationWithNodes()
    {
        var path = WriteConv("test", TwoNodeXml);
        var file = new ConversationFile("test", "", path, "");

        var conversation = _provider.LoadConversation(file);

        Assert.Equal(2, conversation.Nodes.Count);
    }

    [Fact]
    public void SaveConversation_WritesFileToExpectedPath()
    {
        var path = WriteConv("test", TwoNodeXml);
        var file = new ConversationFile("test", "", path, "");
        var conversation = _provider.LoadConversation(file);
        var snapshot = ConversationSnapshotBuilder.Build(conversation);

        _provider.SaveConversation(file, snapshot);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveConversation_RoundTrip_PreservesNodeCount()
    {
        var path = WriteConv("test", TwoNodeXml);
        var file = new ConversationFile("test", "", path, "");
        var original = _provider.LoadConversation(file);
        var snapshot = ConversationSnapshotBuilder.Build(original);

        _provider.SaveConversation(file, snapshot);
        var reloaded = _provider.LoadConversation(file);

        Assert.Equal(original.Nodes.Count, reloaded.Nodes.Count);
    }

    [Fact]
    public void LoadSpeakerNames_WithCharactersFile_ReturnsMappings()
    {
        WriteConv("speaker", ConvWithMappingXml);
        var gameDir = Path.Combine(_root, "PillarsOfEternity_Data", "data", "localized", "en", "text", "game");
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, "characters.stringtable"), CharactersXml);

        var names = _provider.LoadSpeakerNames();

        Assert.Contains("aaaaaaaa-0000-0000-0000-000000000001", names.Keys,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadSpeakerNames_WithoutCharactersFile_ReturnsEmpty()
    {
        // No files created — provider should handle missing files gracefully
        var names = _provider.LoadSpeakerNames();
        Assert.Empty(names);
    }

    [Fact]
    public void InitializeConversationFile_CreatesFileOnDisk()
    {
        var file = _provider.BuildNewConversationFile("newconv");

        _provider.InitializeConversationFile(file);

        Assert.True(File.Exists(file.ConversationPath));
        Assert.NotEmpty(File.ReadAllText(file.ConversationPath));
    }
}
```

- [ ] **Step 2: Run to confirm all 8 tests pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe1GameDataProviderTests" -v normal
```

Expected: `Passed! - Failed: 0, Passed: 8`

If `LoadSpeakerNames_WithCharactersFile_ReturnsMappings` fails with an empty result, verify the `ConvWithMappingXml` constant — the `CharacterMapping` element must be a descendant of the root, which it is.

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs
git commit -m "test: add Poe1GameDataProviderTests (enumeration, load, save, speaker names)"
```

---

## Task 5: Poe2GameDataProvider Tests

**Files:**
- Create: `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs`

Key paths (provider takes `string rootPath`):
- Conversations: `{root}/PillarsOfEternityII_Data/exported/design/conversations/`
- Stringtables: `{root}/PillarsOfEternityII_Data/exported/localized/en/text/conversations/`
- Speakers: `{root}/PillarsOfEternityII_Data/exported/design/gamedata/speakers.gamedatabundle`

The `LoadSpeakerNames` implementation checks `File.Exists(SpeakersBundle)` before parsing — returns empty dict if the file is absent (no exception).

Speakers JSON format: `{"GameDataObjects":[{"ID":"<guid>","DebugName":"SPK_NPC_<name>"}]}`
- `FormatDisplayName("SPK_NPC_TestSpeaker")` → strips "SPK_" → "NPC_TestSpeaker" → strips "NPC_" (category prefix) → "TestSpeaker"

- [ ] **Step 1: Create the test file**

Create `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs`:

```csharp
using DialogEditor.Core.GameData;
using DialogEditor.Patch;

namespace DialogEditor.Tests.GameData;

public class Poe2GameDataProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly Poe2GameDataProvider _provider;

    public Poe2GameDataProviderTests()
    {
        Directory.CreateDirectory(_root);
        _provider = new Poe2GameDataProvider(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ── Fixtures ──────────────────────────────────────────────────────────

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
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
              "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0,
              "VOPositioning": 0, "EmotionType": "", "EmotionStrength": 0.5,
              "PersistEmotion": true, "EmotionDelay": 0.0, "ExternalVO": "", "HasVO": false
            },
            {
              "$type": "OEIFormats.FlowCharts.Conversations.PlayerResponseNode, OEIFormats",
              "SpeakerGuid": "b1a8e901-0000-0000-0000-000000000000",
              "ListenerGuid": "fbeeeff7-ec6a-4a40-a47f-1843eaffc6ae",
              "IsQuestionNode": false, "DisplayType": 1, "Persistence": 1,
              "NodeID": 1, "ContainerNodeID": -1,
              "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
              "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0,
              "VOPositioning": 0, "EmotionType": "", "EmotionStrength": 0.5,
              "PersistEmotion": true, "EmotionDelay": 0.0, "ExternalVO": "", "HasVO": false
            }
          ]
        }]}
        """;

    // speakers.gamedatabundle with one entry
    // FormatDisplayName("SPK_NPC_TestSpeaker") → "TestSpeaker"
    private const string SpeakersJson = """
        {"GameDataObjects":[
          {"ID":"bbbbbbbb-0000-0000-0000-000000000001","DebugName":"SPK_NPC_TestSpeaker"}
        ]}
        """;

    // ── Helpers ───────────────────────────────────────────────────────────

    private string ConvDir => Path.Combine(_root, "PillarsOfEternityII_Data", "exported", "design", "conversations");

    private string WriteBundle(string name, string json)
    {
        Directory.CreateDirectory(ConvDir);
        var path = Path.Combine(ConvDir, name + ".conversationbundle");
        File.WriteAllText(path, json);
        return path;
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void EnumerateConversations_ReturnsConversationBundleFiles()
    {
        WriteBundle("conv1", TwoNodeJson);
        WriteBundle("conv2", TwoNodeJson);

        var files = _provider.EnumerateConversations();

        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void EnumerateConversations_IgnoresNonBundleFiles()
    {
        WriteBundle("valid", TwoNodeJson);
        Directory.CreateDirectory(ConvDir);
        File.WriteAllText(Path.Combine(ConvDir, "notes.txt"), "ignore me");

        var files = _provider.EnumerateConversations();

        Assert.Single(files);
        Assert.Equal("valid", files[0].Name);
    }

    [Fact]
    public void LoadConversation_ReturnsConversationWithNodes()
    {
        var path = WriteBundle("test", TwoNodeJson);
        var file = new ConversationFile("test", "", path, "");

        var conversation = _provider.LoadConversation(file);

        Assert.Equal(2, conversation.Nodes.Count);
    }

    [Fact]
    public void SaveConversation_WritesFileToExpectedPath()
    {
        var path = WriteBundle("test", TwoNodeJson);
        var file = new ConversationFile("test", "", path, "");
        var conversation = _provider.LoadConversation(file);
        var snapshot = ConversationSnapshotBuilder.Build(conversation);

        _provider.SaveConversation(file, snapshot);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveConversation_RoundTrip_PreservesNodeCount()
    {
        var path = WriteBundle("test", TwoNodeJson);
        var file = new ConversationFile("test", "", path, "");
        var original = _provider.LoadConversation(file);
        var snapshot = ConversationSnapshotBuilder.Build(original);

        _provider.SaveConversation(file, snapshot);
        var reloaded = _provider.LoadConversation(file);

        Assert.Equal(original.Nodes.Count, reloaded.Nodes.Count);
    }

    [Fact]
    public void LoadSpeakerNames_WithSpeakersFile_ReturnsMappings()
    {
        var gameDataDir = Path.Combine(_root, "PillarsOfEternityII_Data", "exported", "design", "gamedata");
        Directory.CreateDirectory(gameDataDir);
        File.WriteAllText(Path.Combine(gameDataDir, "speakers.gamedatabundle"), SpeakersJson);

        var names = _provider.LoadSpeakerNames();

        Assert.Contains("bbbbbbbb-0000-0000-0000-000000000001", names.Keys,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("TestSpeaker", names["bbbbbbbb-0000-0000-0000-000000000001"]);
    }

    [Fact]
    public void LoadSpeakerNames_WithoutSpeakersFile_ReturnsEmpty()
    {
        var names = _provider.LoadSpeakerNames();
        Assert.Empty(names);
    }

    [Fact]
    public void InitializeConversationFile_CreatesFileOnDisk()
    {
        var file = _provider.BuildNewConversationFile("newconv");

        _provider.InitializeConversationFile(file);

        Assert.True(File.Exists(file.ConversationPath));
        Assert.NotEmpty(File.ReadAllText(file.ConversationPath));
    }
}
```

- [ ] **Step 2: Run to confirm all 8 tests pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2GameDataProviderTests" -v normal
```

Expected: `Passed! - Failed: 0, Passed: 8`

- [ ] **Step 3: Commit**

```
git add DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs
git commit -m "test: add Poe2GameDataProviderTests (enumeration, load, save, speaker names)"
```

---

## Final Verification

- [ ] Run the full test suite:

```
dotnet test DialogEditor.Tests -v quiet
```

Expected: all 506 existing tests plus ~36 new tests pass (542 total), 0 failures.

- [ ] Update `Gaps.md` — revise the "ViewModel Test Coverage" section to reflect the current, much-improved state, or remove it if fully closed.
