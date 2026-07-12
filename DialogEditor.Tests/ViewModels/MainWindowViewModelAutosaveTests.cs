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
}
