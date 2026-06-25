using System.IO.Compression;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// <summary>
/// Tests for DialogPackHelper — the .dialogpack extraction and VO copy service.
/// </summary>
public class DialogPackHelperTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"DPTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>Creates a .dialogpack ZIP at the given path with specified entries.</summary>
    private static string CreatePack(string dir, IEnumerable<(string entryName, string content)> entries)
    {
        var packPath = Path.Combine(dir, "test.dialogpack");
        using var archive = ZipFile.Open(packPath, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var sw = new StreamWriter(entry.Open());
            sw.Write(content);
        }
        return packPath;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    // Test 1: Extract returns the project file path
    [Fact]
    public void Extract_ReturnsProjectFilePath()
    {
        var dir  = MakeTempDir();
        var pack = CreatePack(dir, [("project.dialogproject", "<root />")]);

        var result = DialogPackHelper.Extract(pack);
        _tempDirs.Add(result.TempDir); // ensure cleanup

        Assert.True(File.Exists(result.ProjectFilePath),
            $"Expected project file at '{result.ProjectFilePath}' to exist.");
        Assert.EndsWith("project.dialogproject", result.ProjectFilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    // Test 2: Extract returns non-null VoFolderPath when vo/ is present
    [Fact]
    public void Extract_ReturnsVoFolderPath_WhenVoPresent()
    {
        var dir  = MakeTempDir();
        var pack = CreatePack(dir, [
            ("project.dialogproject", "<root />"),
            ("vo/test.wem", "fakewem")
        ]);

        var result = DialogPackHelper.Extract(pack);
        _tempDirs.Add(result.TempDir);

        Assert.NotNull(result.VoFolderPath);
        Assert.True(File.Exists(Path.Combine(result.VoFolderPath!, "test.wem")),
            "Expected test.wem to exist under the extracted vo/ folder.");
    }

    // Test 3: Extract returns null VoFolderPath when vo/ is absent
    [Fact]
    public void Extract_ReturnsNullVoFolder_WhenVoAbsent()
    {
        var dir  = MakeTempDir();
        var pack = CreatePack(dir, [("project.dialogproject", "<root />")]);

        var result = DialogPackHelper.Extract(pack);
        _tempDirs.Add(result.TempDir);

        Assert.Null(result.VoFolderPath);
    }

    // Test 4: Extract throws InvalidOperationException when project file is missing
    [Fact]
    public void Extract_Throws_WhenProjectFileMissing()
    {
        var dir  = MakeTempDir();
        var pack = CreatePack(dir, [("FORMAT.md", "# Format")]);

        Assert.Throws<InvalidOperationException>(() =>
        {
            var result = DialogPackHelper.Extract(pack);
            _tempDirs.Add(result.TempDir);
        });
    }

    // Test 5: IsDialogPack returns true for .dialogpack extension
    [Fact]
    public void IsDialogPack_ReturnsTrueForDialogPack()
    {
        Assert.True(DialogPackHelper.IsDialogPack("foo.dialogpack"));
        Assert.True(DialogPackHelper.IsDialogPack("foo.DIALOGPACK"));
        Assert.True(DialogPackHelper.IsDialogPack(@"C:\mods\my_mod.dialogpack"));
    }

    // Test 6: IsDialogPack returns false for .dialogproject extension
    [Fact]
    public void IsDialogPack_ReturnsFalseForDialogProject()
    {
        Assert.False(DialogPackHelper.IsDialogPack("foo.dialogproject"));
        Assert.False(DialogPackHelper.IsDialogPack("foo.zip"));
        Assert.False(DialogPackHelper.IsDialogPack("foo.dialogpack.bak"));
    }

    // Test 7: CopyVoToGame copies all .wem files preserving relative structure
    [Fact]
    public void CopyVoToGame_CopiesAllWemFiles()
    {
        var sourceDir = MakeTempDir();
        var destDir   = MakeTempDir();

        // Create nested .wem files
        var subDir = Path.Combine(sourceDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(sourceDir, "root.wem"),  "data");
        File.WriteAllText(Path.Combine(subDir,    "nested.wem"), "data");
        // A non-.wem file that should NOT be copied
        File.WriteAllText(Path.Combine(sourceDir, "readme.txt"), "ignore me");

        DialogPackHelper.CopyVoToGame(sourceDir, destDir);

        Assert.True(File.Exists(Path.Combine(destDir, "root.wem")),
            "root.wem should be copied to dest root.");
        Assert.True(File.Exists(Path.Combine(destDir, "sub", "nested.wem")),
            "sub/nested.wem should be copied preserving subdirectory.");
        Assert.False(File.Exists(Path.Combine(destDir, "readme.txt")),
            "Non-.wem files should not be copied.");
    }
}
