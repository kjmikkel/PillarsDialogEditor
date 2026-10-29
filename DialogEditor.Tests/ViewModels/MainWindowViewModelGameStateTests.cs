using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 345: opening a game folder hands the Game State panel each global's start-of-game
/// value, so "Fill start-of-game values" has something to fill from.
public class MainWindowViewModelGameStateTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en");
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_gs_{Guid.NewGuid():N}.json");

    public MainWindowViewModelGameStateTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_settingsPath); } catch { /* best-effort */ }
        _game.Dispose();
    }

    [Fact]
    public async Task OpenFolder_GivesTheGameStatePanelTheGlobalsStartValues()
    {
        var dir = Path.Combine(_game.DataRoot, "exported", "design", "globalvariables");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "game.globalvariablesbundle"),
            "{\"GlobalVariablesSets\":[{\"GlobalVariables\":[" +
            "{\"ID\":\"00000000-0000-0000-0000-000000000000\",\"Tag\":\"n_Start\",\"InitialValue\":4}]}]}");
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(_game.Root), new StubFilePicker());
        await vm.OpenFolderCommand.ExecuteAsync(null);

        vm.GameState.Load([[new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)", ["n_Start", "EqualTo", "4"], false, "And")]], "poe2");
        vm.GameState.FillStartOfGameCommand.Execute(null);

        Assert.Equal(4, vm.GameState.State.Globals["n_Start"]);
    }
}
