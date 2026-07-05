using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// File > Close Project: dirty-guarded teardown to projectless browse mode.
/// The canvas is cleared (patched content exists nowhere after close) and
/// AppSettings.LastProjectPath is cleared (a deliberate close sticks across
/// restarts). The branch-switch teardown keeps both — guarded here too.
/// Spec: docs/superpowers/specs/2026-07-05-close-project-design.md
public class MainWindowViewModelCloseProjectTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _tempDir;

    public MainWindowViewModelCloseProjectTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_close_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"close_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static Task InvokeLoadProjectAsync(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("LoadProjectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;
    }

    /// Writes an empty project named after the file into _tempDir; returns its path.
    private string WriteProject(string relativePath)
    {
        var path = Path.Combine(_tempDir, relativePath);
        DialogProjectSerializer.SaveToFile(
            path, DialogProject.Empty(Path.GetFileNameWithoutExtension(path)));
        return path;
    }

    private static NodeViewModel MakeNode(int id) =>
        new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);

    private async Task<MainWindowViewModel> OpenVmWithCanvasContent(string projectPath)
    {
        var vm = MakeVm();
        await InvokeLoadProjectAsync(vm, projectPath);
        vm.Canvas.AddNode(MakeNode(1), new LayoutPoint(0, 0));
        vm.CurrentConversationName = "some_conv";
        vm.IsModified = false;   // AddNode dirtied the canvas; start the test clean
        return vm;
    }

    // ── Close behaviour ───────────────────────────────────────────────────

    [Fact]
    public async Task Close_ClearsProjectStateCanvasAndLastPath()
    {
        var path = WriteProject("p.dialogproject");
        var vm   = await OpenVmWithCanvasContent(path);

        vm.CloseProjectCommand.Execute(null);

        Assert.False(vm.IsProjectOpen);
        Assert.Null(vm.ProjectPath);
        Assert.Null(vm.CurrentProjectName);
        Assert.Null(vm.CurrentConversationName);
        Assert.Empty(vm.Canvas.Nodes);
        Assert.False(vm.IsModified);
        Assert.Null(AppSettings.LastProjectPath);
    }

    [Fact]
    public async Task Close_DirtyCanvas_DefersUntilProceed()
    {
        var path = WriteProject("p.dialogproject");
        var vm   = await OpenVmWithCanvasContent(path);
        vm.IsModified = true;
        var prompted = false;
        vm.UnsavedChangesRequested += () => prompted = true;

        vm.CloseProjectCommand.Execute(null);

        Assert.True(prompted);
        Assert.True(vm.IsProjectOpen);                    // not closed yet

        vm.DiscardAndProceed();

        Assert.False(vm.IsProjectOpen);
        Assert.Null(AppSettings.LastProjectPath);
    }

    [Fact]
    public async Task Close_Cancelled_KeepsEverything()
    {
        var path = WriteProject("p.dialogproject");
        var vm   = await OpenVmWithCanvasContent(path);
        vm.IsModified = true;

        vm.CloseProjectCommand.Execute(null);
        vm.CancelPendingNavigation();

        Assert.True(vm.IsProjectOpen);
        Assert.Equal(path, AppSettings.LastProjectPath);
        Assert.NotEmpty(vm.Canvas.Nodes);
    }

    [Fact]
    public async Task CanExecute_TracksProjectOpenState()
    {
        var vm = MakeVm();
        Assert.False(vm.CloseProjectCommand.CanExecute(null));

        var path = WriteProject("p.dialogproject");
        await InvokeLoadProjectAsync(vm, path);
        Assert.True(vm.CloseProjectCommand.CanExecute(null));

        vm.CloseProjectCommand.Execute(null);
        Assert.False(vm.CloseProjectCommand.CanExecute(null));
    }

    // ── Branch-switch teardown keeps its distinct semantics ──────────────

    [Fact]
    public async Task ReloadFromDisk_VanishedFile_KeepsLastPathAndCanvas()
    {
        var path = WriteProject("p.dialogproject");
        var vm   = await OpenVmWithCanvasContent(path);

        File.Delete(path);
        vm.ReloadCurrentProjectFromDisk();

        Assert.False(vm.IsProjectOpen);
        Assert.Equal(path, AppSettings.LastProjectPath);   // may reappear on switch back
        Assert.NotEmpty(vm.Canvas.Nodes);                  // canvas deliberately untouched
    }
}
