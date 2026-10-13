using DialogEditor.Core.Backup;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Backup;

/// Issue 198: the full backup also keeps the PoE2 DLC data folders, and an older backup is
/// topped up with them once (nothing could have written them before issue 198).
public class FullBackupDlcTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en", "de");
    private readonly string _backup = Path.Combine(Path.GetTempPath(), $"dlcbackup_{Guid.NewGuid():N}");

    public void Dispose()
    {
        _game.Dispose();
        try { Directory.Delete(_backup, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task Take_KeepsEachDlcsConversationsAndText()
    {
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en", "de");

        await FullBackup.TakeAsync(_game.Provider, _backup, default);

        Assert.True(File.Exists(Path.Combine(_backup, "dlc", "lax2_exported", "conversations", "x", "lax02_cv_x.conversationbundle")));
        Assert.True(File.Exists(Path.Combine(_backup, "dlc", "lax2_exported", "stringtables", "de", "x", "lax02_cv_x.stringtable")));
        Assert.Contains("dlc/lax2_exported/conversations", FullBackup.ReadManifest(_backup)!.ExtraFolders!);
    }

    [Fact]
    public async Task PlanRestore_PairsEachDlcFolderWithItsLiveFolder()
    {
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");
        await FullBackup.TakeAsync(_game.Provider, _backup, default);

        var plan = FullBackup.PlanRestore(_game.Provider, _backup, voicesRoot: null);

        Assert.Contains(plan.Pairs, p =>
            p.Snapshot == Path.Combine(_backup, "dlc/lax2_exported/conversations") &&
            p.Live == Path.Combine(_game.DataRoot, "lax2_exported", "design", "conversations"));
    }

    /// A backup as taken before issue 198: format 2, no extra folders.
    internal static void MakeFormatTwo(string backupRoot)
    {
        var path = Path.Combine(backupRoot, FullBackup.ManifestFileName);
        var manifest = FullBackup.ReadManifest(backupRoot)!;
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(manifest with { Format = 2, ExtraFolders = null }));
    }

    [Fact]
    public async Task TopUp_AddsTheDlcFoldersAnOlderBackupLacks()
    {
        await FullBackup.TakeAsync(_game.Provider, _backup, default);   // no DLC yet
        MakeFormatTwo(_backup);
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");

        var added = await FullBackup.TopUpAsync(_game.Provider, _backup, default);

        Assert.Equal(2, added);   // conversations + "en" text
        Assert.True(File.Exists(Path.Combine(_backup, "dlc", "lax2_exported", "conversations", "x", "lax02_cv_x.conversationbundle")));
        Assert.Equal(0, await FullBackup.TopUpAsync(_game.Provider, _backup, default));   // once
    }

    // Final review: a format-3 backup already holds the DLC folders the game had when it was
    // taken. A folder that appears later can only come from the editor or the patcher (a new
    // language's text, say), so it is never captured as an "original".
    [Fact]
    public async Task TopUp_AFormatThreeBackup_IsLeftAlone()
    {
        await FullBackup.TakeAsync(_game.Provider, _backup, default);
        var manifest = FullBackup.ReadManifest(_backup)!;   // as taken between issues 198 and 201
        File.WriteAllText(Path.Combine(_backup, FullBackup.ManifestFileName),
            System.Text.Json.JsonSerializer.Serialize(manifest with { Format = 3 }));
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");

        Assert.Equal(0, await FullBackup.TopUpAsync(_game.Provider, _backup, default));
        Assert.False(Directory.Exists(Path.Combine(_backup, "dlc")));
    }

    [Fact]
    public async Task TopUp_FormatOneBackup_IsLeftAlone()
    {
        Directory.CreateDirectory(Path.Combine(_backup, "conversations"));   // format 1: no manifest
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");

        Assert.Equal(0, await FullBackup.TopUpAsync(_game.Provider, _backup, default));
        Assert.False(File.Exists(Path.Combine(_backup, FullBackup.ManifestFileName)));
        Assert.False(Directory.Exists(Path.Combine(_backup, "dlc")));
    }

    [Fact]
    public void Patcher_OnADlcConversation_RestoresToTheOriginal()   // Review Focus 5
    {
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");
        var original = _game.SnapshotGameData();
        var project = DialogEditor.Patch.DialogProject.Empty("dlc").WithPatch(
            new DialogEditor.Patch.ConversationPatch("lax02_cv_x", DialogEditor.Patch.ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                NodeComments = new Dictionary<int, string> { [1] = "note" },
                Translations = new Dictionary<string, IReadOnlyList<DialogEditor.Core.Models.NodeTranslation>>
                    { ["en"] = [new DialogEditor.Core.Models.NodeTranslation(1, "changed", "")] },
            });

        var result = DialogEditor.Patch.Install.PatchInstaller.Install(_game.Provider, _game.Root,
            [new DialogEditor.Patch.Install.InstallEntry(project)], new DialogEditor.Patch.Install.InstallOptions());
        Assert.IsType<DialogEditor.Patch.Install.InstallResult.Applied>(result);
        Assert.Contains("changed", File.ReadAllText(Path.Combine(_game.DataRoot, "lax2_exported", "localized", "en", "text", "conversations", "x", "lax02_cv_x.stringtable")));

        DialogEditor.Patch.Install.PatchInstaller.Restore(_game.Root);

        Assert.Equal(original, _game.SnapshotGameData());
    }
}
