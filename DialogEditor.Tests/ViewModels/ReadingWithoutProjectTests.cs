using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Speaker Lines and Reputation & Disposition Balance are read-only and need only a game
/// folder, so a Reader without a project can use them (GitHub issue 234).
public class ReadingWithoutProjectTests : IDisposable
{
    private readonly string _settingsPath;

    public ReadingWithoutProjectTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"rwp_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void Inject(MainWindowViewModel vm, string field, object? value) =>
        typeof(MainWindowViewModel).GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, value);

    private static Conversation Conv() =>
        new("vanilla_conv",
            [new ConversationNode(7, false, SpeakerCategory.Npc, "spk", "", [], [], [], "Conversation", "None")],
            new StringTable([new StringEntry(7, "hello there", "")]));

    /// A viewmodel with a game folder loaded and no project.
    private static MainWindowViewModel FolderOnlyVm()
    {
        var vm = MakeVm();
        Inject(vm, "_provider", new FakeGameDataProvider("poe2", "en", Conv()));
        Inject(vm, "_currentGameDirectory", "game-root");
        return vm;
    }

    [Fact]
    public void Balance_CanOpen_WithOnlyAGameFolder() =>
        Assert.True(FolderOnlyVm().ShowRepDispositionBalanceWindowCommand.CanExecute(null));

    [Fact]
    public void SpeakerLines_CanOpen_WithOnlyAGameFolder() =>
        Assert.True(FolderOnlyVm().BrowseSpeakerLinesCommand.CanExecute(null));

    [Fact]
    public void Neither_CanOpen_WithoutAGameFolder()
    {
        var vm = MakeVm();
        Assert.False(vm.ShowRepDispositionBalanceWindowCommand.CanExecute(null));
        Assert.False(vm.BrowseSpeakerLinesCommand.CanExecute(null));
    }

    [Fact]
    public async Task Balance_WithoutAProject_ScansTheGameOnDisk()
    {
        // The summary line reports how many conversations were analysed (see EchoStringProvider).
        Loc.Configure(new EchoStringProvider(new() { ["RepBalance_Summary"] = "analysed {0}" }));
        var vm = FolderOnlyVm();
        RepDispositionBalanceViewModel? shown = null;
        vm.ShowRepDispositionBalance = b => { shown = b; return Task.CompletedTask; };

        await vm.ShowRepDispositionBalanceWindowCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.False(shown!.HasProject);
        Assert.Equal(BalanceSource.OnDiskPlusChanges, shown.Source);
        shown.Scope = BalanceScope.All;
        await shown.RefreshCommand.ExecuteAsync(null);
        Assert.NotEmpty(shown.DispositionRows);
        Assert.Equal("analysed 1", shown.StatusText);
    }

    [Fact]
    public async Task Balance_WithAProject_KeepsTheSourcePicker()
    {
        var vm = FolderOnlyVm();
        Inject(vm, "_project", DialogProject.Empty("p"));
        RepDispositionBalanceViewModel? shown = null;
        vm.ShowRepDispositionBalance = b => { shown = b; return Task.CompletedTask; };

        await vm.ShowRepDispositionBalanceWindowCommand.ExecuteAsync(null);

        Assert.True(shown!.HasProject);
    }

    [Fact]
    public async Task SpeakerLines_WithoutAProject_ListsGameLines()
    {
        var vm = FolderOnlyVm();
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.BrowseSpeakerLinesCommand.ExecuteAsync(null);
        await shown!.ScanAsync();

        Assert.Contains(shown.Rows, r => r.ConversationName == "vanilla_conv" && r.NodeId == 7);
    }

    [Fact]
    public async Task SpeakerLines_HasProject_FollowsWhetherAProjectIsOpen()
    {
        var vm = FolderOnlyVm();
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };
        await vm.BrowseSpeakerLinesCommand.ExecuteAsync(null);
        Assert.False(shown!.HasProject);   // the window hides "Only my lines": no project to compare with

        Inject(vm, "_project", DialogProject.Empty("p"));
        await vm.BrowseSpeakerLinesCommand.ExecuteAsync(null);
        Assert.True(shown.HasProject);
    }

    [Theory]
    [InlineData(true,  "RepBalance_Tip_Scope")]
    [InlineData(false, "RepBalance_Tip_Scope_NoProject")]
    public void Balance_ScopeTip_NamesTheSourceOnlyWhenThePickerShows(bool hasProject, string key)
    {
        var vm = new RepDispositionBalanceViewModel(
            DialogProject.Empty("p"), new FakeGameDataProvider("poe2", "en"),
            () => (null, null), hasProject);
        Assert.Equal(key, vm.ScopeTooltip);
    }

    [Theory]
    [InlineData(true,  "RepBalance_Tip_Refresh")]
    [InlineData(false, "RepBalance_Tip_Refresh_NoProject")]
    public void Balance_AnalyzeTip_NamesTheSourceOnlyWhenThePickerShows(bool hasProject, string key)
    {
        var vm = new RepDispositionBalanceViewModel(
            DialogProject.Empty("p"), new FakeGameDataProvider("poe2", "en"),
            () => (null, null), hasProject);
        Assert.Equal(key, vm.AnalyzeTooltip);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_StillOpen_InReadingMode(bool withProject)
    {
        var vm = FolderOnlyVm();
        if (withProject) Inject(vm, "_project", DialogProject.Empty("p"));

        Assert.True(await vm.SwitchModeAsync(EditorMode.Reading));

        Assert.True(vm.ShowRepDispositionBalanceWindowCommand.CanExecute(null));
        Assert.True(vm.BrowseSpeakerLinesCommand.CanExecute(null));
    }
}
