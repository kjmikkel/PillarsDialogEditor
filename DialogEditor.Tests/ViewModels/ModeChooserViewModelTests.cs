using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

/// <summary>Issue 234: the first-launch mode chooser saves the picked mode (Reading when the
/// window is closed without a pick) and asks its window to close.</summary>
public class ModeChooserViewModelTests : IDisposable
{
    public ModeChooserViewModelTests()
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

    [Fact]
    public void ChooseReading_SavesReading_AndCloses()
    {
        var vm = new ModeChooserViewModel();
        var closes = 0;
        vm.CloseRequested += () => closes++;

        vm.ChooseReadingCommand.Execute(null);

        Assert.Equal(EditorMode.Reading, vm.Result);
        Assert.Equal(EditorMode.Reading, AppSettings.Mode);
        Assert.Equal(1, closes);
    }

    [Fact]
    public void ChooseEditing_SavesEditing_AndCloses()
    {
        var vm = new ModeChooserViewModel();
        var closes = 0;
        vm.CloseRequested += () => closes++;

        vm.ChooseEditingCommand.Execute(null);

        Assert.Equal(EditorMode.Editing, vm.Result);
        Assert.Equal(EditorMode.Editing, AppSettings.Mode);
        Assert.Equal(1, closes);
    }

    [Fact]
    public void ClosingWithoutAChoice_SavesReading()
    {
        var vm = new ModeChooserViewModel();

        vm.OnClosedWithoutChoice();

        Assert.Equal(EditorMode.Reading, vm.Result);
        Assert.Equal(EditorMode.Reading, AppSettings.Mode);
    }

    [Fact]
    public void ClosingAfterAChoice_KeepsTheChoice()
    {
        var vm = new ModeChooserViewModel();
        vm.ChooseEditingCommand.Execute(null);

        vm.OnClosedWithoutChoice();

        Assert.Equal(EditorMode.Editing, AppSettings.Mode);
    }
}
