using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 172 through the main window: which conversations offer the fix, and that a save
/// keeps the renumbered node's translations in every language.
public class MainWindowViewModelStartNodeTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly string _settingsPath;

    public MainWindowViewModelStartNodeTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_start_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static ConversationNode MakeNode(int id, params int[] to) =>
        new(id, false, SpeakerCategory.Npc, "spk", "lst", [.. to.Select(t => new NodeLink(id, t, []))],
            [], [], "Conversation", "None");

    private static void InjectProvider(MainWindowViewModel vm, IGameDataProvider provider) =>
        typeof(MainWindowViewModel)
            .GetField("_provider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, provider);

    private static Task LoadProjectAsync(MainWindowViewModel vm, string path) =>
        (Task)typeof(MainWindowViewModel)
            .GetMethod("LoadProjectAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [path, false])!;

    private static void Select(MainWindowViewModel vm, ConversationFile file) =>
        typeof(MainWindowViewModel)
            .GetMethod("OnConversationSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [file]);

    /// A pre-issue-162 new conversation "fresh": node 1 → node 2, with French for node 1.
    private async Task<(MainWindowViewModel Vm, string Path)> OpenFresh()
    {
        var provider = new FakeGameDataProvider("poe2", "en");
        var created  = ConversationSnapshotBuilder.Build(new Conversation("fresh",
            [MakeNode(1, 2), MakeNode(2)],
            new StringTable([new StringEntry(1, "first", ""), new StringEntry(2, "second", "")])));
        var patch = DiffEngine.Diff("fresh", new ConversationEditSnapshot([]), created, "en");
        patch = patch with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>(patch.Translations)
            {
                ["fr"] = [new NodeTranslation(1, "premier", "")],
            },
        };
        var path = Path.Combine(Path.GetTempPath(), $"start_{Guid.NewGuid():N}.dialogproject");
        _tempFiles.Add(path);
        DialogProjectSerializer.SaveToFile(path,
            DialogProject.Empty("Start").WithNewConversation("fresh").WithPatch(patch));

        var vm = MakeVm();
        InjectProvider(vm, provider);
        await LoadProjectAsync(vm, path);
        Select(vm, provider.BuildNewConversationFile("fresh"));
        return (vm, path);
    }

    private static NodeViewModel ById(MainWindowViewModel vm, int id) => vm.Canvas.Nodes.Single(n => n.NodeId == id);

    [Fact]
    public async Task NewConversation_OffersTheFix()
    {
        var (vm, _) = await OpenFresh();
        Assert.True(vm.Canvas.CanChooseStartNode);
        Assert.True(vm.Canvas.ShowStartNodeWarning);
    }

    [Fact]
    public async Task Make_SetsTheStatus()
    {
        var (vm, _) = await OpenFresh();
        vm.Canvas.MakeStartNode(ById(vm, 1));
        Assert.Equal("Status_StartNodeMade", vm.StatusText);
    }

    [Fact]
    public async Task Save_KeepsOtherLanguages_UnderTheNewId()
    {
        var (vm, path) = await OpenFresh();
        vm.Canvas.MakeStartNode(ById(vm, 1));
        vm.SaveProjectCommand.Execute(null);

        var saved = DialogProjectSerializer.LoadFromFile(path).Patches["fresh"];
        Assert.Equal("premier", saved.Translations["fr"].Single(t => t.NodeId == 0).DefaultText);
        Assert.Equal("first",   saved.Translations["en"].Single(t => t.NodeId == 0).DefaultText);

        // End to end: the saved patch builds a conversation that starts at node 0.
        var built = PatchApplier.Apply(new ConversationEditSnapshot([]), saved);
        var start = built.Nodes.Single(n => n.NodeId == 0);
        Assert.Equal(2, Assert.Single(start.Links).ToNodeId);
        Assert.DoesNotContain(built.Nodes, n => n.NodeId == 1);
    }

    [Fact]
    public async Task Save_Undo_Save_MovesTranslationsBack()
    {
        var (vm, path) = await OpenFresh();
        vm.Canvas.MakeStartNode(ById(vm, 1));
        vm.SaveProjectCommand.Execute(null);
        vm.Canvas.Undo();
        vm.SaveProjectCommand.Execute(null);

        var saved = DialogProjectSerializer.LoadFromFile(path).Patches["fresh"];
        Assert.Equal("premier", saved.Translations["fr"].Single(t => t.NodeId == 1).DefaultText);
        Assert.DoesNotContain(saved.Translations["fr"], t => t.NodeId == 0);
    }
}
