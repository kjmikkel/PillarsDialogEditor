using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Save Project As: classic rebind semantics — the editor switches to the new
/// file, the internal Name follows the new filename, the original is untouched.
/// Spec: docs/superpowers/specs/2026-07-05-save-project-as-design.md
public class MainWindowViewModelSaveAsTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _tempDir;

    public MainWindowViewModelSaveAsTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_saveas_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"saveas_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MainWindowViewModel MakeVm(string? saveResult = null) =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker(saveResult: saveResult));

    private static Task InvokeLoadProjectAsync(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("LoadProjectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;
    }

    private static void InjectProject(MainWindowViewModel vm, DialogProject project)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("SetProject",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        mi.Invoke(vm, [project]);
    }

    /// Writes an empty project named after the file into _tempDir; returns its path.
    private string WriteProject(string relativePath)
    {
        var path = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DialogProjectSerializer.SaveToFile(
            path, DialogProject.Empty(Path.GetFileNameWithoutExtension(path)));
        return path;
    }

    /// VM with the given project opened from disk and the picker primed to answer saveAsTarget.
    private async Task<MainWindowViewModel> OpenVm(string projectPath, string? saveAsTarget)
    {
        var vm = MakeVm(saveResult: saveAsTarget);
        await InvokeLoadProjectAsync(vm, projectPath);
        return vm;
    }

    // ── Core rebind behaviour ─────────────────────────────────────────────

    [Fact]
    public async Task SaveAs_WritesNewFile_RenamesProject_AndRebinds()
    {
        var orig    = WriteProject("orig.dialogproject");
        var target  = Path.Combine(_tempDir, "fork", "forked.dialogproject");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var vm      = await OpenVm(orig, target);

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target), "Save As should write the new file.");
        Assert.Equal("forked", DialogProjectSerializer.LoadFromFile(target).Name);
        Assert.Equal("orig", DialogProjectSerializer.LoadFromFile(orig).Name);   // original untouched
        Assert.Equal(target, AppSettings.LastProjectPath);
        Assert.Equal("forked", vm.CurrentProjectName);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task SaveAs_SubsequentSave_TargetsNewPath()
    {
        var orig   = WriteProject("orig.dialogproject");
        var target = Path.Combine(_tempDir, "forked.dialogproject");
        var vm     = await OpenVm(orig, target);

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        // A later edit + plain Save must land in the NEW file only.
        InjectProject(vm, DialogProjectSerializer.LoadFromFile(target).WithNewConversation("extra"));
        vm.IsModified = true;
        vm.SaveProjectCommand.Execute(null);

        Assert.True(DialogProjectSerializer.LoadFromFile(target).IsNewConversation("extra"));
        Assert.False(DialogProjectSerializer.LoadFromFile(orig).IsNewConversation("extra"));
    }

    [Fact]
    public async Task SaveAs_Cancelled_IsNoOp()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, saveAsTarget: null);   // picker cancels

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.Equal(orig, AppSettings.LastProjectPath);
        Assert.Equal("orig", vm.CurrentProjectName);
        Assert.Single(Directory.GetFiles(_tempDir, "*.dialogproject"));
    }

    [Fact]
    public async Task SaveAs_SamePathChosen_BehavesAsPlainSave()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, saveAsTarget: orig);
        vm.IsModified = true;

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.Equal("orig", DialogProjectSerializer.LoadFromFile(orig).Name);  // no rename
        Assert.Equal(orig, AppSettings.LastProjectPath);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task SaveAs_CleanProject_IsExecutable_WhilePlainSaveIsNot()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, Path.Combine(_tempDir, "copy.dialogproject"));

        // Freshly loaded project: not modified.
        Assert.False(vm.IsModified);
        Assert.False(vm.SaveProjectCommand.CanExecute(null));
        Assert.True(vm.SaveProjectAsCommand.CanExecute(null),
            "Save As must not require IsModified — forking a clean project is legitimate.");
    }

    [Fact]
    public void SaveAs_NoProjectOpen_IsNotExecutable()
    {
        var vm = MakeVm();
        Assert.False(vm.SaveProjectAsCommand.CanExecute(null));
    }

    // ── _vo/ sidecar copy ─────────────────────────────────────────────────

    [Fact]
    public async Task SaveAs_DifferentDirectory_CopiesVoFolderRecursively()
    {
        var orig = WriteProject("orig.dialogproject");
        var voFile = Path.Combine(_tempDir, "_vo", "speaker", "conv_12.wem");
        Directory.CreateDirectory(Path.GetDirectoryName(voFile)!);
        File.WriteAllBytes(voFile, [1, 2, 3]);

        var target = Path.Combine(_tempDir, "fork", "forked.dialogproject");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var vm = await OpenVm(orig, target);

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        var copied = Path.Combine(_tempDir, "fork", "_vo", "speaker", "conv_12.wem");
        Assert.True(File.Exists(copied), "_vo/ must be copied next to the new project file.");
        Assert.True(File.Exists(voFile), "the original _vo/ must be left in place.");
    }

    [Fact]
    public async Task SaveAs_NoVoFolder_CopiesNothing()
    {
        var orig   = WriteProject("orig.dialogproject");
        var target = Path.Combine(_tempDir, "fork", "forked.dialogproject");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var vm = await OpenVm(orig, target);

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(Path.Combine(_tempDir, "fork", "_vo")));
    }

    [Fact]
    public async Task SaveAs_VoCopyFailure_ProjectIsStillSavedAndRebound()
    {
        var orig = WriteProject("orig.dialogproject");
        var voFile = Path.Combine(_tempDir, "_vo", "a.wem");
        Directory.CreateDirectory(Path.GetDirectoryName(voFile)!);
        File.WriteAllBytes(voFile, [1]);

        var forkDir = Path.Combine(_tempDir, "fork");
        Directory.CreateDirectory(forkDir);
        // A FILE named "_vo" blocks Directory.CreateDirectory → deterministic copy failure.
        File.WriteAllText(Path.Combine(forkDir, "_vo"), "blocker");

        var target = Path.Combine(forkDir, "forked.dialogproject");
        var vm = await OpenVm(orig, target);

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target), "the save itself must not be rolled back.");
        Assert.Equal(target, AppSettings.LastProjectPath);
        Assert.False(vm.IsModified);
    }

    // ── ReportSaveError delegate (save-error visibility spec) ─────────────

    /// Returns a Save As target whose parent "directory" is actually a file,
    /// so DialogProjectSerializer.SaveToFile deterministically throws.
    private string BlockedSaveTarget()
    {
        var blocker = Path.Combine(_tempDir, "blocked");
        File.WriteAllText(blocker, "not a directory");
        return Path.Combine(blocker, "forked.dialogproject");
    }

    [Fact]
    public async Task SaveAs_WriteFailure_InvokesReportSaveError_AndStaysBoundToOriginal()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, BlockedSaveTarget());
        Exception? reported = null;
        vm.ReportSaveError = ex => reported = ex;

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.NotNull(reported);
        Assert.Equal(orig, AppSettings.LastProjectPath);   // rebind must not have happened
        Assert.Equal("orig", vm.CurrentProjectName);
    }

    [Fact]
    public async Task SaveAs_WriteFailure_NullDelegate_DoesNotThrow()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, BlockedSaveTarget());
        // ReportSaveError deliberately left null.

        await vm.SaveProjectAsCommand.ExecuteAsync(null);   // must not throw

        Assert.Equal(orig, AppSettings.LastProjectPath);
    }

    [Fact]
    public async Task SaveAs_Success_DoesNotInvokeReportSaveError()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, Path.Combine(_tempDir, "forked.dialogproject"));
        Exception? reported = null;
        vm.ReportSaveError = ex => reported = ex;

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.Null(reported);
    }

    [Fact]
    public async Task SaveAs_VoCopyFailure_InvokesReportSaveError_AndStillSaves()
    {
        var orig = WriteProject("orig.dialogproject");
        var voFile = Path.Combine(_tempDir, "_vo", "a.wem");
        Directory.CreateDirectory(Path.GetDirectoryName(voFile)!);
        File.WriteAllBytes(voFile, [1]);

        var forkDir = Path.Combine(_tempDir, "fork");
        Directory.CreateDirectory(forkDir);
        File.WriteAllText(Path.Combine(forkDir, "_vo"), "blocker");   // file blocks dir creation

        var target = Path.Combine(forkDir, "forked.dialogproject");
        var vm = await OpenVm(orig, target);
        Exception? reported = null;
        vm.ReportSaveError = ex => reported = ex;

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.NotNull(reported);                          // partial failure surfaced
        Assert.True(File.Exists(target), "the save itself must not be rolled back.");
    }

    [Fact]
    public async Task PlainSave_WriteFailure_InvokesReportSaveError()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, saveAsTarget: null);
        Exception? reported = null;
        vm.ReportSaveError = ex => reported = ex;

        // Make the open project file read-only so SaveToFile throws.
        File.SetAttributes(orig, FileAttributes.ReadOnly);
        try
        {
            vm.IsModified = true;
            vm.SaveProjectCommand.Execute(null);
            Assert.NotNull(reported);
        }
        finally
        {
            File.SetAttributes(orig, FileAttributes.Normal);   // or Dispose can't delete _tempDir
        }
    }
}
