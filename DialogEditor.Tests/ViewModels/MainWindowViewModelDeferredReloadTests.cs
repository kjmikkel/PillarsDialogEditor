using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.GitConflict;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Final review of reading mode (issue 234): a project swapped in while a conversation is open
/// (the deferred autosave Restore, the deferred conflict merge, opening a project) must reload
/// the canvas, or the next Save re-diffs the stale canvas over the swapped-in work.
public class MainWindowViewModelDeferredReloadTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _dir;
    private readonly string _fallback;
    private string ProjectPath => Path.Combine(_dir, "p.dialogproject");

    public MainWindowViewModelDeferredReloadTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_dr_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _dir = Path.Combine(Path.GetTempPath(), $"dr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _fallback = Path.Combine(Path.GetTempPath(), $"dr_fb_{Guid.NewGuid():N}");
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

    private static FakeGameDataProvider Provider() =>
        new("poe2", "en",
            new Conversation("game", [PlainNode(1)], new StringTable([new StringEntry(1, "saved text", "")])));

    private static MainWindowViewModel MakeVm(EditorMode mode, FakeGameDataProvider? provider = null)
    {
        AppSettings.Mode = mode;
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        if (provider is not null)
            typeof(MainWindowViewModel).GetField("_provider", Flags)!.SetValue(vm, provider);
        return vm;
    }

    private static Task LoadProject(MainWindowViewModel vm, string path)
        => (Task)typeof(MainWindowViewModel).GetMethod("LoadProjectAsync", Flags)!.Invoke(vm, [path, false])!;

    private static void SelectGame(MainWindowViewModel vm, FakeGameDataProvider provider)
        => typeof(MainWindowViewModel).GetMethod("OnConversationSelected", Flags)!
            .Invoke(vm, [provider.BuildNewConversationFile("game")]);

    private static string NodeText(MainWindowViewModel vm) =>
        vm.Canvas.Nodes.Single(n => n.NodeId == 1).DefaultText;

    // Text edits are stored as translations of the provider's language (here "en").
    private static ConversationPatch EditPatch(string text) =>
        new ConversationPatch("game", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, text, "")],
            },
        };

    /// Writes a saved project plus a newer autosave sidecar that edits node 1 of "game".
    private void WriteProjectWithRestorableEdit(string restoredText)
    {
        DialogProjectSerializer.SaveToFile(ProjectPath, DialogProject.Empty("P"));
        var sidecar = AutosaveRecovery.SidecarPath(ProjectPath);
        DialogProjectSerializer.SaveToFile(sidecar, DialogProject.Empty("P").WithPatch(EditPatch(restoredText)));
        File.SetLastWriteTimeUtc(ProjectPath, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(sidecar,     DateTime.UtcNow.AddMinutes(-1));
    }

    // ── Critical 1: deferred restore ──────────────────────────────────────

    [Fact]
    public async Task DeferredRestore_ReloadsTheOpenConversation_AndSaveKeepsTheRestoredEdit()
    {
        WriteProjectWithRestorableEdit("restored text");
        var provider = Provider();
        var vm = MakeVm(EditorMode.Reading, provider);
        await LoadProject(vm, ProjectPath);
        SelectGame(vm, provider);
        Assert.Equal("saved text", NodeText(vm));
        vm.ConfirmRestoreAutosave = gens => Task.FromResult(AutosaveDecision.Restore(gens[0]));

        await vm.SwitchModeAsync(EditorMode.Editing);

        Assert.Equal("restored text", NodeText(vm));
        Assert.True(vm.IsModified);
        vm.SaveProjectCommand.Execute(null);
        Assert.Contains("restored text", File.ReadAllText(ProjectPath));
    }

    [Fact]
    public async Task DeferredRestore_KeepsTheSelectedNode()
    {
        WriteProjectWithRestorableEdit("restored text");
        var provider = Provider();
        var vm = MakeVm(EditorMode.Reading, provider);
        await LoadProject(vm, ProjectPath);
        SelectGame(vm, provider);
        vm.Canvas.SelectedNode = vm.Canvas.Nodes.Single(n => n.NodeId == 1);
        vm.ConfirmRestoreAutosave = gens => Task.FromResult(AutosaveDecision.Restore(gens[0]));

        await vm.SwitchModeAsync(EditorMode.Editing);

        Assert.Equal(1, vm.Canvas.SelectedNode?.NodeId);
    }

    // ── Critical 1: deferred conflict merge ───────────────────────────────

    private void WriteConflictedProject()
    {
        File.WriteAllText(ProjectPath,
            "<<<<<<< HEAD\n" + DialogProjectSerializer.Serialize(DialogProject.Empty("C").WithPatch(EditPatch("friend")))
          + "\n=======\n"    + DialogProjectSerializer.Serialize(DialogProject.Empty("C").WithPatch(EditPatch("traveler")))
          + "\n>>>>>>> branch\n");
    }

    [Fact]
    public async Task DeferredConflictMerge_ShowsTheMergedResult_AndSaveKeepsIt()
    {
        WriteConflictedProject();
        var provider = Provider();
        var vm = MakeVm(EditorMode.Reading, provider);
        vm.ShowGitConflictResolution = res =>
        {
            res.Conflicts[0].Choice = MergeSide.Theirs;
            res.ApplyCommand.Execute(null);
            return Task.FromResult(res.Result);
        };
        await LoadProject(vm, ProjectPath);
        SelectGame(vm, provider);
        Assert.Equal("friend", NodeText(vm));

        await vm.SwitchModeAsync(EditorMode.Editing);

        Assert.Equal("traveler", NodeText(vm));
        Assert.True(vm.IsModified);
        vm.SaveProjectCommand.Execute(null);
        var saved = File.ReadAllText(ProjectPath);
        Assert.Contains("traveler", saved);
        Assert.DoesNotContain("friend", saved);
    }

    // ── Spec section 4: opening a project with a conversation open ────────

    [Fact]
    public async Task Reading_OpeningAProject_WithAConversationOpen_ShowsTheProjectVersion()
    {
        DialogProjectSerializer.SaveToFile(ProjectPath, DialogProject.Empty("P").WithPatch(EditPatch("project text")));
        var provider = Provider();
        var vm = MakeVm(EditorMode.Reading, provider);
        SelectGame(vm, provider);
        Assert.Equal("saved text", NodeText(vm));

        await LoadProject(vm, ProjectPath);

        Assert.Equal("project text", NodeText(vm));
        Assert.False(vm.IsModified);
    }
}
