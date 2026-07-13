using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// The Validate Text Tags dirty guard (three-way consent seam) on MainWindowViewModel.
public class MainWindowViewModelTextTagTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _projectPath;

    public MainWindowViewModelTextTagTests()
    {
        Loc.Configure(new StubStringProvider());
        // Isolate AppSettings so the VM constructor doesn't auto-load a game folder.
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_tt_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _projectPath = Path.Combine(Path.GetTempPath(), $"tt_{Guid.NewGuid():N}.dialogproject");
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { if (File.Exists(_projectPath)) File.Delete(_projectPath); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void InjectProject(MainWindowViewModel vm, DialogProject project)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        mi.Invoke(vm, [project]);
    }

    private void InjectProjectPath(MainWindowViewModel vm)
    {
        var fi = typeof(MainWindowViewModel)
            .GetField("_projectPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, _projectPath);
    }

    /// A saved project whose open conversation's patch carries a comment on a
    /// deleted node — a confirmed-stale row (no live-node reconstruction needed,
    /// since Confirmed rows come straight from DeletedNodeIds ∩ NodeComments).
    private MainWindowViewModel OpenSavedProjectWithStaleComment(int deletedNodeId)
    {
        var vm = MakeVm();
        var patch = new ConversationPatch("Conv", ConversationPatch.CurrentSchemaVersion, [], [deletedNodeId], [])
        {
            NodeComments = new Dictionary<int, string> { [deletedNodeId] = "x" }
        };
        var project = DialogProject.Empty("T").WithPatch(patch);
        InjectProject(vm, project);
        InjectProjectPath(vm);
        return vm;
    }

    [Fact]
    public async Task NoProject_ReturnsNull()
    {
        var vm = MakeVm();
        Assert.Null(await vm.RequestTextTagValidationAsync());
    }

    [Fact]
    public async Task CleanProject_ReturnsVm_WithoutConsultingSeam()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        var consulted = false;
        vm.ConfirmScanWithUnsavedChanges = () => { consulted = true; return Task.FromResult(ScanDirtyChoice.Cancel); };
        var result = await vm.RequestTextTagValidationAsync();
        Assert.NotNull(result);
        Assert.False(consulted);
    }

    [Fact]
    public async Task Dirty_Cancel_ReturnsNull_AndDoesNotSave()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        InjectProjectPath(vm);
        vm.IsModified = true;
        vm.ConfirmScanWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.Cancel);

        Assert.Null(await vm.RequestTextTagValidationAsync());
        Assert.True(vm.IsModified);              // no save happened
        Assert.False(File.Exists(_projectPath)); // nothing written
    }

    [Fact]
    public async Task Dirty_ScanSavedOnly_ReturnsVm_WithoutSave()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        InjectProjectPath(vm);
        vm.IsModified = true;
        vm.ConfirmScanWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.ScanSavedOnly);

        Assert.NotNull(await vm.RequestTextTagValidationAsync());
        Assert.True(vm.IsModified);              // still dirty — nothing saved
        Assert.False(File.Exists(_projectPath));
    }

    [Fact]
    public async Task Dirty_SaveAndScan_SavesThenReturnsVm()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        InjectProjectPath(vm);
        vm.IsModified = true;
        vm.ConfirmScanWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.SaveAndScan);

        Assert.NotNull(await vm.RequestTextTagValidationAsync());
        Assert.False(vm.IsModified);             // SaveProject flipped the flag
        Assert.True(File.Exists(_projectPath));  // and wrote the file
    }

    [Fact]
    public async Task Dirty_NoSeamWired_ReturnsNull()
    {
        // Safety: a dirty project with no dialog wired must not silently scan.
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        vm.IsModified = true;
        Assert.Null(await vm.RequestTextTagValidationAsync());
    }

    private static void InjectProvider(MainWindowViewModel vm, IGameDataProvider provider)
    {
        var fi = typeof(MainWindowViewModel)
            .GetField("_provider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, provider);
    }

    /// Enumerates (resolves) each named conversation, but LOADS only those mapped to
    /// true — a name mapped to false throws on load, modelling a vanilla file that
    /// exists on disk yet can't be parsed. Loadable conversations return an empty
    /// vanilla base (the effective set then comes purely from the applied patch).
    private sealed class ResolvingProvider(IReadOnlyDictionary<string, bool> loadable) : IGameDataProvider
    {
        public string GameName                          => "Stub";
        public string GameId                            => "stub";
        public IReadOnlyList<string> AvailableLanguages => [];
        public string Language { get; set; }            = "en";

        public IReadOnlyList<ConversationFile> EnumerateConversations()
            => loadable.Keys.Select(n => new ConversationFile(n, "", "", "")).ToList();

        public Conversation LoadConversation(ConversationFile f)
            => loadable[f.Name]
                ? new Conversation(f.Name, [], new StringTable([]))
                : throw new InvalidDataException($"corrupt conversation: {f.Name}");

        public void SaveConversation(ConversationFile f, ConversationEditSnapshot s) { }
        public IReadOnlyDictionary<string, string> LoadSpeakerNames() => new Dictionary<string, string>();
        public string GetStringTablePath(ConversationFile f) => string.Empty;
        public string GetStringTablePath(ConversationFile f, string language) => string.Empty;
        public (string, string) GetBackupRoots() => (string.Empty, string.Empty);
        public ConversationFile BuildNewConversationFile(string name) => new(name, "", "", "");
        public void InitializeConversationFile(ConversationFile f) { }
    }

    private static readonly NodeEditSnapshot AddedNode5 =
        new(5, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false, [], [], []);

    /// A saved project with two conversations, neither the open one, exercised with the
    /// game-file (likely) pass armed:
    ///  • "ResolvableConv" — the provider loads it (empty vanilla base). Its patch adds
    ///    node 5 (which survives, so its comment is NOT stale) and carries an orphaned
    ///    comment on node 42 — an added node that was created then deleted, so 42 is in
    ///    neither AddedNodes nor DeletedNodeIds and is absent from the reconstructed set.
    ///  • "BrokenConv" — the provider throws on load; its patch carries a comment on 99.
    ///    The delegate hits the catch → returns null → the scanner skips it (never flags).
    private async Task<TextTagValidationViewModel> OpenValidationWithGameFiles()
    {
        var vm = MakeVm();
        InjectProvider(vm, new ResolvingProvider(new Dictionary<string, bool>
        {
            ["ResolvableConv"] = true,
            ["BrokenConv"]     = false,
        }));

        var resolvable = new ConversationPatch(
            "ResolvableConv", ConversationPatch.CurrentSchemaVersion, [AddedNode5], [], [])
        {
            NodeComments = new Dictionary<int, string> { [5] = "kept", [42] = "orphan" }
        };
        var broken = new ConversationPatch(
            "BrokenConv", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            NodeComments = new Dictionary<int, string> { [99] = "orphan" }
        };

        InjectProject(vm, DialogProject.Empty("T").WithPatch(resolvable).WithPatch(broken));
        InjectProjectPath(vm);

        var window = await vm.RequestTextTagValidationAsync();
        Assert.NotNull(window);
        window!.CheckGameFiles = true;   // arms the reconstruction-based likely pass
        return window;
    }

    [Fact]
    public async Task LikelyStale_AddedNodeOrphan_InResolvableConversation_SurfacesAsLikelyRow()
    {
        var window = await OpenValidationWithGameFiles();

        // Exactly one row: node 42's orphaned comment. (Node 5 survives reconstruction, so
        // its comment is not flagged; BrokenConv is skipped, so it contributes nothing.)
        var stale = Assert.Single(window.StaleRows);
        Assert.True(stale.IsLikely);
        Assert.Equal("ResolvableConv", stale.ConversationName);
        Assert.Equal(42, stale.Row.NodeId);
    }

    [Fact]
    public async Task LikelyStale_UnloadableConversation_IsSkipped_NotFlagged()
    {
        var window = await OpenValidationWithGameFiles();

        // The conversation the provider can't load surfaces no rows at all — a load
        // failure means "skip", never "flag".
        Assert.DoesNotContain(window.StaleRows, r => r.ConversationName == "BrokenConv");
    }

    [Fact]
    public async Task StaleComment_ForDeletedNode_ShownAndPrunable()
    {
        // Arrange: a saved project whose open conversation's patch has a comment on a
        // deleted node id.
        var vm = OpenSavedProjectWithStaleComment(deletedNodeId: 7);

        // Act
        var window = await vm.RequestTextTagValidationAsync();

        // Assert: the stale row is present and confirmed.
        Assert.NotNull(window);
        var stale = Assert.Single(window!.StaleRows);
        Assert.False(stale.IsLikely);

        // Prune it and confirm the saved project no longer carries the comment.
        window.CleanUpStaleCommand.Execute(null);
        window.ConfirmCleanUpStaleCommand.Execute(null);
        Assert.Empty(window.StaleRows);
    }
}
