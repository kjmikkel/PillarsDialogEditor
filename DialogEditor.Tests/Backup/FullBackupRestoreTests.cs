using DialogEditor.Core.Backup;

namespace DialogEditor.Tests.Backup;

/// <summary>
/// Restore Full Backup must never overwrite a file the editor didn't write (#118).
/// A file is copied back from the snapshot only when its current content is an editor
/// write that was made on top of exactly the snapshot's copy; anything else — a game
/// update, a "verify files", a hand edit — is left alone and reported.
/// </summary>
public class FullBackupRestoreTests : IDisposable
{
    private readonly string _tmp      = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private string Snapshot => Path.Combine(_tmp, "snapshot");
    private string Live     => Path.Combine(_tmp, "live");

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* best-effort */ }
    }

    private void Put(string root, string rel, string content)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string LivePath(string rel) => Path.GetFullPath(Path.Combine(Live, rel));
    private string ReadLive(string rel) => File.ReadAllText(LivePath(rel));

    // Records what F5 does: the file held `before`, the editor wrote `after` over it.
    private EditorWrite Wrote(string rel, string? before, string after)
    {
        var path = LivePath(rel);
        return new EditorWrite(path,
            before is null ? null : FileHash.OfText(before),
            FileHash.OfText(after));
    }

    private FullRestoreResult Restore(IReadOnlyCollection<EditorWrite>? writes = null,
                                      IReadOnlySet<string>? patcherManaged = null) =>
        FullBackupRestore.Restore(Snapshot, Live, writes ?? [],
            patcherManaged ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void EditorWriteOnTopOfTheSnapshot_IsRestored()
    {
        Put(Snapshot, "a/conv.xml", "original");
        Put(Live,     "a/conv.xml", "edited");

        var result = Restore([Wrote("a/conv.xml", before: "original", after: "edited")]);

        Assert.Equal("original", ReadLive("a/conv.xml"));
        Assert.Equal([LivePath("a/conv.xml")], result.Restored);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void GameUpdatedAfterTheSnapshot_IsLeftAloneAndReported()
    {
        // The #118 case: the game (or "verify files") replaced the file; the editor never touched it.
        Put(Snapshot, "conv.xml", "v1");
        Put(Live,     "conv.xml", "v2 from a game update");

        var result = Restore();

        Assert.Equal("v2 from a game update", ReadLive("conv.xml"));
        Assert.Empty(result.Restored);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(LivePath("conv.xml"), skip.Path);
        Assert.Equal(RestoreSkipReason.ChangedOutsideEditor, skip.Reason);
    }

    [Fact]
    public void EditorWriteOnTopOfAGameUpdate_IsNotDowngraded()
    {
        // The game updated to v2 after the snapshot, then F5 edited v2. The snapshot (v1)
        // is the wrong original: copying it back would undo the update too.
        Put(Snapshot, "conv.xml", "v1");
        Put(Live,     "conv.xml", "v2 edited");

        var result = Restore([Wrote("conv.xml", before: "v2", after: "v2 edited")]);

        Assert.Equal("v2 edited", ReadLive("conv.xml"));
        Assert.Equal(RestoreSkipReason.ChangedOutsideEditor, Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public void FileChangedAgainAfterTheEditorWrote_IsLeftAlone()
    {
        Put(Snapshot, "conv.xml", "original");
        Put(Live,     "conv.xml", "edited, then updated by the game");

        var result = Restore([Wrote("conv.xml", before: "original", after: "edited")]);

        Assert.Equal("edited, then updated by the game", ReadLive("conv.xml"));
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void UnchangedFile_IsNeitherRestoredNorReported()
    {
        Put(Snapshot, "conv.xml", "same");
        Put(Live,     "conv.xml", "same");

        var result = Restore();

        Assert.Empty(result.Restored);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void FileManagedByThePatcher_IsLeftToThePatcher()
    {
        // The patcher keeps its own original (possibly newer than the snapshot) and would
        // still list the mod as installed; its Restore is the right tool.
        Put(Snapshot, "conv.xml", "original");
        Put(Live,     "conv.xml", "modded");
        var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LivePath("conv.xml") };

        var result = Restore([Wrote("conv.xml", "original", "modded")], managed);

        Assert.Equal("modded", ReadLive("conv.xml"));
        Assert.Equal(RestoreSkipReason.ManagedByPatcher, Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public void FileMissingFromTheGame_IsNotRecreated()
    {
        // The editor never deletes shipped files, so a missing one was removed by something else.
        Put(Snapshot, "gone.xml", "original");
        Directory.CreateDirectory(Live);

        var result = Restore();

        Assert.False(File.Exists(LivePath("gone.xml")));
        Assert.Equal(RestoreSkipReason.ChangedOutsideEditor, Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public void FileOnlyInTheGame_IsLeftAlone()
    {
        Directory.CreateDirectory(Snapshot);
        Put(Live, "new_in_update.xml", "added by a game update");

        var result = Restore();

        Assert.True(File.Exists(LivePath("new_in_update.xml")));
        Assert.Empty(result.Restored);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void PathMatching_IgnoresCaseAndSeparators()
    {
        Put(Snapshot, "Sub/Conv.xml", "original");
        Put(Live,     "Sub/Conv.xml", "edited");
        var write = new EditorWrite(LivePath("sub/conv.xml").ToUpperInvariant(),
            FileHash.OfText("original"), FileHash.OfText("edited"));

        var result = Restore([write]);

        Assert.Single(result.Restored);
        Assert.Equal("original", ReadLive("Sub/Conv.xml"));
    }
}
