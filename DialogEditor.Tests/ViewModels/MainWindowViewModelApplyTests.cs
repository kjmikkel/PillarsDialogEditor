using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelApplyTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public MainWindowViewModelApplyTests() => Loc.Configure(new StubStringProvider());

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void Inject(MainWindowViewModel vm, string field, object? value)
    {
        var fi = typeof(MainWindowViewModel).GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, value);
    }

    private static DialogProject? GetProject(MainWindowViewModel vm)
    {
        var fi = typeof(MainWindowViewModel).GetField("_project",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (DialogProject?)fi.GetValue(vm);
    }

    private MainWindowViewModel MakeLoadedVm(out string path)
    {
        var vm = MakeVm();
        path = Path.Combine(Path.GetTempPath(), $"apply_{Guid.NewGuid():N}.dialogproject");
        _tempFiles.Add(path);
        Inject(vm, "_project", DialogProject.Empty("ORIG"));
        Inject(vm, "_projectPath", path);
        return vm;
    }

    [Fact]
    public async Task ApplyFromDiff_Dirty_AbortsWhenSaveDeclined()
    {
        var vm = MakeLoadedVm(out _);
        vm.IsModified = true;
        vm.ConfirmSaveBeforeApply = () => Task.FromResult(false);

        await vm.ApplyFromDiff(DialogProject.Empty("APPLIED"));

        Assert.Equal("ORIG", GetProject(vm)!.Name);   // not applied
    }

    [Fact]
    public async Task ApplyFromDiff_WritesAndClearsDirty()
    {
        var vm = MakeLoadedVm(out var path);

        await vm.ApplyFromDiff(DialogProject.Empty("APPLIED"));

        Assert.False(vm.IsModified);
        Assert.True(File.Exists(path));
        Assert.Equal("APPLIED", GetProject(vm)!.Name);
    }

    [Fact]
    public async Task UndoApply_RestoresPreApplyProject()
    {
        var vm = MakeLoadedVm(out _);
        var before = GetProject(vm)!;

        await vm.ApplyFromDiff(DialogProject.Empty("APPLIED"));
        vm.UndoApplyCommand.Execute(null);

        Assert.Equal(before.Name, GetProject(vm)!.Name);
    }
}
