using System.Reflection;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// GitHub issue 9: Node Details' "Add to project dictionary" writes into the open project.
public class MainWindowViewModelProjectWordsTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowViewModelProjectWordsTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_words_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void SetProject(MainWindowViewModel vm, DialogProject? project) =>
        typeof(MainWindowViewModel).GetMethod("SetProject", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, [project]);

    [Fact]
    public void NoProject_DetailCannotAddProjectWords()
    {
        var vm = MakeVm();
        Assert.Null(vm.Detail.AddWordToProjectDictionary);
        Assert.Null(vm.ProjectWords);
    }

    [Fact]
    public void ProjectOpen_AddingAWord_SavesItInTheProject_AndMarksItModified()
    {
        var vm = MakeVm();
        SetProject(vm, DialogProject.Empty("T"));
        Assert.False(vm.IsModified);

        vm.Detail.AddWordToProjectDictionary!.Invoke("Xoti");

        Assert.Equal(["Xoti"], vm.ProjectWords);
        Assert.True(vm.IsModified);
    }

    [Fact]
    public void ProjectOpen_ExposesItsWords()
    {
        var vm = MakeVm();
        SetProject(vm, DialogProject.Empty("T").WithProjectWord("Ondra"));
        Assert.Equal(["Ondra"], vm.ProjectWords);
    }

    [Fact]
    public void AddingAWordAlreadyThere_DoesNotMarkModified()
    {
        var vm = MakeVm();
        SetProject(vm, DialogProject.Empty("T").WithProjectWord("Xoti"));
        vm.Detail.AddWordToProjectDictionary!.Invoke("Xoti");
        Assert.False(vm.IsModified);
    }

    [Fact]
    public void ProjectClosed_DetailCannotAddProjectWords_AndTheWordsGo()
    {
        var vm = MakeVm();
        SetProject(vm, DialogProject.Empty("T").WithProjectWord("Xoti"));
        SetProject(vm, null);
        Assert.Null(vm.Detail.AddWordToProjectDictionary);
        Assert.Null(vm.ProjectWords);
    }
}
