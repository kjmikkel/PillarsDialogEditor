using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class ThemePickerViewModelTests : IDisposable
{
    public ThemePickerViewModelTests()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        Loc.Configure(new StubStringProvider());
    }

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    // Captures Apply() calls and offers a fixed catalogue, so the VM can be tested
    // without the Avalonia layer (mirrors the StubFolderPicker pattern).
    private sealed class StubThemeApplier : IThemeApplier
    {
        public IReadOnlyList<ThemeOption> Available { get; } =
        [
            new("Dark",  "Theme_Name_Dark"),
            new("Light", "Theme_Name_Light"),
        ];

        public List<string> Applied { get; } = [];
        public void Apply(string id) => Applied.Add(id);
    }

    [Fact]
    public void AvailableThemes_AreSourcedFromApplier()
    {
        var vm = new ThemePickerViewModel(new StubThemeApplier());
        Assert.Equal(["Dark", "Light"], vm.AvailableThemes.Select(c => c.Id));
    }

    [Fact]
    public void SelectedTheme_InitialisedFromAppSettings()
    {
        AppSettings.Theme = "Light";
        var vm = new ThemePickerViewModel(new StubThemeApplier());
        Assert.Equal("Light", vm.SelectedTheme.Id);
    }

    [Fact]
    public void SelectedTheme_WhenAppSettingsUnknown_FallsBackToFirst()
    {
        AppSettings.Theme = "NotARealPalette";
        var vm = new ThemePickerViewModel(new StubThemeApplier());
        Assert.Equal("Dark", vm.SelectedTheme.Id);
    }

    [Fact]
    public void ChangingSelectedTheme_PersistsAndApplies()
    {
        var applier = new StubThemeApplier();
        var vm      = new ThemePickerViewModel(applier);

        vm.SelectedTheme = vm.AvailableThemes.Single(c => c.Id == "Light");

        Assert.Equal("Light", AppSettings.Theme);
        Assert.Equal(["Light"], applier.Applied);
    }
}
