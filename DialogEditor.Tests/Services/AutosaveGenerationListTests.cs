using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// Issue 36: the restore offer can list every autosave generation, so a user whose newest copy
/// is damaged or caught a mistake can recover the one before it.
public class AutosaveGenerationListTests : IDisposable
{
    private readonly string _dir      = Path.Combine(Path.GetTempPath(), $"autosave_gen_{Guid.NewGuid():N}");
    private readonly string _fallback = Path.Combine(Path.GetTempPath(), $"autosave_genfb_{Guid.NewGuid():N}");
    private string ProjectPath => Path.Combine(_dir, "p.dialogproject");

    public AutosaveGenerationListTests()
    {
        Directory.CreateDirectory(_dir);
        AutosaveRecovery.FallbackRootOverride = _fallback;
    }

    public void Dispose()
    {
        AutosaveRecovery.FallbackRootOverride = null;
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_fallback, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private string Write(int generation, int minutesAgo, bool fallback = false)
    {
        var path = AutosaveRecovery.GenerationPath(ProjectPath, generation, fallback);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
        return path;
    }

    [Fact]
    public void ListGenerations_NoCopies_IsEmpty()
        => Assert.Empty(AutosaveRecovery.ListGenerations(ProjectPath));

    [Fact]
    public void ListGenerations_IsNewestFirst()
    {
        var g1 = Write(1, minutesAgo: 1);
        var g2 = Write(2, minutesAgo: 6);
        var g3 = Write(3, minutesAgo: 11);

        var list = AutosaveRecovery.ListGenerations(ProjectPath);

        Assert.Equal([g1, g2, g3], list.Select(g => g.Path));
        Assert.All(list, g => Assert.False(g.IsFallback));
    }

    [Fact]
    public void ListGenerations_IncludesFallbackCopies_InTimeOrder()
    {
        var local = Write(1, minutesAgo: 10);
        var fb1   = Write(1, minutesAgo: 1, fallback: true);
        var fb2   = Write(2, minutesAgo: 5, fallback: true);

        var list = AutosaveRecovery.ListGenerations(ProjectPath);

        Assert.Equal([fb1, fb2, local], list.Select(g => g.Path));
        Assert.Equal([true, true, false], list.Select(g => g.IsFallback));
    }

    [Fact]
    public void ListGenerations_ReportsEachCopysTime()
    {
        var path = Write(1, minutesAgo: 3);
        var only = Assert.Single(AutosaveRecovery.ListGenerations(ProjectPath));
        Assert.Equal(File.GetLastWriteTimeUtc(path), only.TimeUtc);
    }
}
