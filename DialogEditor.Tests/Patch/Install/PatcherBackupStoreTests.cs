using System.Text.Json;
using DialogEditor.Patch.Install;

namespace DialogEditor.Tests.Patch.Install;

public class PatcherBackupStoreTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), $"pbs_{Guid.NewGuid():N}");

    public PatcherBackupStoreTests() => Directory.CreateDirectory(_game);
    public void Dispose() { try { Directory.Delete(_game, true); } catch (Exception) { /* best-effort */ } }

    private string Write(string rel, string text)
    {
        var abs = Path.Combine(_game, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, text);
        return abs;
    }

    [Fact]
    public void EnsureBackedUp_ExistingFile_CopiesOriginalAndRecordsOverwritten()
    {
        var abs   = Write("data/a.txt", "original");
        var store = PatcherBackupStore.Open(_game);

        store.EnsureBackedUp(abs);

        var e = Assert.Single(store.Entries);
        Assert.Equal("data/a.txt", e.Path);
        Assert.Equal(BackupEntryKind.Overwritten, e.Kind);
        Assert.Equal(PatcherBackupStore.HashFile(abs), e.OriginalSha256);
        Assert.Equal("original", File.ReadAllText(
            Path.Combine(_game, "PillarsDialogPatcher", "files", "data", "a.txt")));
    }

    [Fact]
    public void EnsureBackedUp_MissingFile_RecordsCreated()
    {
        var store = PatcherBackupStore.Open(_game);
        store.EnsureBackedUp(Path.Combine(_game, "data", "new.txt"));

        var e = Assert.Single(store.Entries);
        Assert.Equal(BackupEntryKind.Created, e.Kind);
        Assert.Null(e.OriginalSha256);
    }

    [Fact]
    public void EnsureBackedUp_IsIdempotent_DoesNotBackUpModdedBytes()
    {
        var abs   = Write("data/a.txt", "original");
        var store = PatcherBackupStore.Open(_game);
        store.EnsureBackedUp(abs);
        File.WriteAllText(abs, "modded");

        store.EnsureBackedUp(abs);

        Assert.Single(store.Entries);
        Assert.Equal("original", File.ReadAllText(
            Path.Combine(_game, "PillarsDialogPatcher", "files", "data", "a.txt")));
    }

    [Fact]
    public void RecordWritten_StoresHashOfCurrentBytes()
    {
        var abs   = Write("data/a.txt", "original");
        var store = PatcherBackupStore.Open(_game);
        store.EnsureBackedUp(abs);
        File.WriteAllText(abs, "modded");

        store.RecordWritten(abs);

        Assert.Equal(PatcherBackupStore.HashFile(abs), Assert.Single(store.Entries).LastWrittenSha256);
    }

    [Fact]
    public void Manifest_IsPersistedAndReloads_WithoutTempFileLeftBehind()
    {
        var abs = Write("data/a.txt", "original");
        PatcherBackupStore.Open(_game).EnsureBackedUp(abs);

        var reopened = PatcherBackupStore.Open(_game);

        Assert.Equal("data/a.txt", Assert.Single(reopened.Entries).Path);
        Assert.False(File.Exists(reopened.ManifestPath + ".tmp"));
        using var doc = JsonDocument.Parse(File.ReadAllText(reopened.ManifestPath));
        Assert.Equal(1, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    [Fact]
    public void Exists_FalseUntilSomethingIsBackedUp()
    {
        Assert.False(PatcherBackupStore.Exists(_game));
        PatcherBackupStore.Open(_game).EnsureBackedUp(Write("a.txt", "x"));
        Assert.True(PatcherBackupStore.Exists(_game));
    }

    [Fact]
    public void Open_CorruptManifest_ThrowsAndLeavesFileUntouched()
    {
        var manifest = Write("PillarsDialogPatcher/manifest.json", "{ not json");

        Assert.Throws<PatcherBackupCorruptException>(() => PatcherBackupStore.Open(_game));
        Assert.Equal("{ not json", File.ReadAllText(manifest));
    }

    [Fact]
    public void Open_NewerSchema_Throws()
    {
        Write("PillarsDialogPatcher/manifest.json",
              """{"SchemaVersion":99,"PatcherVersion":"9.9","Entries":[]}""");
        Assert.Throws<PatcherBackupCorruptException>(() => PatcherBackupStore.Open(_game));
    }

    [Fact]
    public void EnsureBackedUp_PathOutsideGameDir_Throws()
    {
        var store = PatcherBackupStore.Open(_game);
        Assert.Throws<ArgumentException>(() =>
            store.EnsureBackedUp(Path.Combine(Path.GetTempPath(), "elsewhere.txt")));
    }
}
