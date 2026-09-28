using DialogEditor.Core.Backup;

namespace DialogEditor.Tests.Backup;

public class BackupServiceTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose() => Directory.Delete(_tmp, recursive: true);

    [Fact]
    public async Task BackupAsync_CopiesAllFilesPreservingStructure()
    {
        var src = Path.Combine(_tmp, "source");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.txt"),        "A");
        File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "B");

        var dest = Path.Combine(_tmp, "backup");

        await BackupService.BackupAsync(src, dest, CancellationToken.None);

        Assert.Equal("A", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(dest, "sub", "b.txt")));
    }


    [Fact]
    public async Task BackupAsync_EmptySource_CreatesEmptyDest()
    {
        var src  = Path.Combine(_tmp, "empty");
        var dest = Path.Combine(_tmp, "out");
        Directory.CreateDirectory(src);

        await BackupService.BackupAsync(src, dest, CancellationToken.None);

        Assert.True(Directory.Exists(dest));
        Assert.Empty(Directory.GetFiles(dest));
    }
}
