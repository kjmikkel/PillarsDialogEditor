using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// A guided tour per mode (issue 234): each mode has its own steps and its own seen flag, and
/// each starts by itself the first time that mode is shown.
public sealed class GuidedTourModeTests : IDisposable
{
    private readonly string _settingsPath;

    public GuidedTourModeTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"tourmode_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;   // no file: both tours unseen
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel NewVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    [Fact]
    public void Start_Reading_RunsTheReaderSteps_AndMarksItSeen()
    {
        var vm = new GuidedTourViewModel();
        Assert.False(GuidedTourViewModel.HasSeen(EditorMode.Reading));

        vm.Start(EditorMode.Reading);

        Assert.True(vm.IsVisible);
        Assert.Equal("BrowserPanel", vm.CurrentStep.TargetName);
        Assert.Equal(4, GuidedTourViewModel.ReaderSteps.Count);
        Assert.Equal("ModeBadge", GuidedTourViewModel.ReaderSteps[^1].TargetName);
        Assert.Equal(["Tour_Reader_Step1_Text", "Tour_Reader_Step2_Text", "Tour_Reader_Step3_Text", "Tour_Reader_Step4_Text"],
                     GuidedTourViewModel.ReaderSteps.Select(s => s.DescriptionKey));
        Assert.True(AppSettings.ReaderTourSeen);
        Assert.False(AppSettings.GuidedTourSeen);
        Assert.True(GuidedTourViewModel.HasSeen(EditorMode.Reading));
    }

    [Fact]
    public void Start_Editing_RunsTheEditingSteps_AndMarksItSeen()
    {
        var vm = new GuidedTourViewModel();

        vm.Start(EditorMode.Editing);

        Assert.True(vm.IsVisible);
        Assert.Equal("HelpToggle", GuidedTourViewModel.DefaultSteps[^1].TargetName);
        Assert.True(AppSettings.GuidedTourSeen);
        Assert.False(AppSettings.ReaderTourSeen);
    }

    [Fact]
    public void Start_Reading_ThenEditing_SwapsTheStepList()
    {
        var vm = new GuidedTourViewModel();
        vm.Start(EditorMode.Reading);
        vm.NextCommand.Execute(null); vm.NextCommand.Execute(null); vm.NextCommand.Execute(null);
        Assert.Equal("ModeBadge", vm.CurrentStep.TargetName);

        vm.Start(EditorMode.Editing);

        Assert.Equal(0, vm.CurrentIndex);
        Assert.Equal("BrowserPanel", vm.CurrentStep.TargetName);
        vm.NextCommand.Execute(null); vm.NextCommand.Execute(null); vm.NextCommand.Execute(null);
        Assert.Equal("HelpToggle", vm.CurrentStep.TargetName);
    }

    [Fact]
    public void StartGuidedTourCommand_StartsTheCurrentModesTour()
    {
        var vm = NewVm();
        vm.Mode = EditorMode.Reading;

        vm.StartGuidedTourCommand.Execute(null);

        Assert.True(vm.Tour.IsVisible);
        Assert.True(AppSettings.ReaderTourSeen);
        Assert.False(AppSettings.GuidedTourSeen);
        for (var i = 0; i < 3; i++) vm.Tour.NextCommand.Execute(null);
        Assert.Equal("ModeBadge", vm.Tour.CurrentStep.TargetName);
    }

    [Fact]
    public async Task FirstSwitchToAMode_StartsItsTour_Once()
    {
        var vm = NewVm();
        vm.Mode = EditorMode.Reading;

        Assert.True(await vm.SwitchModeAsync(EditorMode.Editing));
        Assert.True(vm.Tour.IsVisible);
        Assert.True(AppSettings.GuidedTourSeen);
        Assert.False(AppSettings.ReaderTourSeen);

        vm.Tour.DismissCommand.Execute(null);
        Assert.True(await vm.SwitchModeAsync(EditorMode.Reading));    // first time reading
        Assert.True(vm.Tour.IsVisible);
        Assert.True(AppSettings.ReaderTourSeen);

        vm.Tour.DismissCommand.Execute(null);
        Assert.True(await vm.SwitchModeAsync(EditorMode.Editing));
        Assert.False(vm.Tour.IsVisible);
        Assert.True(await vm.SwitchModeAsync(EditorMode.Reading));
        Assert.False(vm.Tour.IsVisible);
    }

    [Fact]
    public async Task Switching_DismissesARunningTour()
    {
        var vm = NewVm();
        AppSettings.GuidedTourSeen = true;
        AppSettings.ReaderTourSeen = true;
        vm.Tour.Start(EditorMode.Editing);
        Assert.True(vm.Tour.IsVisible);

        Assert.True(await vm.SwitchModeAsync(EditorMode.Reading));

        Assert.False(vm.Tour.IsVisible);
    }
}
