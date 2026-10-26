using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Discard must really discard even after an autosave tick has run (issue 234): the tick used
/// to fold the canvas edit into the in-memory project, and the revert re-applied it.
public class MainWindowViewModelDiscardAfterAutosaveTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _dir;

    public MainWindowViewModelDiscardAfterAutosaveTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_dau_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _dir = Path.Combine(Path.GetTempPath(), $"dau_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private static ConversationNode PlainNode(int id) =>
        new(id, false, SpeakerCategory.Npc, "spk", "lst", [], [], [], "Conversation", "None");

    private string ProjectPath => Path.Combine(_dir, "p.dialogproject");

    private async Task<MainWindowViewModel> OpenGameConversation()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            new Conversation("game", [PlainNode(1)], new StringTable([new StringEntry(1, "saved text", "")])));
        DialogProjectSerializer.SaveToFile(ProjectPath, DialogProject.Empty("P"));

        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(MainWindowViewModel).GetField("_provider", flags)!.SetValue(vm, provider);
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadProjectAsync", flags)!.Invoke(vm, [ProjectPath, false])!;
        typeof(MainWindowViewModel).GetMethod("OnConversationSelected", flags)!
            .Invoke(vm, [provider.BuildNewConversationFile("game")]);
        return vm;
    }

    private static string NodeText(MainWindowViewModel vm) =>
        vm.Canvas.Nodes.Single(n => n.NodeId == 1).DefaultText;

    private static void EditAndAutosave(MainWindowViewModel vm)
    {
        vm.Canvas.Nodes.Single(n => n.NodeId == 1).DefaultText = "edited text";
        Assert.True(vm.IsModified);
        vm.AutosaveTick();
    }

    private static bool HasEditedText(ConversationPatch patch) =>
        patch.Translations.Values.SelectMany(t => t).Any(t => t.DefaultText == "edited text");

    private void AssertSaveDoesNotContainTheEdit(MainWindowViewModel vm)
    {
        vm.IsModified = true;   // force a save so the file is rewritten from the in-memory project
        vm.SaveProjectCommand.Execute(null);
        // An untouched conversation may still save an empty patch; what matters is that the
        // edited text is not in it.
        var saved = DialogProjectSerializer.LoadFromFile(ProjectPath).Patches.GetValueOrDefault("game");
        Assert.False(saved is not null && HasEditedText(saved), "the discarded edit must not reach the saved project");
    }

    [Fact]
    public async Task ModeSwitchDiscard_AfterAutosave_DropsTheEdit()
    {
        var vm = await OpenGameConversation();
        EditAndAutosave(vm);
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;

        Assert.True(await vm.SwitchModeAsync(EditorMode.Reading));
        Assert.Equal("saved text", NodeText(vm));

        await vm.SwitchModeAsync(EditorMode.Editing);
        AssertSaveDoesNotContainTheEdit(vm);
    }

    [Fact]
    public async Task ConversationSwitchDiscard_AfterAutosave_DropsTheEdit()
    {
        var vm = await OpenGameConversation();
        EditAndAutosave(vm);
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;

        // Selecting a conversation while dirty asks, and Discard then loads it.
        typeof(MainWindowViewModel)
            .GetMethod("OnConversationSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [new FakeGameDataProvider("poe2", "en").BuildNewConversationFile("game")]);

        Assert.Equal("saved text", NodeText(vm));
        AssertSaveDoesNotContainTheEdit(vm);
    }

    [Fact]
    public async Task Discard_AfterAutosave_LeavesNoSidecarToResurrectTheEdit()
    {
        var vm = await OpenGameConversation();
        EditAndAutosave(vm);
        Assert.True(File.Exists(AutosaveRecovery.SidecarPath(ProjectPath)));
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;

        await vm.SwitchModeAsync(EditorMode.Reading);

        Assert.False(File.Exists(AutosaveRecovery.SidecarPath(ProjectPath)));
    }

    [Fact]
    public async Task Autosave_StillWritesTheEditToTheSidecar()
    {
        var vm = await OpenGameConversation();
        EditAndAutosave(vm);

        var sidecar = DialogProjectSerializer.LoadFromFile(AutosaveRecovery.SidecarPath(ProjectPath));
        Assert.True(HasEditedText(sidecar.Patches["game"]));   // recovery needs the edit folded into the copy
    }

    [Fact]
    public async Task Save_AfterAutosave_StillPersistsTheEdit()
    {
        var vm = await OpenGameConversation();
        EditAndAutosave(vm);

        vm.SaveProjectCommand.Execute(null);

        Assert.True(HasEditedText(DialogProjectSerializer.LoadFromFile(ProjectPath).Patches["game"]));
    }
}
