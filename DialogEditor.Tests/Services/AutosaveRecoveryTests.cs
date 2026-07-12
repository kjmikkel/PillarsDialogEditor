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
