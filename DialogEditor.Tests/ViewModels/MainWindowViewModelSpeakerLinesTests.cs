using System.Reflection;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// The Speaker Line Browser open path on MainWindowViewModel: gate + browser-specific
/// three-way dirty guard, mirroring MainWindowViewModelTextTagTests.
public class MainWindowViewModelSpeakerLinesTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _projectPath;

    public MainWindowViewModelSpeakerLinesTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_sl_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _projectPath = Path.Combine(Path.GetTempPath(), $"sl_{Guid.NewGuid():N}.dialogproject");
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { if (File.Exists(_projectPath)) File.Delete(_projectPath); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void Inject(MainWindowViewModel vm, string field, object value)
    {
        var fi = typeof(MainWindowViewModel).GetField(field,
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        fi.SetValue(vm, value);
    }

    private static void InjectProject(MainWindowViewModel vm, DialogProject project)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("SetProject",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        mi.Invoke(vm, [project]);
    }

    /// An open project + a stub provider, so the open path passes its project/provider gate.
    private MainWindowViewModel OpenProject(bool withPath = true)
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        Inject(vm, "_provider", new FakeGameDataProvider("poe2", "en"));
        if (withPath) Inject(vm, "_projectPath", _projectPath);
        return vm;
    }

    [Fact]
    public async Task NoProject_DoesNotOpen()
    {
        var vm = MakeVm();
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.Null(shown);
    }

    [Fact]
    public async Task CleanProject_Opens_WithoutConsultingGuard()
    {
        var vm = OpenProject();
        var consulted = false;
        vm.ConfirmBrowseWithUnsavedChanges = () => { consulted = true; return Task.FromResult(ScanDirtyChoice.Cancel); };
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.NotNull(shown);
        Assert.False(consulted);
    }

    [Fact]
    public async Task Dirty_Cancel_DoesNotOpen_NorSave()
    {
        var vm = OpenProject();
        vm.IsModified = true;
        vm.ConfirmBrowseWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.Cancel);
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.Null(shown);
        Assert.True(vm.IsModified);
        Assert.False(File.Exists(_projectPath));
    }

    [Fact]
    public async Task Dirty_ScanSavedOnly_Opens_WithoutSaving()
    {
        var vm = OpenProject();
        vm.IsModified = true;
        vm.ConfirmBrowseWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.ScanSavedOnly);
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.NotNull(shown);
        Assert.True(vm.IsModified);
        Assert.False(File.Exists(_projectPath));
    }

    [Fact]
    public async Task Dirty_SaveAndScan_SavesThenOpens()
    {
        var vm = OpenProject();
        vm.IsModified = true;
        vm.ConfirmBrowseWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.SaveAndScan);
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.NotNull(shown);
        Assert.False(vm.IsModified);              // SaveProject flipped the flag
        Assert.True(File.Exists(_projectPath));
    }

    [Fact]
    public async Task Dirty_NoGuardWired_DoesNotOpen()
    {
        var vm = OpenProject();
        vm.IsModified = true;                     // no ConfirmBrowseWithUnsavedChanges set
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.Null(shown);
    }

    [Fact]
    public async Task InitialSpeaker_IsForwarded_FromContextEvent()
    {
        var vm = OpenProject();
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        // Simulate the canvas context action for a node whose speaker is Bao.
        await vm.OpenSpeakerLineBrowserAsync("9c5f12c9-e93d-4952-9f1a-726c9498f8fb");

        Assert.NotNull(shown);   // opened; the pre-selection is exercised by the VM's own tests
    }
}
