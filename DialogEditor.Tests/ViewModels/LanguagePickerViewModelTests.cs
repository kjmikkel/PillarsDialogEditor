using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class LanguagePickerViewModelTests : IDisposable
{
    public LanguagePickerViewModelTests()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        Loc.Configure(new StubStringProvider());
    }

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) try { File.Delete(path); } catch { /* best-effort */ }
    }

    private sealed class StubLanguageApplier : ILanguageApplier
    {
        public IReadOnlyList<LanguageOption> Available { get; } =
        [
            new("en", "Settings_Language_English"),
            new("de", "Settings_Language_German"),
        ];
        public List<string> Applied { get; } = [];
        public void Apply(string id) => Applied.Add(id);
    }

    [Fact]
    public void AvailableLanguages_AreSourcedFromApplier()
    {
        var vm = new LanguagePickerViewModel(new StubLanguageApplier());
        Assert.Equal(["en", "de"], vm.AvailableLanguages.Select(c => c.Id));
    }

    [Fact]
    public void SelectedLanguage_InitialisedFromAppSettings()
    {
        AppSettings.UiLanguage = "de";
        var vm = new LanguagePickerViewModel(new StubLanguageApplier());
        Assert.Equal("de", vm.SelectedLanguage.Id);
    }

    [Fact]
    public void SelectedLanguage_WhenAppSettingsUnknown_FallsBackToFirst()
    {
        AppSettings.UiLanguage = "xx-UNKNOWN";
        var vm = new LanguagePickerViewModel(new StubLanguageApplier());
        Assert.Equal("en", vm.SelectedLanguage.Id);
    }

    [Fact]
    public void ChangingSelectedLanguage_PersistsAndApplies()
    {
        var applier = new StubLanguageApplier();
        var vm      = new LanguagePickerViewModel(applier);

        vm.SelectedLanguage = vm.AvailableLanguages.Single(c => c.Id == "de");

        Assert.Equal("de", AppSettings.UiLanguage);
        Assert.Equal(["de"], applier.Applied);
    }
}
