using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// An autosave tick only peeks at the pending start-node renumbers (issue 172, issue 234): the
/// Save that follows must still re-key the other languages' translations.
public class MainWindowViewModelAutosaveRemapTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _dir;
    private string ProjectPath => Path.Combine(_dir, "p.dialogproject");

    public MainWindowViewModelAutosaveRemapTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_asr_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _dir = Path.Combine(Path.GetTempPath(), $"asr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    [Fact]
    public async Task Renumber_ThenAutosaveTick_ThenSave_StillRekeysTheTranslation()
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var provider = new FakeGameDataProvider("poe2", "en",
            new Conversation("game",
                [new ConversationNode(1, false, SpeakerCategory.Npc, "spk", "lst", [], [], [], "Conversation", "None")],
                new StringTable([new StringEntry(1, "text", "")])));
        var prior = new ConversationPatch("game", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["fr"] = [new NodeTranslation(1, "bonjour", "")],
            },
        };
        DialogProjectSerializer.SaveToFile(ProjectPath, DialogProject.Empty("P").WithPatch(prior));

        AppSettings.Mode = EditorMode.Editing;
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        typeof(MainWindowViewModel).GetField("_provider", flags)!.SetValue(vm, provider);
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadProjectAsync", flags)!.Invoke(vm, [ProjectPath, false])!;
        typeof(MainWindowViewModel).GetMethod("OnConversationSelected", flags)!
            .Invoke(vm, [provider.BuildNewConversationFile("game")]);
        vm.Canvas.CanChooseStartNode = true;   // issue 172: only a project-created conversation can renumber
        Assert.Equal(StartNodeOutcome.Made, vm.Canvas.MakeStartNode(vm.Canvas.Nodes.Single(n => n.NodeId == 1)));

        vm.AutosaveTick();
        vm.SaveProjectCommand.Execute(null);

        var fr = DialogProjectSerializer.LoadFromFile(ProjectPath).Patches["game"].Translations["fr"];
        Assert.Equal(0, Assert.Single(fr).NodeId);
    }
}
