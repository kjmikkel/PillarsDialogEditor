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

    [Fact]
    public void OpenRecent_MissingFile_ConfirmYes_RemovesEntryAndAsks()
    {
        var vm = NewProject(_projectPath);
        var ghost = @"Z:\nope\ghost.dialogproject";
        AppSettings.AddRecentProject(ghost);
        var asked = new List<string>();
        vm.ConfirmRemoveMissingProject = p => { asked.Add(p); return Task.FromResult(true); };

        vm.OpenRecentProjectCommand.Execute(ghost);

        Assert.Equal(new[] { Path.GetFullPath(ghost) }, asked);
        Assert.DoesNotContain(Path.GetFullPath(ghost), vm.RecentProjects);
    }

    [Fact]
    public void OpenRecent_MissingFile_ConfirmNo_KeepsEntry()
    {
        var vm = NewProject(_projectPath);
        var ghost = @"Z:\nope\ghost.dialogproject";
        AppSettings.AddRecentProject(ghost);
        vm.ConfirmRemoveMissingProject = _ => Task.FromResult(false);

        vm.OpenRecentProjectCommand.Execute(ghost);

        Assert.Contains(Path.GetFullPath(ghost), vm.RecentProjects);
    }

    [Fact]
    public void OpenRecent_ExistingFile_OpensProject()
    {
        var vm = NewProject(_projectPath);
        vm.NewProjectCommand.Execute(null);   // writes a real .dialogproject at _projectPath
        vm.CloseProjectCommand.Execute(null); // no project open now

        vm.OpenRecentProjectCommand.Execute(Path.GetFullPath(_projectPath));

        Assert.NotNull(vm.CurrentProjectName); // funnel loaded the project
    }

    [Fact]
    public void ClearRecent_EmptiesList()
    {
        var vm = NewProject(_projectPath);
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");

        vm.ClearRecentProjectsCommand.Execute(null);

        Assert.Empty(vm.RecentProjects);
    }
}
