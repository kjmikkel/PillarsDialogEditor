using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Regression tests for the save/load persistence cycle: edits saved into a
/// .dialogproject must be visible again when the conversation is reopened, and
/// re-saving after reopening must not erase previously saved edits.
public class MainWindowViewModelPersistenceTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly string _settingsPath;

    public MainWindowViewModelPersistenceTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_persist_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort cleanup */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static ConversationNode MakeNode(int id) =>
        new(id, false, SpeakerCategory.Npc, "spk", "lst", [],
            [], [], "Conversation", "None");

    /// Vanilla "greeting" conversation: node 4 says "orig line".
    private static Conversation VanillaGreeting() =>
        new("greeting", [MakeNode(4)], new StringTable([new StringEntry(4, "orig line", "")]));

    /// A patch (vanilla → edited) changing node 4's text to <paramref name="text"/>.
    private static ConversationPatch GreetingTextPatch(string text)
    {
        var vanilla = ConversationSnapshotBuilder.Build(VanillaGreeting());
        var edited  = ConversationSnapshotBuilder.Build(
            new Conversation("greeting", [MakeNode(4)], new StringTable([new StringEntry(4, text, "")])));
        return DiffEngine.Diff("greeting", vanilla, edited, "en");
    }

    private string TempProjectPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"persist_{Guid.NewGuid():N}.dialogproject");
        _tempFiles.Add(path);
        return path;
    }

    private static void InjectProvider(MainWindowViewModel vm, DialogEditor.Core.GameData.IGameDataProvider provider)
    {
        var fi = typeof(MainWindowViewModel)
            .GetField("_provider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, provider);
    }

    private static Task InvokeLoadProjectAsync(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("LoadProjectAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;
    }

    private static void SelectConversation(MainWindowViewModel vm, ConversationFile file)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("OnConversationSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        mi.Invoke(vm, [file]);
    }

    /// VM with the fake game folder loaded and the given project open from disk.
    private async Task<(MainWindowViewModel Vm, FakeGameDataProvider Provider, string ProjectPath)>
        OpenProjectWithGreetingPatch(ConversationPatch patch)
    {
        var provider = new FakeGameDataProvider("poe2", "en", VanillaGreeting());
        var project  = DialogProject.Empty("Persist").WithPatch(patch);
        var path     = TempProjectPath();
        DialogProjectSerializer.SaveToFile(path, project);

        var vm = MakeVm();
        InjectProvider(vm, provider);
        await InvokeLoadProjectAsync(vm, path);
        return (vm, provider, path);
    }

    // ── Load: stored patch must be visible on the canvas ──────────────────

    [Fact]
    public async Task SelectConversation_WithStoredPatch_ShowsPatchedText()
    {
        var (vm, provider, _) = await OpenProjectWithGreetingPatch(GreetingTextPatch("edited line"));

        SelectConversation(vm, provider.BuildNewConversationFile("greeting"));

        var node = Assert.Single(vm.Canvas.Nodes);
        Assert.Equal("edited line", node.DefaultText);
    }

    [Fact]
    public async Task SelectConversation_WithStoredPatch_BaselineStaysVanilla()
    {
        var (vm, provider, _) = await OpenProjectWithGreetingPatch(GreetingTextPatch("edited line"));

        SelectConversation(vm, provider.BuildNewConversationFile("greeting"));

        Assert.NotNull(vm.Canvas.BaseSnapshot);
        var baseNode = Assert.Single(vm.Canvas.BaseSnapshot!.Nodes);
        Assert.Equal("orig line", baseNode.DefaultText);
    }

    // ── Save after reopen: previously saved edits must survive ────────────

    [Fact]
    public async Task SaveAfterReopen_PreservesPreviouslySavedEdits()
    {
        var (vm, provider, path) = await OpenProjectWithGreetingPatch(GreetingTextPatch("edited line"));
        SelectConversation(vm, provider.BuildNewConversationFile("greeting"));

        // A second editing session: change an unrelated field, then save.
        vm.Canvas.Nodes[0].ActorDirection = "whisper";
        vm.SaveProjectCommand.Execute(null);

        var reloaded = DialogProjectSerializer.LoadFromFile(path);
        var patch    = reloaded.Patches["greeting"];

        // The session-2 edit is present…
        var mod = patch.ModifiedNodes.Single(m => m.NodeId == 4);
        Assert.Contains("ActorDirection", mod.FieldChanges.Keys);
        // …and the session-1 text edit was not erased.
        var translation = patch.Translations["en"].Single(t => t.NodeId == 4);
        Assert.Equal("edited line", translation.DefaultText);
    }

    [Fact]
    public async Task SaveAfterReopen_PreservesOtherLanguageTranslations()
    {
        // Session 1 saved a text edit and imported a French translation for it.
        var patch = GreetingTextPatch("edited line");
        var withFrench = patch with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>(patch.Translations)
            {
                ["fr"] = [new NodeTranslation(4, "ligne éditée", "")],
            },
        };
        var (vm, provider, path) = await OpenProjectWithGreetingPatch(withFrench);
        SelectConversation(vm, provider.BuildNewConversationFile("greeting"));

        vm.Canvas.Nodes[0].ActorDirection = "whisper";
        vm.SaveProjectCommand.Execute(null);

        var reloaded = DialogProjectSerializer.LoadFromFile(path).Patches["greeting"];
        Assert.Equal("ligne éditée",
            reloaded.Translations["fr"].Single(t => t.NodeId == 4).DefaultText);
    }

    [Fact]
    public async Task SaveAfterReopen_NewConversation_KeepsCreatedNodes()
    {
        // A conversation created with ⊕: exists only in the project, not on disk.
        var provider = new FakeGameDataProvider("poe2", "en");
        var created  = ConversationSnapshotBuilder.Build(
            new Conversation("fresh", [MakeNode(1)],
                new StringTable([new StringEntry(1, "created line", "")])));
        var patch   = DiffEngine.Diff("fresh", new ConversationEditSnapshot([]), created, "en");
        var project = DialogProject.Empty("Persist").WithNewConversation("fresh").WithPatch(patch);
        var path    = TempProjectPath();
        DialogProjectSerializer.SaveToFile(path, project);

        var vm = MakeVm();
        InjectProvider(vm, provider);
        await InvokeLoadProjectAsync(vm, path);
        SelectConversation(vm, provider.BuildNewConversationFile("fresh"));

        // Second session: tweak a field, then save.
        vm.Canvas.Nodes[0].ActorDirection = "whisper";
        vm.SaveProjectCommand.Execute(null);

        var savedPatch = DialogProjectSerializer.LoadFromFile(path).Patches["fresh"];
        var addedNode  = Assert.Single(savedPatch.AddedNodes);
        Assert.Equal(1, addedNode.NodeId);
        Assert.Equal("whisper", addedNode.ActorDirection);
        Assert.Equal("created line",
            savedPatch.Translations["en"].Single(t => t.NodeId == 1).DefaultText);
    }
}
