using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Autosave sidecar behaviour on MainWindowViewModel (spec 2026-07-12).
public class MainWindowViewModelAutosaveTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _dir;
    private string _projectPath => Path.Combine(_dir, "p.dialogproject");

    public MainWindowViewModelAutosaveTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_as_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _dir = Path.Combine(Path.GetTempPath(), $"asvm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void InjectProject(MainWindowViewModel vm, DialogProject project)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        mi.Invoke(vm, [project]);
    }

    private void InjectProjectPath(MainWindowViewModel vm)
    {
        var fi = typeof(MainWindowViewModel)
            .GetField("_projectPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, _projectPath);
    }

    private MainWindowViewModel DirtyVm()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        InjectProjectPath(vm);
        vm.IsModified = true;
        return vm;
    }

    [Fact]
    public void AutosaveTick_Dirty_WritesLoadableSidecar()
    {
        var vm = DirtyVm();

        vm.AutosaveTick();

        var sidecar = AutosaveRecovery.SidecarPath(_projectPath);
        Assert.True(File.Exists(sidecar));
        Assert.Equal("T", DialogProjectSerializer.LoadFromFile(sidecar).Name);
        Assert.True(vm.IsModified); // a tick is not a save
    }

    [Fact]
    public void AutosaveTick_Clean_WritesNothing()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        InjectProjectPath(vm);

        vm.AutosaveTick();

        Assert.False(File.Exists(AutosaveRecovery.SidecarPath(_projectPath)));
    }

    [Fact]
    public void AutosaveTick_NoProject_NoThrow() => MakeVm().AutosaveTick();

    [Fact]
    public void SaveProject_DeletesSidecar()
    {
        var vm = DirtyVm();
        vm.AutosaveTick();
        Assert.True(File.Exists(AutosaveRecovery.SidecarPath(_projectPath)));

        vm.SaveProjectCommand.Execute(null);

        Assert.False(File.Exists(AutosaveRecovery.SidecarPath(_projectPath)));
        Assert.True(File.Exists(_projectPath));
    }

    [Fact]
    public void DiscardAndProceed_DeletesSidecar()
    {
        var vm = DirtyVm();
        vm.AutosaveTick();
        vm.GuardDirtyThen(() => { });

        vm.DiscardAndProceed();

        Assert.False(File.Exists(AutosaveRecovery.SidecarPath(_projectPath)));
    }

    // ── Restore offer on open (spec §4) ─────────────────────────────────────

    private static Task InvokeLoadProjectAsync(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("LoadProjectAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;
    }

    /// Real file named "Saved"; newer sidecar named "Recovered".
    private void WriteProjectAndNewerSidecar()
    {
        DialogProjectSerializer.SaveToFile(_projectPath, DialogProject.Empty("Saved"));
        var sidecar = AutosaveRecovery.SidecarPath(_projectPath);
        DialogProjectSerializer.SaveToFile(sidecar, DialogProject.Empty("Recovered"));
        File.SetLastWriteTimeUtc(_projectPath, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(sidecar,      DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Open_NewerSidecar_Restore_LoadsSidecarDirty_AndKeepsIt()
    {
        WriteProjectAndNewerSidecar();
        var vm = MakeVm();
        vm.ConfirmRestoreAutosave = _ => Task.FromResult(true);

        await InvokeLoadProjectAsync(vm, _projectPath);

        Assert.Equal("Recovered", vm.CurrentProjectName);
        Assert.True(vm.IsModified); // unsaved until an explicit save
        Assert.True(File.Exists(AutosaveRecovery.SidecarPath(_projectPath))); // double-crash protection
    }

    [Fact]
    public async Task Open_NewerSidecar_Decline_DeletesSidecar_LoadsSaved()
    {
        WriteProjectAndNewerSidecar();
        var vm = MakeVm();
        vm.ConfirmRestoreAutosave = _ => Task.FromResult(false);

        await InvokeLoadProjectAsync(vm, _projectPath);

        Assert.Equal("Saved", vm.CurrentProjectName);
        Assert.False(File.Exists(AutosaveRecovery.SidecarPath(_projectPath)));
    }

    [Fact]
    public async Task Open_StaleSidecar_SilentlyDeleted_NoSeamCall()
    {
        DialogProjectSerializer.SaveToFile(_projectPath, DialogProject.Empty("Saved"));
        var sidecar = AutosaveRecovery.SidecarPath(_projectPath);
        DialogProjectSerializer.SaveToFile(sidecar, DialogProject.Empty("Old"));
        File.SetLastWriteTimeUtc(sidecar,      DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(_projectPath, DateTime.UtcNow.AddMinutes(-1));
        var vm = MakeVm();
        var consulted = false;
        vm.ConfirmRestoreAutosave = _ => { consulted = true; return Task.FromResult(false); };

        await InvokeLoadProjectAsync(vm, _projectPath);

        Assert.Equal("Saved", vm.CurrentProjectName);
        Assert.False(consulted);
        Assert.False(File.Exists(sidecar));
    }

    [Fact]
    public async Task Open_NewerSidecar_NullSeam_LoadsSaved_KeepsSidecar()
    {
        WriteProjectAndNewerSidecar();
        var vm = MakeVm(); // no seam wired — never destroy recovery data

        await InvokeLoadProjectAsync(vm, _projectPath);

        Assert.Equal("Saved", vm.CurrentProjectName);
        Assert.True(File.Exists(AutosaveRecovery.SidecarPath(_projectPath)));
    }
}
