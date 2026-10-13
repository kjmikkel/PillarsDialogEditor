using DialogEditor.Core.Backup;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 198: Test Patch and F6 on a DLC conversation, and the backup top-up on opening a
/// known game folder.
public class MainWindowViewModelDlcBackupTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en");
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_dlc_{Guid.NewGuid():N}.json");
    private readonly string _backupPick   = Path.Combine(Path.GetTempPath(), $"dlc_pick_{Guid.NewGuid():N}");

    public MainWindowViewModelDlcBackupTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_settingsPath); } catch { /* best-effort */ }
        try { Directory.Delete(_backupPick, recursive: true); } catch { /* best-effort */ }
        _game.Dispose();
    }

    private static void Inject(MainWindowViewModel vm, string field, object? value) =>
        typeof(MainWindowViewModel).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, value);

    private static object? Invoke(MainWindowViewModel vm, string method, params object?[] args) =>
        typeof(MainWindowViewModel).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, args);

    [Fact]
    public async Task TestPatch_OnADlcConversation_RestoresByteForByte()   // Review Focus 3
    {
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");
        var original = _game.SnapshotGameData();
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Inject(vm, "_provider", _game.Provider);
        Inject(vm, "_currentGameDirectory", _game.Root);
        Inject(vm, "_activeGameId", "poe2");
        Invoke(vm, "SetProject", DialogProject.Empty("dlc").WithPatch(
            new ConversationPatch("lax02_cv_x", ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                Translations = new Dictionary<string, IReadOnlyList<DialogEditor.Core.Models.NodeTranslation>>
                    { ["en"] = [new DialogEditor.Core.Models.NodeTranslation(1, "changed", "")] },
            }));

        await vm.TestPatchCommand.ExecuteAsync(null);
        var written = _game.SnapshotGameData().Where(kv => !original.TryGetValue(kv.Key, out var v) || v != kv.Value).Select(kv => kv.Key).ToList();
        Assert.NotEmpty(written);
        Assert.All(written, k => Assert.StartsWith("lax2_exported", k));

        await vm.RestoreConversationCommand.ExecuteAsync(null);
        Assert.Equal(original, _game.SnapshotGameData());
    }

    [Fact]
    public async Task OpeningAKnownGameFolder_TopsUpTheLatestBackup()
    {
        var backupRoot = Path.Combine(_backupPick, "2026-09-28T10-00");
        await FullBackup.TakeAsync(_game.Provider, backupRoot, default);   // before the DLC existed
        AppSettings.SetBackupPath(_game.Root, _backupPick);
        AppSettings.MarkGameDirectoryKnown(_game.Root);
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");

        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Invoke(vm, "LoadDirectory", _game.Root);

        var expected = Path.Combine(backupRoot, "dlc", "lax2_exported", "conversations", "x", "lax02_cv_x.conversationbundle");
        for (var i = 0; i < 50 && !File.Exists(expected); i++) await Task.Delay(100);
        Assert.True(File.Exists(expected));
    }
}
