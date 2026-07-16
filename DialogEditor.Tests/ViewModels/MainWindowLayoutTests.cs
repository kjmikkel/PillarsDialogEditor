using System.Reflection;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// <summary>
/// Docking Shell Phase 1 (Task 4/5): MainWindowViewModel now owns the shell-level
/// ConditionSearchViewModel (previously ConversationViewModel.ConditionSearch) so the
/// EditorDockFactory can host it as its own dock tool alongside Browser/Canvas/Detail.
/// </summary>
public class MainWindowLayoutTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowLayoutTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwlt_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void SetPrivateField(MainWindowViewModel vm, string field, object? value)
    {
        var fi = typeof(MainWindowViewModel)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!;
        fi.SetValue(vm, value);
    }

    /// <summary>
    /// Invokes the private RebuildConditionSearch(string gameId) helper — the same code
    /// path LoadDirectory runs after detecting a provider — via reflection, mirroring the
    /// InjectProject/InjectProvider pattern in MainWindowViewModelTests.
    /// </summary>
    private static void RebuildConditionSearch(MainWindowViewModel vm, string gameId)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("RebuildConditionSearch", BindingFlags.NonPublic | BindingFlags.Instance)!;
        mi.Invoke(vm, [gameId]);
    }

    [Fact]
    public void ConditionSearch_IsNullBeforeGameLoads()
    {
        var vm = MakeVm();
        Assert.Null(vm.ConditionSearch);
    }

    [Fact]
    public void ConditionSearch_BuiltAfterGameLoad_AppliesHighlightToCanvas()
    {
        var vm = MakeVm();
        SetPrivateField(vm, "_activeGameId", "poe2");

        RebuildConditionSearch(vm, "poe2");

        Assert.NotNull(vm.ConditionSearch);

        var node = CanvasNavigationServiceTests.MakeNode(1);
        vm.Canvas.Nodes.Add(node);

        // Select the first catalogue entry (embedded conditions.json — no game folder needed)
        // and run a search. Even a zero-match search must dim/mark every canvas node, proving
        // the shell-level ConditionSearchViewModel is wired to THIS Canvas instance.
        vm.ConditionSearch!.SelectedEntry = vm.ConditionSearch.Entries.FirstOrDefault();
        Assert.NotNull(vm.ConditionSearch.SelectedEntry);

        vm.ConditionSearch.SearchCommand.Execute(null);

        Assert.NotEqual(SearchMatchState.None, node.SearchMatchState);
    }
}
