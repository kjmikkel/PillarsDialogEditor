using DialogEditor.Core.Backup;
using DialogEditor.Core.Logging;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Backup;

/// The full backup's on-disk layout (issue 123): conversations plus every installed
/// language's stringtables, described by a backup.json manifest.
public class FullBackupTests : IDisposable
{
    private readonly FakePoe2Game _game = FakePoe2Game.Canonical();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fullbackup_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception) { /* best-effort */ }
        _game.Dispose();
    }

    [Fact]
    public async Task TakeAsync_SnapshotsConversationsAndEveryLanguage()
    {
        await FullBackup.TakeAsync(_game.Provider, _root, default);

        Assert.True(File.Exists(Path.Combine(_root, "conversations", "canonical.conversationbundle")));
        foreach (var lang in new[] { "en", "de" })
            Assert.Equal(File.ReadAllBytes(_game.StPath(lang, "canonical")),
                File.ReadAllBytes(Path.Combine(_root, "stringtables", lang, "canonical.stringtable")));
        Assert.False(Directory.Exists(Path.Combine(_root, "voice-over")));   // filled lazily by Test Patch
    }

    [Fact]
    public async Task TakeAsync_WritesAFormat2Manifest()
    {
        await FullBackup.TakeAsync(_game.Provider, _root, default);

        var manifest = FullBackup.ReadManifest(_root);

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest.Format);
        Assert.Equal("poe2", manifest.GameId);
        Assert.Equal(["de", "en"], manifest.Languages);
    }

    [Fact]
    public void ReadManifest_WithoutBackupJson_IsNull()
    {
        Directory.CreateDirectory(_root);
        Assert.Null(FullBackup.ReadManifest(_root));
    }

    [Fact]
    public void ReadManifest_CorruptBackupJson_IsNullAndLogged()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, FullBackup.ManifestFileName), "{ not json");

        Assert.Null(FullBackup.ReadManifest(_root));

        Assert.Contains("backup.json", File.ReadAllText(AppLog.LogPath));
    }

    [Fact]
    public void Latest_IsTheLexicographicallyLastSubfolder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "2026-01-01T10-00"));
        Directory.CreateDirectory(Path.Combine(_root, "2026-03-01T10-00"));
        Directory.CreateDirectory(Path.Combine(_root, "2026-02-01T10-00"));

        Assert.Equal(Path.Combine(_root, "2026-03-01T10-00"), FullBackup.Latest(_root));
    }

    [Fact]
    public void Latest_NoSubfoldersOrMissingFolder_IsNull()
    {
        Assert.Null(FullBackup.Latest(_root));
        Directory.CreateDirectory(_root);
        Assert.Null(FullBackup.Latest(_root));
    }
}
