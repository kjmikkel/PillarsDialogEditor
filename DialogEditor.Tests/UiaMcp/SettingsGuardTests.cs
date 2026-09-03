using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// The settings lifecycle ported from tools/ui-automation/DriveApp.ps1 and hardened.
/// The backup lives in a FILE rather than memory because a verification run can span
/// several processes, and because a crashed run must not strand the user's real
/// LastProjectPath — which is exactly what happened before this server existed.
/// </summary>
public class SettingsGuardTests : IDisposable
{
    private readonly string _dir;
    private readonly string _settings;
    private readonly string _backup;

    public SettingsGuardTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "uiamcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settings = Path.Combine(_dir, "settings.json");
        _backup = Path.Combine(_dir, "settings.backup.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private SettingsGuard NewGuard() => new(_settings, _backup);

    private void WriteSettings(string lastProject, string theme = "Dark") =>
        File.WriteAllText(_settings,
            $$"""{"LastProjectPath":"{{lastProject}}","Theme":"{{theme}}"}""");

    [Fact]
    public void BackupCopiesSettingsToBackupPath()
    {
        WriteSettings("C:/real/project.dialogproject");
        NewGuard().Backup();
        Assert.True(File.Exists(_backup));
        Assert.Contains("C:/real/project.dialogproject", File.ReadAllText(_backup));
    }

    [Fact]
    public void BackupDoesNotOverwriteAnExistingBackup()
    {
        // A previous run died before restoring: the OLDER backup holds the genuine
        // settings, so a second Backup() must not clobber it with run-mutated state.
        File.WriteAllText(_backup, """{"LastProjectPath":"C:/genuine.dialogproject"}""");
        WriteSettings("C:/temp/scratch.dialogproject");

        NewGuard().Backup();

        Assert.Contains("C:/genuine.dialogproject", File.ReadAllText(_backup));
    }

    [Fact]
    public void RestoreCopiesBackupBackAndRemovesIt()
    {
        WriteSettings("C:/real/project.dialogproject");
        var guard = NewGuard();
        guard.Backup();
        WriteSettings("C:/temp/scratch.dialogproject");

        guard.Restore();

        Assert.Contains("C:/real/project.dialogproject", File.ReadAllText(_settings));
        Assert.False(File.Exists(_backup));
    }

    [Fact]
    public void RestoreWithoutABackupReportsThatNothingWasRestored()
    {
        WriteSettings("C:/temp/scratch.dialogproject");
        var message = NewGuard().Restore();
        Assert.Contains("no backup", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecoverStaleBackupRestoresLeftoverStateAndReportsIt()
    {
        // Server startup after a crashed session.
        File.WriteAllText(_backup, """{"LastProjectPath":"C:/genuine.dialogproject"}""");
        WriteSettings("C:/temp/vanished.dialogproject");

        var message = NewGuard().RecoverStaleBackup();

        Assert.NotNull(message);
        Assert.Contains("C:/genuine.dialogproject", File.ReadAllText(_settings));
        Assert.False(File.Exists(_backup));
    }

    [Fact]
    public void RecoverStaleBackupReturnsNullWhenThereIsNothingToRecover()
    {
        WriteSettings("C:/real/project.dialogproject");
        Assert.Null(NewGuard().RecoverStaleBackup());
    }

    [Fact]
    public void SetLastProjectPathPreservesEveryOtherKey()
    {
        WriteSettings("C:/real/project.dialogproject", theme: "Light");

        NewGuard().SetLastProjectPath("");

        var json = File.ReadAllText(_settings);
        Assert.Contains("\"LastProjectPath\": \"\"", json);
        Assert.Contains("Light", json);
    }
}
