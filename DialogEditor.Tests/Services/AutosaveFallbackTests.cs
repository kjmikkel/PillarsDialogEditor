using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// Issue 37: when the project's own folder can't be written, autosave falls back to a per-user
/// folder so the user keeps crash protection. These cover where the fallback copy lives, how
/// Check finds it, and that deletion sweeps it too.
public class AutosaveFallbackTests : IDisposable
{
    private readonly string _dir      = Path.Combine(Path.GetTempPath(), $"autosave_fb_{Guid.NewGuid():N}");
    private readonly string _fallback = Path.Combine(Path.GetTempPath(), $"autosave_root_{Guid.NewGuid():N}");
    private string ProjectPath => Path.Combine(_dir, "p.dialogproject");

    public AutosaveFallbackTests()
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

    private void WriteFallback(int generation, DateTime utc)
    {
        var path = AutosaveRecovery.GenerationPath(ProjectPath, generation, fallback: true);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        File.SetLastWriteTimeUtc(path, utc);
    }

    [Fact]
    public void FallbackSidecarPath_LivesUnderTheFallbackRoot()
    {
        var path = AutosaveRecovery.FallbackSidecarPath(ProjectPath);
        Assert.Equal(_fallback, Path.GetDirectoryName(path));
        Assert.StartsWith("p.dialogproject.", Path.GetFileName(path));
        Assert.EndsWith(".autosave", path);
    }

    [Fact]
    public void FallbackSidecarPath_DiffersForSameFileNameInDifferentFolders()
    {
        var other = Path.Combine(_dir, "sub", "p.dialogproject");
        Assert.NotEqual(AutosaveRecovery.FallbackSidecarPath(ProjectPath), AutosaveRecovery.FallbackSidecarPath(other));
    }

    [Fact]
    public void FallbackSidecarPath_IgnoresPathCasing()
        => Assert.Equal(AutosaveRecovery.FallbackSidecarPath(ProjectPath),
                        AutosaveRecovery.FallbackSidecarPath(ProjectPath.ToUpperInvariant()));

    [Fact]
    public void FallbackGenerations_NumberLikeTheSidecar()
    {
        Assert.Equal(AutosaveRecovery.FallbackSidecarPath(ProjectPath),
                     AutosaveRecovery.GenerationPath(ProjectPath, 1, fallback: true));
        Assert.Equal(AutosaveRecovery.FallbackSidecarPath(ProjectPath) + ".2",
                     AutosaveRecovery.GenerationPath(ProjectPath, 2, fallback: true));
    }

    [Fact]
    public void Check_FindsANewerFallbackCopy_WhenThereIsNoSidecar()
    {
        File.WriteAllText(ProjectPath, "{}");
        File.SetLastWriteTimeUtc(ProjectPath, DateTime.UtcNow.AddMinutes(-10));
        WriteFallback(1, DateTime.UtcNow.AddMinutes(-1));

        var r = AutosaveRecovery.Check(ProjectPath);

        Assert.Equal(AutosaveState.Newer, r.State);
        Assert.True(r.IsFallback);
        Assert.Equal(AutosaveRecovery.FallbackSidecarPath(ProjectPath), r.SidecarPath);
    }

    [Fact]
    public void Check_PrefersTheNewerOfSidecarAndFallback()
    {
        File.WriteAllText(ProjectPath, "{}");
        File.SetLastWriteTimeUtc(ProjectPath, DateTime.UtcNow.AddMinutes(-20));
        var sidecar = AutosaveRecovery.SidecarPath(ProjectPath);
        File.WriteAllText(sidecar, "{}");
        File.SetLastWriteTimeUtc(sidecar, DateTime.UtcNow.AddMinutes(-10));
        WriteFallback(1, DateTime.UtcNow.AddMinutes(-1));

        var r = AutosaveRecovery.Check(ProjectPath);

        Assert.True(r.IsFallback);
        Assert.Equal(AutosaveState.Newer, r.State);
    }

    [Fact]
    public void Check_LocalSidecar_IsNotFallback()
    {
        File.WriteAllText(ProjectPath, "{}");
        File.SetLastWriteTimeUtc(ProjectPath, DateTime.UtcNow.AddMinutes(-10));
        var sidecar = AutosaveRecovery.SidecarPath(ProjectPath);
        File.WriteAllText(sidecar, "{}");
        File.SetLastWriteTimeUtc(sidecar, DateTime.UtcNow.AddMinutes(-1));

        Assert.False(AutosaveRecovery.Check(ProjectPath).IsFallback);
    }

    [Fact]
    public void Check_StaleFallbackCopy_IsStale()
    {
        File.WriteAllText(ProjectPath, "{}");
        WriteFallback(1, DateTime.UtcNow.AddMinutes(-30));
        File.SetLastWriteTimeUtc(ProjectPath, DateTime.UtcNow.AddMinutes(-1));

        var r = AutosaveRecovery.Check(ProjectPath);

        Assert.Equal(AutosaveState.Stale, r.State);
        Assert.True(r.IsFallback);
    }

    [Fact]
    public void TryDelete_SweepsTheFallbackGenerationsToo()
    {
        WriteFallback(1, DateTime.UtcNow);
        WriteFallback(2, DateTime.UtcNow);
        File.WriteAllText(AutosaveRecovery.SidecarPath(ProjectPath), "{}");

        AutosaveRecovery.TryDelete(ProjectPath);

        Assert.Empty(Directory.GetFiles(_fallback));
        Assert.False(File.Exists(AutosaveRecovery.SidecarPath(ProjectPath)));
    }

    [Fact]
    public void Rotate_Fallback_AgesTheFallbackGenerations()
    {
        WriteFallback(1, DateTime.UtcNow);
        AutosaveRecovery.Rotate(ProjectPath, keep: 3, fallback: true);
        Assert.False(File.Exists(AutosaveRecovery.GenerationPath(ProjectPath, 1, fallback: true)));
        Assert.True(File.Exists(AutosaveRecovery.GenerationPath(ProjectPath, 2, fallback: true)));
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), true)]
    [InlineData(typeof(DirectoryNotFoundException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(IOException), false)]   // e.g. a sharing violation: a moment, not the location
    public void IsLocationFailure_ClassifiesTheException(Type type, bool expected)
        => Assert.Equal(expected, AutosaveRecovery.IsLocationFailure((Exception)Activator.CreateInstance(type)!));

    [Fact]
    public void IsLocationFailure_WriteProtectedVolume_IsALocationFailure()
        => Assert.True(AutosaveRecovery.IsLocationFailure(new IOException("write-protected", unchecked((int)0x80070013))));
}
