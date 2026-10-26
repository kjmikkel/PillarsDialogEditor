using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// GitHub issue 263: the Patcher has its own text size, chosen in its Settings window, saved
/// apart from the editor's font scale and applied at once.
public class TextSizePickerViewModelTests : IDisposable
{
    public TextSizePickerViewModelTests()
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

    private sealed class RecordingApplier : IFontScaleApplier
    {
        public List<double> Applied { get; } = [];
        public void Apply(double scale) => Applied.Add(scale);
    }

    [Fact]
    public void PatcherFontScale_DefaultsTo100Percent()
        => Assert.Equal(1.0, AppSettings.PatcherFontScale);

    [Fact]
    public void Options_AreTheEditorsSteps_AndStartAtTheSavedOne()
    {
        AppSettings.PatcherFontScale = 1.5;
        var vm = new TextSizePickerViewModel(new RecordingApplier());

        Assert.Equal([1.0, 1.25, 1.5, 1.75, 2.0], vm.Options.Select(o => o.Scale));
        Assert.Equal(1.5, vm.Selected.Scale);
    }

    [Fact]
    public void Picking_SavesThePatchersOwnSetting_AndAppliesIt()
    {
        var applier = new RecordingApplier();
        var vm = new TextSizePickerViewModel(applier);

        vm.Selected = vm.Options.Single(o => o.Scale == 1.75);

        Assert.Equal(1.75, AppSettings.PatcherFontScale);
        Assert.Equal(1.0, AppSettings.FontScale);   // the editor's own setting is untouched
        Assert.Equal([1.75], applier.Applied);
    }

    [Fact]
    public void AnUnknownSavedValue_FallsBackTo100Percent()
    {
        AppSettings.PatcherFontScale = 3.3;
        Assert.Equal(1.0, new TextSizePickerViewModel(new RecordingApplier()).Selected.Scale);
    }
}
