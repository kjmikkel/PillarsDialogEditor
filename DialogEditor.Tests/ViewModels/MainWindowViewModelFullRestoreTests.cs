using DialogEditor.Core.Backup;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Restore Full Backup (Ctrl+Shift+B) never overwrites a file the editor didn't write
/// (issue #118). The one-time snapshot can be older than the game's data; a blind copy back
/// silently downgraded every file a game update or "verify files" had changed since.
public class MainWindowViewModelFullRestoreTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en");
    private readonly string _settingsPath;
    private readonly string _backupPick;
    private readonly string _projectDir;

    public MainWindowViewModelFullRestoreTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_fullrestore_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _backupPick = Path.Combine(Path.GetTempPath(), $"fullrestore_backup_{Guid.NewGuid():N}");
        _projectDir = Path.Combine(Path.GetTempPath(), $"fullrestore_proj_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_backupPick, recursive: true); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_projectDir, recursive: true); } catch (Exception) { /* best-effort */ }
        _game.Dispose();
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private MainWindowViewModel MakeVm(DialogProject? project = null)
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Inject(vm, "_provider", _game.Provider);
        Inject(vm, "_currentGameDirectory", _game.Root);
        Inject(vm, "_activeGameId", "poe2");
        Inject(vm, "_projectPath", Path.Combine(_projectDir, "proj.dialogproj"));
        typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [project ?? EditExisting()]);
        return vm;
    }

    private static void Inject(MainWindowViewModel vm, string field, object? value) =>
        typeof(MainWindowViewModel)
            .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, value);

    private static DialogProject EditExisting() =>
        DialogProject.Empty("FullRestore").WithPatch(
            new ConversationPatch("test_conv", ConversationPatch.CurrentSchemaVersion,
                [new NodeEditSnapshot(99, false, SpeakerCategory.Npc, "spk", "lst", "", "",
                    "Conversation", "None", "", "", "", false, false,
                    [new LinkEditSnapshot(99, 1, 1f, "", false)], [], [])], [], [])
            {
                Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                {
                    ["en"] = [new NodeTranslation(99, "added line", "")],
                },
            });

    /// What OfferBackupAsync does the first time a game folder is opened.
    private async Task TakeSnapshot()
    {
        var (convRoot, stRoot) = _game.Provider.GetBackupRoots();
        var root = Path.Combine(_backupPick, "2026-09-28T10-00");
        await BackupService.BackupAsync(convRoot, Path.Combine(root, "conversations"), default);
        await BackupService.BackupAsync(stRoot,   Path.Combine(root, "stringtables"),  default);
        AppSettings.SetBackupPath(_game.Root, _backupPick);
    }

    // F6's bookkeeping lost (crash, reset settings): Full Restore is the only undo left.
    private static void LoseTestBookkeeping() => AppSettings.ClearPendingRestores();

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AfterTestPatch_WithF6BookkeepingLost_RestoresTheOriginals()
    {
        await TakeSnapshot();
        var conv = File.ReadAllBytes(_game.ConvPath());
        var st   = File.ReadAllBytes(_game.StPath("en"));
        var vm = MakeVm();
        await vm.TestPatchCommand.ExecuteAsync(null);
        Assert.NotEqual(conv, File.ReadAllBytes(_game.ConvPath()));   // F5 really wrote something
        LoseTestBookkeeping();

        await vm.RestoreBackupCommand.ExecuteAsync(null);

        Assert.Equal(conv, File.ReadAllBytes(_game.ConvPath()));
        Assert.Equal(st,   File.ReadAllBytes(_game.StPath("en")));
        Assert.StartsWith("Status_FullRestoreComplete", vm.StatusText);
        Assert.DoesNotContain("Status_FullRestoreSkipped", vm.StatusText);
    }

    [Fact]
    public async Task GameUpdatedAfterTheBackup_IsNotDowngraded()
    {
        await TakeSnapshot();
        File.WriteAllText(_game.StPath("en"), FakePoe2Game.StringTable("text from a game update"));
        var vm = MakeVm();

        await vm.RestoreBackupCommand.ExecuteAsync(null);

        Assert.Contains("text from a game update", File.ReadAllText(_game.StPath("en")));
        Assert.Contains("Status_FullRestoreSkipped", vm.StatusText);
    }

    [Fact]
    public async Task TestPatchOverAGameUpdate_IsNotRolledBackToThePreUpdateFile()
    {
        await TakeSnapshot();
        File.WriteAllText(_game.StPath("en"), FakePoe2Game.StringTable("text from a game update"));
        var vm = MakeVm();
        await vm.TestPatchCommand.ExecuteAsync(null);
        LoseTestBookkeeping();
        var afterTest = File.ReadAllText(_game.StPath("en"));

        await vm.RestoreBackupCommand.ExecuteAsync(null);

        Assert.Equal(afterTest, File.ReadAllText(_game.StPath("en")));
    }

    [Fact]
    public async Task WhileATestIsActive_RefusesAndPointsAtF6()
    {
        await TakeSnapshot();
        var vm = MakeVm();
        await vm.TestPatchCommand.ExecuteAsync(null);
        var tested = _game.SnapshotGameData();

        await vm.RestoreBackupCommand.ExecuteAsync(null);

        Assert.Equal(tested, _game.SnapshotGameData());
        Assert.Equal("Status_FullRestoreTestActive", vm.StatusText);
    }

    [Fact]
    public async Task TestPatch_RecordsItsWritesInTheJournal()
    {
        await TakeSnapshot();
        var vm = MakeVm();

        await vm.TestPatchCommand.ExecuteAsync(null);

        var journal = EditorWriteJournal.Load(Path.Combine(_backupPick, EditorWriteJournal.FileName));
        Assert.Contains(journal, w => string.Equals(w.Path, _game.ConvPath(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(journal, w => string.Equals(w.Path, _game.StPath("en"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TestPatch_WithoutABackup_WritesNoJournal()
    {
        var vm = MakeVm();

        await vm.TestPatchCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(_backupPick));
    }
}
