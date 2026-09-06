using System.Reflection;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Per-node attribution is HEAD-based blame, built lazily and then cached against the
/// project path — cheap, because HEAD does not normally move while a project is open.
/// It does move when the app commits, and issue #12 is that nothing told the cache.
public class MainWindowViewModelAttributionTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowViewModelAttributionTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_attr_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    /// The cache keys off _projectPath, so a lookup only builds once a project is open.
    private static void SetProjectPath(MainWindowViewModel vm, string path) =>
        typeof(MainWindowViewModel)
            .GetField("_projectPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, path);

    [Fact]
    public void AttributionIsBuiltOnceAndThenCached()
    {
        var vm = MakeVm();
        SetProjectPath(vm, Path.Combine(Path.GetTempPath(), "attr.dialogproject"));
        var loads = 0;
        vm.AttributionLoader = _ => { loads++; return Array.Empty<NodeBlame>(); };

        vm.Detail.AttributionLookup!("conv", 1);
        vm.Detail.AttributionLookup!("conv", 1);

        Assert.Equal(1, loads);
    }

    [Fact]
    public void InvalidateAttributionForcesTheNextLookupToRebuild()
    {
        // The caching above is what makes a commit go unnoticed: without an explicit
        // invalidation the node detail panel keeps reporting the pre-commit author, and
        // looks authoritative doing it.
        var vm = MakeVm();
        SetProjectPath(vm, Path.Combine(Path.GetTempPath(), "attr.dialogproject"));
        var loads = 0;
        vm.AttributionLoader = _ => { loads++; return Array.Empty<NodeBlame>(); };
        vm.Detail.AttributionLookup!("conv", 1);

        vm.InvalidateAttribution();
        vm.Detail.AttributionLookup!("conv", 1);

        Assert.Equal(2, loads);
    }
}
