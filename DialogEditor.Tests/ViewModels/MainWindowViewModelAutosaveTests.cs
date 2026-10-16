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
        AutosaveRecovery.FallbackRootOverride = _fallback;
    }

    private readonly string _fallback = Path.Combine(Path.GetTempPath(), $"asvm_fb_{Guid.NewGuid():N}");

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
        AutosaveRecovery.FallbackRootOverride = null;
        try { Directory.Delete(_fallback, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    // ── Issue 37: fallback when the project's folder can't be written ────────────

    private MainWindowViewModel DirtyVmInUnwritableFolder(out string projectPath)
    {
        // A folder sitting where the sidecar file would go makes the write fail with
        // UnauthorizedAccessException, the same failure read-only media or a locked share gives.
        // (A missing folder doesn't work: the serializer creates folders.)
        projectPath = Path.Combine(_dir, "locked", "p.dialogproject");
        Directory.CreateDirectory(AutosaveRecovery.SidecarPath(projectPath));
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        typeof(MainWindowViewModel)
            .GetField("_projectPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, projectPath);
        vm.IsModified = true;
        return vm;
    }

    [Fact]
    public void AutosaveTick_UnwritableFolder_WritesTheFallbackCopy()
    {
        var vm = DirtyVmInUnwritableFolder(out var projectPath);

        vm.AutosaveTick();

        var fallback = AutosaveRecovery.FallbackSidecarPath(projectPath);
        Assert.True(File.Exists(fallback));
        Assert.Equal("T", DialogProjectSerializer.LoadFromFile(fallback).Name);
        Assert.True(vm.IsModified);
    }

    [Fact]
    public void AutosaveTick_UnwritableFolder_SaysWhereTheCopyLives_Once()
    {
        var vm = DirtyVmInUnwritableFolder(out _);

        vm.AutosaveTick();
        Assert.Contains("Status_AutosaveFallback", vm.StatusText);

        vm.StatusText = "something else";
        vm.AutosaveTick();
        Assert.Equal("something else", vm.StatusText);   // not repeated on every tick
    }

    [Fact]
    public async Task Open_NewerFallbackCopy_IsOffered_WithItsFolder_AndRestored()
    {
        DialogProjectSerializer.SaveToFile(_projectPath, DialogProject.Empty("Saved"));
        File.SetLastWriteTimeUtc(_projectPath, DateTime.UtcNow.AddMinutes(-10));
        var fallback = AutosaveRecovery.FallbackSidecarPath(_projectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
        DialogProjectSerializer.SaveToFile(fallback, DialogProject.Empty("Recovered"));
        File.SetLastWriteTimeUtc(fallback, DateTime.UtcNow.AddMinutes(-1));
        var vm = MakeVm();
        bool? offeredFallback = null;
        vm.ConfirmRestoreAutosave = gens => { offeredFallback = gens[0].IsFallback; return Task.FromResult<AutosaveGeneration?>(gens[0]); };

        await InvokeLoadProjectAsync(vm, _projectPath);

        Assert.True(offeredFallback);
        Assert.Equal("Recovered", vm.CurrentProjectName);
    }

    [Fact]
    public async Task Open_NewerLocalSidecar_IsOfferedWithoutAFolder()
    {
        WriteProjectAndNewerSidecar();
        var vm = MakeVm();
        bool? offeredFallback = null;
        vm.ConfirmRestoreAutosave = gens => { offeredFallback = gens[0].IsFallback; return Task.FromResult<AutosaveGeneration?>(null); };

        await InvokeLoadProjectAsync(vm, _projectPath);

        Assert.False(offeredFallback);
    }

    // ── Issue 36: choosing which generation to restore ──────────────────────────

    private void WriteProjectAndTwoGenerations()
    {
        DialogProjectSerializer.SaveToFile(_projectPath, DialogProject.Empty("Saved"));
        File.SetLastWriteTimeUtc(_projectPath, DateTime.UtcNow.AddMinutes(-20));
        var g1 = AutosaveRecovery.GenerationPath(_projectPath, 1);
        var g2 = AutosaveRecovery.GenerationPath(_projectPath, 2);
        DialogProjectSerializer.SaveToFile(g1, DialogProject.Empty("Newest"));
        DialogProjectSerializer.SaveToFile(g2, DialogProject.Empty("Earlier"));
        File.SetLastWriteTimeUtc(g1, DateTime.UtcNow.AddMinutes(-1));
        File.SetLastWriteTimeUtc(g2, DateTime.UtcNow.AddMinutes(-6));
    }

    [Fact]
    public async Task Open_OffersEveryGeneration_NewestFirst()
    {
        WriteProjectAndTwoGenerations();
        var vm = MakeVm();
        IReadOnlyList<AutosaveGeneration>? offered = null;
        vm.ConfirmRestoreAutosave = gens => { offered = gens; return Task.FromResult<AutosaveGeneration?>(null); };

        await InvokeLoadProjectAsync(vm, _projectPath);

        Assert.NotNull(offered);
        Assert.Equal([AutosaveRecovery.GenerationPath(_projectPath, 1), AutosaveRecovery.GenerationPath(_projectPath, 2)],
                     offered!.Select(g => g.Path));
    }

    [Fact]
    public async Task Open_RestoreAnEarlierGeneration_LoadsIt_AndKeepsEveryCopy()
    {
        WriteProjectAndTwoGenerations();
        var vm = MakeVm();
        vm.ConfirmRestoreAutosave = gens => Task.FromResult<AutosaveGeneration?>(gens[1]);

        await InvokeLoadProjectAsync(vm, _projectPath);

        Assert.Equal("Earlier", vm.CurrentProjectName);
        Assert.True(vm.IsModified);
        // Kept until an explicit save, so a second crash still has every copy (issue 36).
        Assert.True(File.Exists(AutosaveRecovery.GenerationPath(_projectPath, 1)));
        Assert.True(File.Exists(AutosaveRecovery.GenerationPath(_projectPath, 2)));
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
        vm.ConfirmRestoreAutosave = gens => Task.FromResult<AutosaveGeneration?>(gens[0]);

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
        vm.ConfirmRestoreAutosave = _ => Task.FromResult<AutosaveGeneration?>(null);

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
        vm.ConfirmRestoreAutosave = _ => { consulted = true; return Task.FromResult<AutosaveGeneration?>(null); };

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

    // ── Rotating generations (issue #11) ────────────────────────────────────

    [Fact]
    public void AutosaveTick_RotatesPreviousGenerationsBeforeWriting()
    {
        AppSettings.AutosaveGenerations = 3;
        var vm = DirtyVm();

        vm.AutosaveTick();                                    // generation 1 = "T"
        InjectProject(vm, DialogProject.Empty("T2"));
        vm.IsModified = true;
        vm.AutosaveTick();                                    // "T" pushed down to 2

        Assert.Equal("T2", DialogProjectSerializer.LoadFromFile(
            AutosaveRecovery.GenerationPath(_projectPath, 1)).Name);
        Assert.Equal("T", DialogProjectSerializer.LoadFromFile(
            AutosaveRecovery.GenerationPath(_projectPath, 2)).Name);
    }

    [Fact]
    public void AutosaveTick_GenerationsOne_KeepsASingleSidecar()
    {
        AppSettings.AutosaveGenerations = 1;
        var vm = DirtyVm();

        vm.AutosaveTick();
        vm.AutosaveTick();

        Assert.True(File.Exists(AutosaveRecovery.GenerationPath(_projectPath, 1)));
        Assert.False(File.Exists(AutosaveRecovery.GenerationPath(_projectPath, 2)));
    }

    [Fact]
    public void SaveProject_DeletesEveryGeneration()
    {
        AppSettings.AutosaveGenerations = 3;
        var vm = DirtyVm();
        vm.AutosaveTick();
        vm.IsModified = true;
        vm.AutosaveTick();
        Assert.True(File.Exists(AutosaveRecovery.GenerationPath(_projectPath, 2)));

        vm.SaveProjectCommand.Execute(null);

        Assert.False(File.Exists(AutosaveRecovery.GenerationPath(_projectPath, 1)));
        Assert.False(File.Exists(AutosaveRecovery.GenerationPath(_projectPath, 2)));
    }
}
