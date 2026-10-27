using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 327: New Project and Merge Projects swap the in-memory project under an open
/// conversation. Without a canvas reload the next Save re-diffs the stale canvas over the new
/// project (New Project inherits the old patch; Merge Projects loses the merged changes).
public class MainWindowViewModelProjectSwapReloadTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _dir;
    private readonly string _fallback;
    private string ProjectPath => Path.Combine(_dir, "a.dialogproject");
    private string NewPath     => Path.Combine(_dir, "b.dialogproject");
    private string MergePath   => Path.Combine(_dir, "c.dialogproject");

    public MainWindowViewModelProjectSwapReloadTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_ps_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _dir = Path.Combine(Path.GetTempPath(), $"ps_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _fallback = Path.Combine(Path.GetTempPath(), $"ps_fb_{Guid.NewGuid():N}");
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
            new Conversation("game", [PlainNode(1)], new StringTable([new StringEntry(1, "vanilla text", "")])));

    private static MainWindowViewModel MakeVm(FakeGameDataProvider provider, IFilePicker picker)
    {
        AppSettings.Mode = EditorMode.Editing;
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), picker);
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

    private static ConversationPatch EditPatch(string text) =>
        new ConversationPatch("game", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, text, "")],
            },
        };

    /// Text the patch for "game" in the project file stores for node 1, or null when it has none.
    private static string? SavedText(string path)
    {
        DialogProjectSerializer.LoadFromFile(path).Patches.TryGetValue("game", out var patch);
        IReadOnlyList<NodeTranslation>? en = null;
        return patch?.Translations?.TryGetValue("en", out en) == true
            ? en!.Single(t => t.NodeId == 1).DefaultText
            : null;
    }

    private async Task<MainWindowViewModel> OpenAWithGameOpen(IFilePicker picker)
    {
        DialogProjectSerializer.SaveToFile(ProjectPath, DialogProject.Empty("A").WithPatch(EditPatch("A text")));
        var provider = Provider();
        var vm = MakeVm(provider, picker);
        await LoadProject(vm, ProjectPath);
        Select(vm, provider, "game");
        Assert.Equal("A text", NodeText(vm));
        Assert.False(vm.IsModified);
        return vm;
    }

    // ── New Project ───────────────────────────────────────────────────────

    [Fact]
    public async Task NewProject_ReloadsTheOpenConversation_AndSaveDoesNotInheritTheOldPatch()
    {
        var vm = await OpenAWithGameOpen(new StubFilePicker(saveResult: NewPath));

        vm.NewProjectCommand.Execute(null);
        await Task.Delay(200);   // DoNewProject is fire-and-forget; the stub picker completes at once

        Assert.Equal("vanilla text", NodeText(vm));
        vm.SaveProjectCommand.Execute(null);
        Assert.Null(SavedText(NewPath));
    }

    [Fact]
    public async Task NewProject_ClearsAConversationOnlyTheOldProjectCreated_WithoutAnError()
    {
        var created = ConversationSnapshotBuilder.Build(new Conversation("fresh",
            [PlainNode(1)], new StringTable([new StringEntry(1, "first", "")])));
        var patch = DiffEngine.Diff("fresh", new ConversationEditSnapshot([]), created, "en");
        DialogProjectSerializer.SaveToFile(ProjectPath,
            DialogProject.Empty("A").WithNewConversation("fresh").WithPatch(patch));
        var provider = Provider();   // the game has no "fresh"
        var vm = MakeVm(provider, new StubFilePicker(saveResult: NewPath));
        var errors = new List<Exception>();
        vm.ReportError = errors.Add;
        await LoadProject(vm, ProjectPath);
        Select(vm, provider, "fresh");
        Assert.Equal("fresh", vm.CurrentConversationName);

        vm.NewProjectCommand.Execute(null);
        await Task.Delay(200);

        Assert.Empty(errors);
        Assert.Null(vm.CurrentConversationName);
        Assert.Empty(vm.Canvas.Nodes);
    }

    // ── Merge Projects ────────────────────────────────────────────────────

    private void WriteMergeSource(string text)
        => DialogProjectSerializer.SaveToFile(MergePath, DialogProject.Empty("C").WithPatch(EditPatch(text)));

    [Fact]
    public async Task MergeProjects_ReloadsTheOpenConversation_AndSaveKeepsTheMergedPatch()
    {
        WriteMergeSource("C text");
        var vm = await OpenAWithGameOpen(new StubFilePicker(multiResult: [MergePath]));

        await vm.MergeProjectsCommand.ExecuteAsync(null);

        var merged = SavedText(ProjectPath);   // what the merge wrote, whichever side wins
        Assert.NotNull(merged);
        Assert.Equal(merged, NodeText(vm));
        vm.SaveProjectCommand.Execute(null);
        Assert.Equal(merged, SavedText(ProjectPath));
    }

    // ── Merge Projects with unsaved edits ─────────────────────────────────

    private async Task<MainWindowViewModel> DirtyThenMerge(Action<MainWindowViewModel> answer)
    {
        WriteMergeSource("C text");
        var vm = await OpenAWithGameOpen(new StubFilePicker(multiResult: [MergePath]));
        vm.Canvas.Nodes.Single(n => n.NodeId == 1).DefaultText = "dirty edit";
        Assert.True(vm.IsModified);
        var asked = 0;
        vm.UnsavedChangesRequested += () => { asked++; answer(vm); };
        await vm.MergeProjectsCommand.ExecuteAsync(null);
        Assert.Equal(1, asked);
        return vm;
    }

    [Fact]
    public async Task MergeProjects_WithUnsavedEdits_StayHere_DoesNotMerge()
    {
        var vm = await DirtyThenMerge(v => v.CancelPendingNavigation());

        Assert.Equal("A text", SavedText(ProjectPath));
        Assert.DoesNotContain("C text", File.ReadAllText(ProjectPath));
        Assert.Equal("dirty edit", NodeText(vm));
        Assert.True(vm.IsModified);
    }

    [Fact]
    public async Task MergeProjects_WithUnsavedEdits_Save_SavesTheEditThenMerges()
    {
        var vm = await DirtyThenMerge(v => v.SaveAndProceed());

        // The edit was saved first, so the merge saw "dirty edit" as A's side.
        var saved = File.ReadAllText(ProjectPath);
        Assert.Contains("C text", saved);   // C's side is in the merged file (a conflict keeps both)
        Assert.False(vm.IsModified);
        Assert.Equal(SavedText(ProjectPath), NodeText(vm));
    }

    [Fact]
    public async Task MergeProjects_WithUnsavedEdits_Discard_RevertsTheEditThenMerges()
    {
        var vm = await DirtyThenMerge(v => v.DiscardAndProceed());

        var saved = File.ReadAllText(ProjectPath);
        Assert.DoesNotContain("dirty edit", saved);
        Assert.Contains("C text", saved);
        Assert.False(vm.IsModified);
        Assert.Equal(SavedText(ProjectPath), NodeText(vm));
    }
}
