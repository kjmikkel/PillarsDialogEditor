using DialogEditor.Core.Editing;
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

    // ── Important 1: Later keeps the sidecar and closes the project ───────

    [Fact]
    public async Task DeferredOffer_Later_ClosesTheProject_AndKeepsTheSidecar()
    {
        WriteProjectWithRestorableEdit("restored text");
        var provider = Provider();
        var vm = MakeVm(EditorMode.Reading, provider);
        await LoadProject(vm, ProjectPath);
        SelectGame(vm, provider);
        vm.ConfirmRestoreAutosave = _ => Task.FromResult(AutosaveDecision.Later);

        await vm.SwitchModeAsync(EditorMode.Editing);

        Assert.False(vm.IsProjectOpen);
        Assert.StartsWith("Status_AutosaveKeptNotOpened", vm.StatusText);
        Assert.True(File.Exists(AutosaveRecovery.SidecarPath(ProjectPath)));
    }

    // ── Important 3: the editing tour waits for the deferred prompts ──────

    [Fact]
    public async Task EditingTour_StartsOnlyAfterTheDeferredPrompt()
    {
        WriteProjectWithRestorableEdit("restored text");
        var vm = MakeVm(EditorMode.Reading);
        await LoadProject(vm, ProjectPath);
        var tourVisibleAtPrompt = true;
        vm.ConfirmRestoreAutosave = gens =>
        {
            tourVisibleAtPrompt = vm.Tour.IsVisible;
            return Task.FromResult(AutosaveDecision.Restore(gens[0]));
        };

        await vm.SwitchModeAsync(EditorMode.Editing);

        Assert.False(tourVisibleAtPrompt);
    }

    // ── Final review 2, R1: Discard clears the dirty flag ──────────────────

    private string OtherProjectPath => Path.Combine(_dir, "q.dialogproject");

    /// Writes a saved project at <paramref name="path"/> plus a newer autosave sidecar that edits
    /// node 1 of "game".
    private static void WriteProjectWithRestorableEdit(string path, string restoredText)
    {
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty("Q"));
        var sidecar = AutosaveRecovery.SidecarPath(path);
        DialogProjectSerializer.SaveToFile(sidecar, DialogProject.Empty("Q").WithPatch(EditPatch(restoredText)));
        File.SetLastWriteTimeUtc(path,    DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(sidecar, DateTime.UtcNow.AddMinutes(-1));
    }

    private static void Edit(MainWindowViewModel vm, string text)
    {
        vm.Canvas.Nodes.Single(n => n.NodeId == 1).DefaultText = text;
        Assert.True(vm.IsModified);
    }

    /// Editing, a saved project open with "game" on the canvas.
    private async Task<(MainWindowViewModel Vm, FakeGameDataProvider Provider)> OpenGameInEditing()
    {
        DialogProjectSerializer.SaveToFile(ProjectPath, DialogProject.Empty("P"));
        var provider = Provider();
        var vm = MakeVm(EditorMode.Editing, provider);
        await LoadProject(vm, ProjectPath);
        SelectGame(vm, provider);
        return (vm, provider);
    }

    [Fact]
    public async Task OpeningAnotherProject_AfterDiscard_RestoreShowsTheRestoredText_AndSaveKeepsIt()
    {
        var (vm, _) = await OpenGameInEditing();
        Edit(vm, "discarded edit");
        WriteProjectWithRestorableEdit(OtherProjectPath, "restored text");
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;
        vm.ConfirmRestoreAutosave = gens => Task.FromResult(AutosaveDecision.Restore(gens[0]));

        vm.OpenRecentProjectCommand.Execute(OtherProjectPath);

        Assert.Equal("restored text", NodeText(vm));
        Assert.True(vm.IsModified);   // restored work is unsaved until Save
        vm.SaveProjectCommand.Execute(null);
        var saved = File.ReadAllText(OtherProjectPath);
        Assert.Contains("restored text", saved);
        Assert.DoesNotContain("discarded edit", saved);
    }

    [Fact]
    public async Task BranchSwitchReload_AfterDiscard_ReloadsTheCanvas_AndIsClean()
    {
        var (vm, _) = await OpenGameInEditing();
        Edit(vm, "discarded edit");
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;

        Assert.True(await vm.EnsureNoUnsavedEditsAsync());
        // The branch checkout rewrites the project file, then the Branches window reloads it.
        DialogProjectSerializer.SaveToFile(ProjectPath, DialogProject.Empty("P").WithPatch(EditPatch("branch text")));
        vm.ReloadCurrentProjectFromDisk();

        Assert.Equal("branch text", NodeText(vm));
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task Discard_ThenAnotherEdit_IsDirtyAgain()
    {
        // Discard clears the canvas flag too: left set, the next edit would not change it, and
        // the window would never be marked dirty again.
        var (vm, _) = await OpenGameInEditing();
        Edit(vm, "discarded edit");
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;
        Assert.True(await vm.EnsureNoUnsavedEditsAsync());
        Assert.False(vm.IsModified);

        vm.Canvas.Nodes.Single(n => n.NodeId == 1).DefaultText = "later edit";

        Assert.True(vm.IsModified);
    }

    [Fact]
    public async Task Switch_ToReading_WhenSaveFails_StaysInEditingWithTheEdit()
    {
        var (vm, _) = await OpenGameInEditing();
        Edit(vm, "unsaved edit");
        // A folder where the project file was: the save cannot write it.
        File.Delete(ProjectPath);
        Directory.CreateDirectory(ProjectPath);
        vm.UnsavedChangesRequested += vm.SaveAndProceed;

        Assert.False(await vm.SwitchModeAsync(EditorMode.Reading));

        Assert.Equal(EditorMode.Editing, vm.Mode);
        Assert.True(vm.IsModified);
        Assert.Equal("unsaved edit", NodeText(vm));
    }
}
