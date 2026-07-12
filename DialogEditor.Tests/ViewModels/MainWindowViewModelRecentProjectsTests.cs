using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelRecentProjectsTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _projectPath;

    public MainWindowViewModelRecentProjectsTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_rp_{Guid.NewGuid():N}.json");
        _projectPath  = Path.Combine(Path.GetTempPath(), $"proj_{Guid.NewGuid():N}.dialogproject");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_settingsPath); } catch { /* best-effort */ }
        try { File.Delete(_projectPath);  } catch { /* best-effort */ }
    }

    private static MainWindowViewModel NewProject(string savePath) =>
        new(new StubDispatcher(), new StubFolderPicker(),
            new StubFilePicker(saveResult: savePath));

    [Fact]
    public void NewProject_RecordsPathInRecents()
    {
        var vm = NewProject(_projectPath);
        vm.NewProjectCommand.Execute(null);
        Assert.Contains(Path.GetFullPath(_projectPath), vm.RecentProjects);
    }

    [Fact]
    public void CloseProject_DoesNotRemoveFromRecents()
    {
        var vm = NewProject(_projectPath);
        vm.NewProjectCommand.Execute(null);
        Assert.Contains(Path.GetFullPath(_projectPath), vm.RecentProjects);

        vm.CloseProjectCommand.Execute(null);

        Assert.Null(AppSettings.LastProjectPath);                            // close cleared auto-reopen
        Assert.Contains(Path.GetFullPath(_projectPath), vm.RecentProjects);  // history kept
    }
}
