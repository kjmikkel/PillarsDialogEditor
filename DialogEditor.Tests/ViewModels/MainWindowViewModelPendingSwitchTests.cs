using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 328: answering the unsaved-changes prompt (Save or Discard) while switching
/// conversations continues by opening the pending conversation. That continuation must open it
/// the way a plain selection does, so a conversation the project created (no game file) opens
/// too; and a target that fails to load must leave the previous conversation open as saved.
public class MainWindowViewModelPendingSwitchTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _dir;
    private readonly string _fallback;
    private string ProjectPath => Path.Combine(_dir, "a.dialogproject");

    public MainWindowViewModelPendingSwitchTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_psw_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _dir = Path.Combine(Path.GetTempPath(), $"psw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _fallback = Path.Combine(Path.GetTempPath(), $"psw_fb_{Guid.NewGuid():N}");
        AutosaveRecovery.FallbackRootOverride = _fallback;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        AutosaveRecovery.FallbackRootOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_fallback, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private const System.Reflection.BindingFlags Flags =
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    private static ConversationNode PlainNode(int id) =>
        new(id, false, SpeakerCategory.Npc, "spk", "lst", [], [], [], "Conversation", "None");

    private static Conversation Game(string name, string text) =>
        new(name, [PlainNode(1)], new StringTable([new StringEntry(1, text, "")]));

    private static MainWindowViewModel MakeVm(FakeGameDataProvider provider)
    {
        AppSettings.Mode = EditorMode.Editing;
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        typeof(MainWindowViewModel).GetField("_provider", Flags)!.SetValue(vm, provider);
        return vm;
    }

    private static Task LoadProject(MainWindowViewModel vm, string path)
        => (Task)typeof(MainWindowViewModel).GetMethod("LoadProjectAsync", Flags)!.Invoke(vm, [path, false])!;

    private static void Select(MainWindowViewModel vm, FakeGameDataProvider provider, string name)
        => typeof(MainWindowViewModel).GetMethod("OnConversationSelected", Flags)!
            .Invoke(vm, [provider.BuildNewConversationFile(name)]);

    private static string NodeText(MainWindowViewModel vm) =>
        vm.Canvas.Nodes.Single(n => n.NodeId == 1).DefaultText;

    /// A project that created "fresh" (one node, "fresh text") and has "game" open with an
    /// unsaved edit. The game itself has "game" and "other", but no "fresh".
    private async Task<(MainWindowViewModel Vm, FakeGameDataProvider Provider, List<Exception> Errors)> DirtyGameOpen()
    {
        var created = ConversationSnapshotBuilder.Build(Game("fresh", "fresh text"));
        var patch = DiffEngine.Diff("fresh", new ConversationEditSnapshot([]), created, "en");
        DialogProjectSerializer.SaveToFile(ProjectPath,
            DialogProject.Empty("A").WithNewConversation("fresh").WithPatch(patch));
        var provider = new FakeGameDataProvider("poe2", "en",
            Game("game", "vanilla text"), Game("other", "other text"));
        var vm = MakeVm(provider);
        var errors = new List<Exception>();
        vm.ReportError = errors.Add;
        await LoadProject(vm, ProjectPath);
        Select(vm, provider, "game");
        vm.Canvas.Nodes.Single(n => n.NodeId == 1).DefaultText = "dirty edit";
        Assert.True(vm.IsModified);
        return (vm, provider, errors);
    }

    [Fact]
    public async Task DiscardThenSwitch_ToAProjectCreatedConversation_OpensIt()
    {
        var (vm, provider, errors) = await DirtyGameOpen();
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;

        Select(vm, provider, "fresh");

        Assert.Empty(errors);
        Assert.Equal("fresh", vm.CurrentConversationName);
        Assert.Equal("fresh text", NodeText(vm));
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task SaveThenSwitch_ToAProjectCreatedConversation_OpensIt()
    {
        var (vm, provider, errors) = await DirtyGameOpen();
        vm.UnsavedChangesRequested += vm.SaveAndProceed;

        Select(vm, provider, "fresh");

        Assert.Empty(errors);
        Assert.Equal("fresh", vm.CurrentConversationName);
        Assert.Equal("fresh text", NodeText(vm));
    }

    [Fact]
    public async Task DiscardThenSwitch_ToAConversationThatFailsToLoad_PutsThePreviousOneBackAsSaved()
    {
        var (vm, provider, errors) = await DirtyGameOpen();
        provider.Unreadable.Add("other");
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;

        Select(vm, provider, "other");

        Assert.Single(errors);                             // the failed load is still reported
        Assert.Equal("game", vm.CurrentConversationName);
        Assert.Equal("vanilla text", NodeText(vm));        // the discarded edit is gone from the canvas
        Assert.False(vm.IsModified);

        // The editor still treats "game" as open: a later edit and Save write "game", not "other".
        vm.Canvas.Nodes.Single(n => n.NodeId == 1).DefaultText = "later edit";
        vm.SaveProjectCommand.Execute(null);
        var saved = DialogProjectSerializer.LoadFromFile(ProjectPath);
        Assert.True(saved.Patches.ContainsKey("game"));
        Assert.False(saved.Patches.ContainsKey("other"));
    }
}
