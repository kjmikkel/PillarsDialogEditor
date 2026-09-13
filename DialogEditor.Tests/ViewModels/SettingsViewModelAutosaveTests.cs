using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// The autosave interval / generations pickers (issue #11). Both apply live — the
/// scheduler seam is what lets the running DispatcherTimer be reconfigured without
/// a restart, mirroring IFontScaleApplier.
public class SettingsViewModelAutosaveTests : IDisposable
{
    private sealed class RecordingScheduler : IAutosaveScheduler
    {
        public List<int> Applied { get; } = [];
        public void Apply(int intervalSeconds) => Applied.Add(intervalSeconds);
    }

    public SettingsViewModelAutosaveTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
    }

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null && File.Exists(path)) File.Delete(path);
    }

    private static SettingsViewModel Vm(IAutosaveScheduler? scheduler = null)
        => new("/game", new StubFolderPicker(), autosaveScheduler: scheduler);

    [Fact]
    public void IntervalOptions_OfferOffAndThePresets()
        => Assert.Equal([0, 30, 60, 120, 300, 600], Vm().AutosaveIntervalOptions);

    [Fact]
    public void GenerationOptions_OfferOneThroughFive()
        => Assert.Equal([1, 2, 3, 4, 5], Vm().AutosaveGenerationOptions);

    [Fact]
    public void SelectedInterval_StartsFromPersistedSetting()
    {
        AppSettings.AutosaveIntervalSeconds = 300;
        Assert.Equal(300, Vm().SelectedAutosaveInterval);
    }

    [Fact]
    public void SelectedGenerations_StartsFromPersistedSetting()
    {
        AppSettings.AutosaveGenerations = 5;
        Assert.Equal(5, Vm().SelectedAutosaveGenerations);
    }

    [Fact]
    public void ChangingInterval_PersistsAndRescheduesImmediately()
    {
        var scheduler = new RecordingScheduler();
        Vm(scheduler).SelectedAutosaveInterval = 120;
        Assert.Equal(120, AppSettings.AutosaveIntervalSeconds);
        Assert.Equal([120], scheduler.Applied);
    }

    [Fact]
    public void ChoosingOff_PersistsZeroAndSchedulesZero()
    {
        var scheduler = new RecordingScheduler();
        Vm(scheduler).SelectedAutosaveInterval = 0;
        Assert.Equal(0, AppSettings.AutosaveIntervalSeconds);
        Assert.Equal([0], scheduler.Applied);
    }

    [Fact]
    public void ChangingGenerations_Persists_ButDoesNotTouchTheTimer()
    {
        var scheduler = new RecordingScheduler();
        Vm(scheduler).SelectedAutosaveGenerations = 4;
        Assert.Equal(4, AppSettings.AutosaveGenerations);
        Assert.Empty(scheduler.Applied);   // generations change the write, not the cadence
    }

    [Fact]
    public void NoSchedulerInjected_ChangingIntervalStillPersists()
    {
        Vm().SelectedAutosaveInterval = 600;   // null-object applier: no throw
        Assert.Equal(600, AppSettings.AutosaveIntervalSeconds);
    }
}
