using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 183: File → Patch Manager opened from the editor starts with the editor's game
/// folder, instead of "(no game folder selected)".
public class MainWindowViewModelPatchManagerTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en");
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_pm_{Guid.NewGuid():N}.json");

    public MainWindowViewModelPatchManagerTests()
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
    public async Task CreatePatchManagerViewModel_StartsWithTheEditorsGameFolder()
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(_game.Root), new StubFilePicker());
        await vm.OpenFolderCommand.ExecuteAsync(null);

        var pm = vm.CreatePatchManagerViewModel(new StubFolderPicker(), new StubFilePicker());

        Assert.Equal(_game.Root, pm.GameFolder);
        Assert.Equal(PatchManagerHost.Editor, pm.Host);
    }

    [Fact]
    public void CreatePatchManagerViewModel_NoGameFolderOpen_StartsEmpty()
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

        var pm = vm.CreatePatchManagerViewModel(new StubFolderPicker(), new StubFilePicker());

        Assert.Equal(string.Empty, pm.GameFolder);
    }
}
