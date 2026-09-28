using DialogEditor.Core.Backup;

namespace DialogEditor.Tests.Backup;

/// <summary>
/// The record of what the editor wrote into the game folder (#118). It lives next to the
/// full backup and outlives F6, so Restore Full Backup can still recognise the editor's
/// writes after the test-patch bookkeeping is gone.
/// </summary>
public class EditorWriteJournalTests : IDisposable
{
    private readonly string _tmp  = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private string JournalPath    => Path.Combine(_tmp, EditorWriteJournal.FileName);

    public EditorWriteJournalTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Load_MissingFile_IsEmpty() =>
        Assert.Empty(EditorWriteJournal.Load(JournalPath));

    [Fact]
    public void Append_ThenLoad_RoundTrips()
    {
        var w1 = new EditorWrite(@"C:\game\a.conversation", "AA", "BB");
        var w2 = new EditorWrite(@"C:\game\new.conversation", null, "CC");

        EditorWriteJournal.Append(JournalPath, [w1]);
        EditorWriteJournal.Append(JournalPath, [w2]);

        Assert.Equal([w1, w2], EditorWriteJournal.Load(JournalPath));
    }

    [Fact]
    public void Append_SameWriteTwice_IsStoredOnce()
    {
        var w = new EditorWrite(@"C:\game\a.conversation", "AA", "BB");

        EditorWriteJournal.Append(JournalPath, [w]);
        EditorWriteJournal.Append(JournalPath, [w]);

        Assert.Single(EditorWriteJournal.Load(JournalPath));
    }

    [Fact]
    public void Load_CorruptFile_IsEmpty()
    {
        // Losing the journal can only make Restore Full Backup skip more, never overwrite more.
        File.WriteAllText(JournalPath, "{ not json");

        Assert.Empty(EditorWriteJournal.Load(JournalPath));
    }
}
