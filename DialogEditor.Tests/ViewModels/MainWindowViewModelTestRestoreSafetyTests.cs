using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// The editor's safety promise (issue #66): Test (F5) followed by Restore (F6) returns the
/// game's data folder to its exact original bytes, whatever F5 wrote: edited conversations,
/// every language's stringtables, their .bak sidecars (#121), new conversations, and
/// voice-over. Each test snapshots the whole folder before F5 and compares after F6, so a
/// new write path that isn't tracked for restore fails here rather than in a player's install.
public class MainWindowViewModelTestRestoreSafetyTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en", "fr");
    private readonly string _settingsPath;
    private readonly string _projectDir;

    public MainWindowViewModelTestRestoreSafetyTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_restoresafety_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _projectDir = Path.Combine(Path.GetTempPath(), $"restoresafety_proj_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_projectDir, recursive: true); } catch (Exception) { /* best-effort */ }
        _game.Dispose();
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private MainWindowViewModel MakeVm(DialogProject project)
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Inject(vm, "_provider", _game.Provider);
        Inject(vm, "_currentGameDirectory", _game.Root);
        Inject(vm, "_activeGameId", "poe2");
        Inject(vm, "_projectPath", Path.Combine(_projectDir, "proj.dialogproj"));
        typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [project]);
        return vm;
    }

    private static void Inject(MainWindowViewModel vm, string field, object? value) =>
        typeof(MainWindowViewModel)
            .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, value);

    private static NodeEditSnapshot AddedNode(int id, params LinkEditSnapshot[] links) =>
        new(id, false, SpeakerCategory.Npc, "spk", "lst",
            "", "", "Conversation", "None", "", "", "", false, false, links, [], []);

    /// A patch adding node 99 (→ node 1) to <paramref name="conv"/>, with en and fr text.
    private static ConversationPatch AddNodePatch(string conv, bool linkToOne = true) =>
        new(conv, ConversationPatch.CurrentSchemaVersion,
            [AddedNode(99, linkToOne ? [new LinkEditSnapshot(99, 1, 1f, "", false)] : [])], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(99, "added line", "")],
                ["fr"] = [new NodeTranslation(99, "ligne ajoutée", "")],
            },
        };

    private static DialogProject EditExisting() =>
        DialogProject.Empty("Safety").WithPatch(AddNodePatch("test_conv"));

    private static DialogProject CreateNew() =>
        DialogProject.Empty("Safety")
            .WithNewConversation("brand_new")
            .WithPatch(AddNodePatch("brand_new", linkToOne: false));

    private async Task<MainWindowViewModel> TestThenRestore(DialogProject project)
    {
        var vm = MakeVm(project);
        await vm.TestPatchCommand.ExecuteAsync(null);
        vm.RestoreConversationCommand.Execute(null);
        return vm;
    }

    // ── Write paths ──────────────────────────────────────────────────────

    [Fact]
    public async Task EditedConversation_AllLanguages_RestoresByteIdentical()
    {
        var before = _game.SnapshotGameData();

        var vm = MakeVm(EditExisting());
        await vm.TestPatchCommand.ExecuteAsync(null);
        Assert.NotEqual(before, _game.SnapshotGameData());   // F5 really wrote something

        vm.RestoreConversationCommand.Execute(null);
        Assert.Equal(before, _game.SnapshotGameData());
    }

    [Fact]
    public async Task PreExistingBakSidecars_AreRestoredNotClobbered()
    {
        // A .bak already in the game folder (e.g. left by the patcher or a manual save)
        // must come back with its own bytes, not the copy F5's save made over it (#121).
        File.WriteAllText(_game.ConvPath() + ".bak", "someone else's backup");
        File.WriteAllText(_game.StPath("en") + ".bak", "someone else's st backup");
        var before = _game.SnapshotGameData();

        await TestThenRestore(EditExisting());

        Assert.Equal(before, _game.SnapshotGameData());
    }

    [Fact]
    public async Task NewConversation_RestoreRemovesEverythingF5Created()
    {
        var before = _game.SnapshotGameData();

        var vm = MakeVm(CreateNew());
        await vm.TestPatchCommand.ExecuteAsync(null);
        Assert.True(File.Exists(_game.ConvPath("brand_new")), "F5 should create the new conversation");
        Assert.True(File.Exists(_game.StPath("en", "brand_new")), "F5 should create its stringtable");

        vm.RestoreConversationCommand.Execute(null);
        Assert.Equal(before, _game.SnapshotGameData());
    }

    [Fact]
    public async Task VoiceOver_OverwrittenAndAdded_RestoresByteIdentical()
    {
        var vo = Path.Combine(_projectDir, "_vo");
        Directory.CreateDirectory(Path.Combine(vo, "sub"));
        File.WriteAllBytes(Path.Combine(vo, "existing.wem"), [9, 9, 9]);
        File.WriteAllBytes(Path.Combine(vo, "sub", "added.wem"), [7, 7]);
        var before = _game.SnapshotGameData();

        await TestThenRestore(EditExisting());

        Assert.Equal(before, _game.SnapshotGameData());
    }

    [Fact]
    public async Task VoiceOver_InNewNestedFolders_RestoreRemovesTheFolders()
    {
        // F5 creates "a" and "a/b" to hold the .wem; F6 must take both away again (issue 125).
        var vo = Path.Combine(_projectDir, "_vo", "a", "b");
        Directory.CreateDirectory(vo);
        File.WriteAllBytes(Path.Combine(vo, "added.wem"), [7, 7]);
        var before = _game.SnapshotGameData();

        await TestThenRestore(EditExisting());

        Assert.Equal(before, _game.SnapshotGameData());
    }

    [Fact]
    public async Task CreatedFolder_KeptWhenSomethingElseWasPutInIt()
    {
        // Restore only removes the folders it made *if they are empty*: a file someone else
        // dropped there during the test must survive, and so must its folder.
        var vo = Path.Combine(_projectDir, "_vo", "a");
        Directory.CreateDirectory(vo);
        File.WriteAllBytes(Path.Combine(vo, "added.wem"), [7]);

        var vm = MakeVm(EditExisting());
        await vm.TestPatchCommand.ExecuteAsync(null);
        var foreign = Path.Combine(_game.VoDir, "a", "someone_elses.wem");
        File.WriteAllBytes(foreign, [5]);
        vm.RestoreConversationCommand.Execute(null);

        Assert.True(File.Exists(foreign));
        Assert.False(File.Exists(Path.Combine(_game.VoDir, "a", "added.wem")));
    }

    // ── Lifecycle hazards ────────────────────────────────────────────────

    [Fact]
    public async Task RestartBetweenTestAndRestore_StillRestoresNewConversation()
    {
        // Created files must be tracked in the persisted manifest, not only in memory,
        // or a crash/restart in test mode leaves them in the game folder.
        var before = _game.SnapshotGameData();

        await MakeVm(CreateNew()).TestPatchCommand.ExecuteAsync(null);
        MakeVm(CreateNew()).RestoreConversationCommand.Execute(null);   // a fresh app session

        Assert.Equal(before, _game.SnapshotGameData());
    }

    [Fact]
    public async Task TestPressedTwice_RestoreStillReturnsOriginals()
    {
        // A second F5 while in test mode must not back up the *patched* files as the
        // new "originals" — that would make F6 restore the test state permanently.
        var before = _game.SnapshotGameData();

        var vm = MakeVm(EditExisting());
        await vm.TestPatchCommand.ExecuteAsync(null);
        await vm.TestPatchCommand.ExecuteAsync(null);
        vm.RestoreConversationCommand.Execute(null);

        Assert.Equal(before, _game.SnapshotGameData());
    }

    [Fact]
    public async Task FailureMidApply_RollsBackWritesAlreadyMade()
    {
        // test_conv is patched first and succeeds; zz_broken then fails to load. The
        // error path must undo test_conv's writes — clearing the manifest without
        // restoring would strand them with no way back via F6.
        File.WriteAllText(_game.ConvPath("zz_broken"), "{ not json");
        var before = _game.SnapshotGameData();
        var project = EditExisting().WithPatch(AddNodePatch("zz_broken"));

        var vm = MakeVm(project);
        await vm.TestPatchCommand.ExecuteAsync(null);

        Assert.Equal(before, _game.SnapshotGameData());
        Assert.Null(AppSettings.GetPendingRestores());
    }

    // ── Lost temp backups (issue 125) ────────────────────────────────────

    /// Runs F5, then deletes the temp backup of test_conv's bundle — as the OS or a
    /// cleanup tool might while the game sits in test mode.
    private async Task<MainWindowViewModel> TestThenLoseConvBackup()
    {
        var vm = MakeVm(EditExisting());
        await vm.TestPatchCommand.ExecuteAsync(null);
        var entry = AppSettings.GetPendingRestores()!.Single(e => e.OriginalConvPath == _game.ConvPath());
        File.Delete(entry.BackupConvPath);
        return vm;
    }

    [Fact]
    public async Task MissingBackup_UserStaysInTestMode_RestoresTheRestAndKeepsTheManifest()
    {
        var stBefore = File.ReadAllText(_game.StPath("en"));
        var vm = await TestThenLoseConvBackup();
        IReadOnlyList<string>? asked = null;
        vm.ConfirmLeaveTestModeUnrestored = files => { asked = files; return Task.FromResult(false); };

        await vm.RestoreConversationCommand.ExecuteAsync(null);

        Assert.Equal([_game.ConvPath()], asked);                          // told exactly what is lost
        Assert.Equal(stBefore, File.ReadAllText(_game.StPath("en")));    // everything else put back
        Assert.NotNull(AppSettings.GetPendingRestores());                 // F6 can be retried
        Assert.Contains("Status_RestoreBackupsMissing", vm.StatusText);
    }

    [Fact]
    public async Task MissingBackup_UserLeavesTestMode_ClearsTheManifest()
    {
        var vm = await TestThenLoseConvBackup();
        vm.ConfirmLeaveTestModeUnrestored = _ => Task.FromResult(true);
        var exited = false;
        vm.TestModeExited += () => exited = true;

        await vm.RestoreConversationCommand.ExecuteAsync(null);

        Assert.Null(AppSettings.GetPendingRestores());
        Assert.True(exited);
        Assert.Contains("Status_RestoreLeftUnrestored", vm.StatusText);
    }

    [Fact]
    public async Task MissingBackup_NobodyToAsk_KeepsTheManifest()
    {
        // Never report success over a file still in its test state (the old behaviour).
        var vm = await TestThenLoseConvBackup();

        await vm.RestoreConversationCommand.ExecuteAsync(null);

        Assert.NotNull(AppSettings.GetPendingRestores());
    }
}
