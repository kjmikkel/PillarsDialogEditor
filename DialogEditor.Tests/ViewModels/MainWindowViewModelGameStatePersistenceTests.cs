using DialogEditor.Core.Conditions;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// GitHub issue 344: the Game State panel's assumed state is saved with the project and
/// restored when the project is opened; changing it marks the project modified.
public class MainWindowViewModelGameStatePersistenceTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly string _settingsPath;

    public MainWindowViewModelGameStatePersistenceTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_gamestate_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort cleanup */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static Task Load(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("LoadProjectAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;
    }

    private string Write(DialogProject p)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gs_{Guid.NewGuid():N}.dialogproject");
        _tempFiles.Add(path);
        DialogProjectSerializer.SaveToFile(path, p);
        return path;
    }

    private static DialogProject WithGlobal(string name, int value)
    {
        var s = new GameState();
        s.Globals[name] = value;
        return DialogProject.Empty("GS") with { GameState = GameStateSnapshot.From(s) };
    }

    [Fact]
    public async Task OpeningAProject_RestoresItsSavedState_WithoutMarkingItModified()
    {
        var vm = MakeVm();
        await Load(vm, Write(WithGlobal("n_test", 7)));

        Assert.Equal(7, vm.GameState.State.Globals["n_test"]);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task OpeningAProjectWithNoSavedState_ClearsTheStateLeftFromBefore()
    {
        var vm = MakeVm();
        await Load(vm, Write(WithGlobal("n_test", 7)));
        await Load(vm, Write(DialogProject.Empty("Plain")));

        Assert.False(vm.GameState.State.HasAnyFact);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task ChangingTheState_MarksTheProjectModified_AndSaveWritesIt()
    {
        var vm = MakeVm();
        var path = Write(DialogProject.Empty("GS"));
        await Load(vm, path);

        vm.GameState.NoCompanionsInParty = true;
        Assert.True(vm.IsModified);

        vm.SaveProjectCommand.Execute(null);
        Assert.False(vm.IsModified);

        var restored = new GameState();
        GameStateSnapshot.ApplyTo(DialogProjectSerializer.LoadFromFile(path).GameState, restored);
        Assert.True(restored.NoCompanionsInParty);
    }

    [Fact]
    public async Task ANotificationThatChangesNothing_DoesNotMarkModified()
    {
        var vm = MakeVm();
        await Load(vm, Write(DialogProject.Empty("GS")));

        vm.GameState.ClearAllCommand.Execute(null);   // raises Changed on an already-empty state

        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task ReadingMode_ChangingTheState_DoesNotMarkModified()
    {
        // Reading mode never writes the project (Save needs Editing), so a dirty flag would
        // only raise an unsaved-changes prompt the reader can't act on.
        var vm = MakeVm();
        await Load(vm, Write(DialogProject.Empty("GS")));
        await vm.SwitchModeAsync(EditorMode.Reading);

        vm.GameState.NoCompanionsInParty = true;

        Assert.False(vm.IsModified);
    }

    [Fact]
    public void NoProjectOpen_ChangingTheState_DoesNotMarkModified()
    {
        var vm = MakeVm();
        vm.GameState.NoCompanionsInParty = true;
        Assert.False(vm.IsModified);
    }
}
