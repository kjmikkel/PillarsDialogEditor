using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// The Validate Text Tags dirty guard (three-way consent seam) on MainWindowViewModel.
public class MainWindowViewModelTextTagTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _projectPath;

    public MainWindowViewModelTextTagTests()
    {
        Loc.Configure(new StubStringProvider());
        // Isolate AppSettings so the VM constructor doesn't auto-load a game folder.
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_tt_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _projectPath = Path.Combine(Path.GetTempPath(), $"tt_{Guid.NewGuid():N}.dialogproject");
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { if (File.Exists(_projectPath)) File.Delete(_projectPath); } catch (Exception) { /* best-effort */ }
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

    [Fact]
    public async Task NoProject_ReturnsNull()
    {
        var vm = MakeVm();
        Assert.Null(await vm.RequestTextTagValidationAsync());
    }

    [Fact]
    public async Task CleanProject_ReturnsVm_WithoutConsultingSeam()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        var consulted = false;
        vm.ConfirmScanWithUnsavedChanges = () => { consulted = true; return Task.FromResult(ScanDirtyChoice.Cancel); };
        var result = await vm.RequestTextTagValidationAsync();
        Assert.NotNull(result);
        Assert.False(consulted);
    }

    [Fact]
    public async Task Dirty_Cancel_ReturnsNull_AndDoesNotSave()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        InjectProjectPath(vm);
        vm.IsModified = true;
        vm.ConfirmScanWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.Cancel);

        Assert.Null(await vm.RequestTextTagValidationAsync());
        Assert.True(vm.IsModified);              // no save happened
        Assert.False(File.Exists(_projectPath)); // nothing written
    }

    [Fact]
    public async Task Dirty_ScanSavedOnly_ReturnsVm_WithoutSave()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        InjectProjectPath(vm);
        vm.IsModified = true;
        vm.ConfirmScanWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.ScanSavedOnly);

        Assert.NotNull(await vm.RequestTextTagValidationAsync());
        Assert.True(vm.IsModified);              // still dirty — nothing saved
        Assert.False(File.Exists(_projectPath));
    }

    [Fact]
    public async Task Dirty_SaveAndScan_SavesThenReturnsVm()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        InjectProjectPath(vm);
        vm.IsModified = true;
        vm.ConfirmScanWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.SaveAndScan);

        Assert.NotNull(await vm.RequestTextTagValidationAsync());
        Assert.False(vm.IsModified);             // SaveProject flipped the flag
        Assert.True(File.Exists(_projectPath));  // and wrote the file
    }

    [Fact]
    public async Task Dirty_NoSeamWired_ReturnsNull()
    {
        // Safety: a dirty project with no dialog wired must not silently scan.
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        vm.IsModified = true;
        Assert.Null(await vm.RequestTextTagValidationAsync());
    }
}
