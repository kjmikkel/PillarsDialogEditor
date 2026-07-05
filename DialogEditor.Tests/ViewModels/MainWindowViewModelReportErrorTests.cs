using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// ReportError must fire for representative non-save failures (project open,
/// conversation import); the remaining Error-level sites are covered
/// structurally by ErrorReportingCoverageTests.
public class MainWindowViewModelReportErrorTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _tempDir;

    public MainWindowViewModelReportErrorTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_reporterr_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"reporterr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm(string? openResult = null) =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker(openResult: openResult));

    private static Task InvokeLoadProjectAsync(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("LoadProjectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;
    }

    private static void InjectProvider(MainWindowViewModel vm, IGameDataProvider provider)
    {
        var fi = typeof(MainWindowViewModel).GetField("_provider",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, provider);
    }

    private static void InjectProject(MainWindowViewModel vm, DialogProject project)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("SetProject",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        mi.Invoke(vm, [project]);
    }

    [Fact]
    public async Task OpenCorruptProject_InvokesReportError()
    {
        var path = Path.Combine(_tempDir, "corrupt.dialogproject");
        File.WriteAllText(path, "{ this is not valid json");
        var vm = MakeVm();
        Exception? reported = null;
        vm.ReportError = ex => reported = ex;

        await InvokeLoadProjectAsync(vm, path);

        Assert.NotNull(reported);
    }

    [Fact]
    public async Task OpenValidProject_DoesNotInvokeReportError()
    {
        var path = Path.Combine(_tempDir, "ok.dialogproject");
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty("ok"));
        var vm = MakeVm();
        Exception? reported = null;
        vm.ReportError = ex => reported = ex;

        await InvokeLoadProjectAsync(vm, path);

        Assert.Null(reported);
    }

    [Fact]
    public async Task ImportConversation_MissingFile_InvokesReportError()
    {
        // Picker returns a path that doesn't exist — the import's file read throws.
        var vm = MakeVm(openResult: Path.Combine(_tempDir, "does-not-exist.yarn"));
        var file = new ConversationFile("stub_conv", "", "", "");
        InjectProvider(vm, new StubProvider(file, new ConversationEditSnapshot([])));
        InjectProject(vm, DialogProject.Empty("p"));
        Exception? reported = null;
        vm.ReportError = ex => reported = ex;

        await vm.ImportConversationCommand.ExecuteAsync(null);

        Assert.NotNull(reported);
    }
}
