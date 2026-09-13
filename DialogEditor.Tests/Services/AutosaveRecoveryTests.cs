using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class AutosaveRecoveryTests : IDisposable
{
    private readonly string _dir;
    private string ProjectPath => Path.Combine(_dir, "p.dialogproject");

    public AutosaveRecoveryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"autosave_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    [Fact]
    public void SidecarPath_AppendsAutosaveExtension()
        => Assert.Equal(ProjectPath + ".autosave", AutosaveRecovery.SidecarPath(ProjectPath));

    [Fact]
    public void Check_NoSidecar_None()
    {
        File.WriteAllText(ProjectPath, "{}");
        Assert.Equal(AutosaveState.None, AutosaveRecovery.Check(ProjectPath).State);
    }

    [Fact]
    public void Check_NewerSidecar_Newer_WithTimestamp()
    {
        File.WriteAllText(ProjectPath, "{}");
        var sidecar = AutosaveRecovery.SidecarPath(ProjectPath);
        File.WriteAllText(sidecar, "{}");
        File.SetLastWriteTimeUtc(ProjectPath, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(sidecar,     DateTime.UtcNow.AddMinutes(-1));
        var r = AutosaveRecovery.Check(ProjectPath);
        Assert.Equal(AutosaveState.Newer, r.State);
        Assert.NotNull(r.SidecarTimeUtc);
    }

    [Fact]
    public void Check_StaleSidecar_Stale()
    {
        File.WriteAllText(ProjectPath, "{}");
        var sidecar = AutosaveRecovery.SidecarPath(ProjectPath);
        File.WriteAllText(sidecar, "{}");
        File.SetLastWriteTimeUtc(sidecar,     DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(ProjectPath, DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(AutosaveState.Stale, AutosaveRecovery.Check(ProjectPath).State);
    }

    [Fact]
    public void Check_SidecarWithoutProjectFile_Newer()
    {
        File.WriteAllText(AutosaveRecovery.SidecarPath(ProjectPath), "{}");
        Assert.Equal(AutosaveState.Newer, AutosaveRecovery.Check(ProjectPath).State);
    }

    [Fact]
    public void TryDelete_RemovesSidecar_AndToleratesAbsence()
    {
        var sidecar = AutosaveRecovery.SidecarPath(ProjectPath);
        File.WriteAllText(sidecar, "{}");
        AutosaveRecovery.TryDelete(ProjectPath);
        Assert.False(File.Exists(sidecar));
        AutosaveRecovery.TryDelete(ProjectPath); // absent → no throw
    }
}

/// Rotating autosave generations (issue #11). Generation 1 deliberately keeps the
/// historical `.autosave` name so a sidecar written before the upgrade is still found.
public class AutosaveRecoveryGenerationTests : IDisposable
{
    private readonly string _dir;
    private string ProjectPath => Path.Combine(_dir, "p.dialogproject");

    public AutosaveRecoveryGenerationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"autogen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private string Gen(int n) => AutosaveRecovery.GenerationPath(ProjectPath, n);
    private void Write(int n, string content) => File.WriteAllText(Gen(n), content);
    private string? ReadOrNull(int n) => File.Exists(Gen(n)) ? File.ReadAllText(Gen(n)) : null;

    [Fact]
    public void GenerationPath_One_IsTheLegacySidecarPath()
        => Assert.Equal(AutosaveRecovery.SidecarPath(ProjectPath), Gen(1));

    [Fact]
    public void GenerationPath_BeyondOne_AppendsTheNumber()
    {
        Assert.Equal(ProjectPath + ".autosave.2", Gen(2));
        Assert.Equal(ProjectPath + ".autosave.3", Gen(3));
    }

    [Fact]
    public void Rotate_ShiftsEachGenerationDownAndFreesGenerationOne()
    {
        Write(1, "newest");
        Write(2, "older");
        AutosaveRecovery.Rotate(ProjectPath, keep: 3);
        Assert.Null(ReadOrNull(1));          // freed for the incoming write
        Assert.Equal("newest", ReadOrNull(2));
        Assert.Equal("older",  ReadOrNull(3));
    }

    [Fact]
    public void Rotate_DropsTheOldestBeyondKeep()
    {
        Write(1, "a");
        Write(2, "b");
        Write(3, "c");
        AutosaveRecovery.Rotate(ProjectPath, keep: 3);
        Assert.Equal("a", ReadOrNull(2));
        Assert.Equal("b", ReadOrNull(3));
        Assert.Null(ReadOrNull(4));          // "c" dropped, nothing spilled past keep
    }

    [Fact]
    public void Rotate_KeepOne_JustClearsGenerationOne()
    {
        Write(1, "a");
        AutosaveRecovery.Rotate(ProjectPath, keep: 1);
        Assert.Null(ReadOrNull(1));
        Assert.Null(ReadOrNull(2));          // no rotation happens at keep: 1
    }

    [Fact]
    public void Rotate_ToleratesGapsAndAbsentGenerations()
    {
        Write(3, "only-an-old-one");
        AutosaveRecovery.Rotate(ProjectPath, keep: 5);   // no throw
        Assert.Equal("only-an-old-one", ReadOrNull(4));
    }

    [Fact]
    public void Rotate_NoGenerationsAtAll_DoesNothing()
    {
        AutosaveRecovery.Rotate(ProjectPath, keep: 3);   // no throw, no files created
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void TryDelete_RemovesEveryGeneration_NotJustTheNewest()
    {
        // Written beyond any plausible `keep` so lowering the setting can't strand files.
        for (var n = 1; n <= 10; n++) Write(n, "x");
        AutosaveRecovery.TryDelete(ProjectPath);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Check_StillReadsGenerationOne_WhenOlderGenerationsExist()
    {
        File.WriteAllText(ProjectPath, "{}");
        Write(1, "{}");
        Write(2, "{}");
        File.SetLastWriteTimeUtc(ProjectPath, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(Gen(1),      DateTime.UtcNow.AddMinutes(-1));
        var r = AutosaveRecovery.Check(ProjectPath);
        Assert.Equal(AutosaveState.Newer, r.State);
        Assert.Equal(Gen(1), r.SidecarPath);
    }
}
