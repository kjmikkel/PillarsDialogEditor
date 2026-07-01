using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// F5 (Test Patch) must write the patch's node text into the game's stringtables —
/// the conversation bundle only carries structure; text lives in the stringtable,
/// so without this step added/edited lines are invisible in-game (B-005).
public class MainWindowViewModelTestPatchTests : IDisposable
{
    private readonly string _gameRoot;
    private readonly string _settingsPath;

    private const string OneNodeBundle = """
        {"Conversations": [{
          "Nodes": [
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "aaaa-0000", "ListenerGuid": "bbbb-0000",
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

    private const string OriginalStringTable = """
        <StringTableFile>
          <Entries>
            <Entry><ID>1</ID><DefaultText>vanilla line</DefaultText><FemaleText></FemaleText></Entry>
          </Entries>
        </StringTableFile>
        """;

    public MainWindowViewModelTestPatchTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_testpatch_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;

        _gameRoot = Path.Combine(Path.GetTempPath(), $"fakegame_{Guid.NewGuid():N}");
        var convDir = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "exported", "design", "conversations");
        var stDir = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "exported", "localized", "en", "text", "conversations");
        Directory.CreateDirectory(convDir);
        Directory.CreateDirectory(stDir);
        File.WriteAllText(Path.Combine(convDir, "test_conv.conversationbundle"), OneNodeBundle);
        File.WriteAllText(Path.Combine(stDir, "test_conv.stringtable"), OriginalStringTable);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_gameRoot, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private string StringTablePath => Path.Combine(_gameRoot,
        "PillarsOfEternityII_Data", "exported", "localized", "en", "text", "conversations",
        "test_conv.stringtable");

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void Inject(MainWindowViewModel vm, string field, object? value) =>
        typeof(MainWindowViewModel)
            .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, value);

    private static void InjectProject(MainWindowViewModel vm, DialogProject project) =>
        typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [project]);

    /// Project with one patch on test_conv: adds node 99 (linked from nothing, links to 1)
    /// whose text lives in Translations["en"], and no other changes.
    private static DialogProject ProjectWithAddedNode()
    {
        var addedNode = new NodeEditSnapshot(99, false, SpeakerCategory.Npc, "spk", "lst",
            "", "", "Conversation", "None", "", "", "", false, false,
            [new LinkEditSnapshot(99, 1, 1f, "", false)], [], []);
        var patch = new ConversationPatch(
            "test_conv", ConversationPatch.CurrentSchemaVersion, [addedNode], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(99, "added line", "")],
            },
        };
        return DialogProject.Empty("TestPatchProj").WithPatch(patch);
    }

    private MainWindowViewModel MakeVmWithProjectAndGame()
    {
        var vm = MakeVm();
        Inject(vm, "_provider", new Poe2GameDataProvider(_gameRoot));
        InjectProject(vm, ProjectWithAddedNode());
        return vm;
    }

    [Fact]
    public async Task TestPatch_WritesAddedNodeTextToStringTable()
    {
        var vm = MakeVmWithProjectAndGame();

        await vm.TestPatchCommand.ExecuteAsync(null);

        var st = File.ReadAllText(StringTablePath);
        Assert.Contains("added line", st);
        Assert.Contains("vanilla line", st);   // untouched entries survive
    }

    [Fact]
    public async Task TestPatch_WritesOtherInstalledLanguages_AndRestoreRemovesCreatedTable()
    {
        // French is installed but this conversation has no French stringtable yet:
        // F5 must create it from the patch's "fr" translations, F6 must remove it.
        var frDir = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "exported", "localized", "fr", "text", "conversations");
        Directory.CreateDirectory(frDir);
        var frStPath = Path.Combine(frDir, "test_conv.stringtable");

        var vm      = MakeVm();
        Inject(vm, "_provider", new Poe2GameDataProvider(_gameRoot));
        var project = ProjectWithAddedNode();
        var patch   = project.Patches["test_conv"];
        patch = patch with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>(patch.Translations)
            {
                ["fr"] = [new NodeTranslation(99, "ligne ajoutée", "")],
            },
        };
        InjectProject(vm, project.WithPatch(patch));

        await vm.TestPatchCommand.ExecuteAsync(null);
        Assert.True(File.Exists(frStPath), "F5 should create the French stringtable");
        Assert.Contains("ligne ajoutée", File.ReadAllText(frStPath));

        vm.RestoreConversationCommand.Execute(null);
        Assert.False(File.Exists(frStPath), "F6 should remove the stringtable it created");
    }

    [Fact]
    public async Task RestoreConversation_RevertsStringTable()
    {
        var vm = MakeVmWithProjectAndGame();
        await vm.TestPatchCommand.ExecuteAsync(null);

        vm.RestoreConversationCommand.Execute(null);

        var st = File.ReadAllText(StringTablePath);
        Assert.DoesNotContain("added line", st);
        Assert.Contains("vanilla line", st);
    }
}
