using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Help > Text Tag Reference: the command hands the UI layer a viewmodel
/// primed with the active game (PoE2 default when no game folder is open).
/// Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md
public class MainWindowViewModelTagReferenceTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowViewModelTagReferenceTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_tagref_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    [Fact]
    public void TagReferenceCommand_ShowsViewModel_DefaultingToPoE2()
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        TagReferenceViewModel? shown = null;
        vm.ShowTagReference = t => shown = t;

        vm.TagReferenceCommand.Execute(null);

        Assert.NotNull(shown);
        Assert.Equal(TagGameFilter.PoE2, shown!.SelectedGame.Value);   // no game folder open
    }
}
