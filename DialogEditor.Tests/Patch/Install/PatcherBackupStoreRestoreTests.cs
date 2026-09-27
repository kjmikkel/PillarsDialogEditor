using DialogEditor.Patch.Install;

namespace DialogEditor.Tests.Patch.Install;

public class PatcherBackupStoreRestoreTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), $"pbsr_{Guid.NewGuid():N}");

    public PatcherBackupStoreRestoreTests() => Directory.CreateDirectory(_game);
    public void Dispose() { try { Directory.Delete(_game, true); } catch (Exception) { /* best-effort */ } }

    private string P(string rel) => Path.Combine(_game, rel);

    private void Write(string rel, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P(rel))!);
        File.WriteAllText(P(rel), text);
    }

    /// Mimics one patcher write: back up, write, record.
    private static void PatcherWrites(PatcherBackupStore s, string abs, string text)
    {
        s.EnsureBackedUp(abs);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, text);
        s.RecordWritten(abs);
    }

    [Fact]
    public void RestoreAll_PutsBackOverwritten_AndDeletesCreated()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        PatcherWrites(s, P("new/b.txt"), "added");

        var skipped = s.RestoreAll();

        Assert.Empty(skipped);
        Assert.Equal("original", File.ReadAllText(P("a.txt")));
        Assert.False(File.Exists(P("new/b.txt")));
        Assert.All(s.Entries, e => Assert.Null(e.LastWrittenSha256));
    }

    [Fact]
    public void FindExternallyChanged_EmptyWhenFilesAreOriginalOrOurs()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        PatcherWrites(s, P("b.txt"), "added");
        Assert.Empty(s.FindExternallyChanged());

        s.RestoreAll();
        Assert.Empty(s.FindExternallyChanged());
    }

    [Fact]
    public void FindExternallyChanged_ReportsFileChangedByOthers()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        File.WriteAllText(P("a.txt"), "game update");

        Assert.Equal(["a.txt"], s.FindExternallyChanged());
    }

    [Fact]
    public void FindExternallyChanged_ReportsDeletedOverwrittenFile()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        File.Delete(P("a.txt"));

        Assert.Equal(["a.txt"], s.FindExternallyChanged());
    }

    [Fact]
    public void RestoreAll_SkipsExternallyChangedFile_AndLeavesItAsIs()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        File.WriteAllText(P("a.txt"), "game update");

        var skipped = s.RestoreAll();

        Assert.Equal(["a.txt"], skipped);
        Assert.Equal("game update", File.ReadAllText(P("a.txt")));
    }

    [Fact]
    public void AcceptCurrentAsOriginal_AdoptsNewBytes()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        File.WriteAllText(P("a.txt"), "game update");

        s.AcceptCurrentAsOriginal(["a.txt"]);

        Assert.Empty(s.FindExternallyChanged());
        PatcherWrites(s, P("a.txt"), "modded again");
        s.RestoreAll();
        Assert.Equal("game update", File.ReadAllText(P("a.txt")));
    }

    [Fact]
    public void AcceptCurrentAsOriginal_MissingFileBecomesCreated()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        File.Delete(P("a.txt"));

        s.AcceptCurrentAsOriginal(["a.txt"]);

        Assert.Equal(BackupEntryKind.Created, Assert.Single(s.Entries).Kind);
    }

    [Fact]
    public void DeleteBackup_RemovesFolder()
    {
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "x");
        s.DeleteBackup();
        Assert.False(PatcherBackupStore.Exists(_game));
        Assert.False(Directory.Exists(s.BackupRoot));
    }
}
