using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// The editor refuses a project saved by a newer version (GitHub issue 62) on every load
/// route — open, autosave recovery, git-conflicted open and merge — without touching the
/// project already open, and without going through the crash-report dialog.
public class MainWindowViewModelSchemaTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_schema_{Guid.NewGuid():N}.json");
    private readonly string _dir          = Directory.CreateTempSubdirectory().FullName;
    private string ProjectPath => Path.Combine(_dir, "p.dialogproject");

    public MainWindowViewModelSchemaTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_dir, true); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm(IReadOnlyList<string>? multiResult = null) =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker(multiResult: multiResult));

    private static Task Load(MainWindowViewModel vm, string path) =>
        (Task)typeof(MainWindowViewModel).GetMethod("LoadProjectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [path, false])!;

    private static void WriteVersion(string path, string name, int version)
    {
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty(name));
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"SchemaVersion\": 1", $"\"SchemaVersion\": {version}"));
    }

    private async Task<MainWindowViewModel> VmWithCurrentProjectOpen()
    {
        var current = Path.Combine(_dir, "current.dialogproject");
        DialogProjectSerializer.SaveToFile(current, DialogProject.Empty("Current"));
        var vm = MakeVm([Path.Combine(_dir, "future.dialogproject")]);
        await Load(vm, current);
        return vm;
    }

    [Fact]
    public async Task Open_NewerProject_IsRefused_WithMessage_NotACrashReport()
    {
        var vm = await VmWithCurrentProjectOpen();
        WriteVersion(ProjectPath, "Future", 99);
        string? shown = null; Exception? reported = null;
        vm.ShowUnsupportedFormat = (_, msg) => { shown = msg; return Task.CompletedTask; };
        vm.ReportError = ex => reported = ex;

        await Load(vm, ProjectPath);

        Assert.Equal("Current", vm.CurrentProjectName);   // previous project untouched
        Assert.StartsWith("Schema_TooNew", vm.StatusText);
        Assert.StartsWith("Schema_TooNew", shown);
        Assert.Null(reported);
    }

    [Fact]
    public async Task Open_NewerAutosaveSidecar_IsKept_AndTheSavedFileLoads()
    {
        DialogProjectSerializer.SaveToFile(ProjectPath, DialogProject.Empty("Saved"));
        var sidecar = AutosaveRecovery.SidecarPath(ProjectPath);
        WriteVersion(sidecar, "Recovered", 99);
        File.SetLastWriteTimeUtc(ProjectPath, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(sidecar,     DateTime.UtcNow.AddMinutes(-1));
        var vm = MakeVm();
        vm.ConfirmRestoreAutosave = _ => Task.FromResult(true);

        await Load(vm, ProjectPath);

        Assert.Equal("Saved", vm.CurrentProjectName);
        Assert.True(File.Exists(sidecar));   // a newer editor's unsaved work, not corruption
    }

    [Fact]
    public async Task Open_OlderPatchInside_IsNotMarkedModified()
    {
        var project = DialogProject.Empty("Old")
            .WithPatch(new ConversationPatch("greeting", 1, [], [7], []));
        DialogProjectSerializer.SaveToFile(ProjectPath, project);
        var vm = MakeVm();

        await Load(vm, ProjectPath);

        Assert.Equal("Old", vm.CurrentProjectName);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task Open_GitConflictWithANewerSide_ShowsTheSchemaMessage()
    {
        File.WriteAllText(ProjectPath,
            "{\n" +
            "  \"Name\": \"P\",\n" +
            "<<<<<<< HEAD\n" +
            "  \"SchemaVersion\": 1,\n" +
            "=======\n" +
            "  \"SchemaVersion\": 99,\n" +
            ">>>>>>> other\n" +
            "  \"Patches\": {}\n" +
            "}\n");
        var vm = MakeVm();

        await Load(vm, ProjectPath);

        Assert.StartsWith("Schema_TooNew", vm.StatusText);
    }

    [Fact]
    public async Task Merge_NewerProject_IsRefused_AndNothingChanges()
    {
        var vm = await VmWithCurrentProjectOpen();
        WriteVersion(Path.Combine(_dir, "future.dialogproject"), "Future", 99);
        var currentPath  = Path.Combine(_dir, "current.dialogproject");
        var bytesBefore  = File.ReadAllBytes(currentPath);
        string? shown = null;
        vm.ShowUnsupportedFormat = (_, msg) => { shown = msg; return Task.CompletedTask; };

        await vm.MergeProjectsCommand.ExecuteAsync(null);

        Assert.Equal("Current", vm.CurrentProjectName);
        Assert.False(vm.IsModified);
        Assert.Equal(bytesBefore, File.ReadAllBytes(currentPath));
        Assert.StartsWith("Schema_TooNew", vm.StatusText);
        Assert.StartsWith("Schema_TooNew", shown);
    }
}
