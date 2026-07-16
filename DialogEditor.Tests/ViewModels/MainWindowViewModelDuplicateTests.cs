using System.Reflection;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// The duplicate panes wired through MainWindowViewModel.RequestTextTagValidationAsync.
public class MainWindowViewModelDuplicateTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowViewModelDuplicateTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_dup_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void Inject(MainWindowViewModel vm, string field, object value) =>
        typeof(MainWindowViewModel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, value);

    private static void InjectProject(MainWindowViewModel vm, DialogProject project) =>
        typeof(MainWindowViewModel).GetMethod("SetProject", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, [project]);

    private static ConversationPatch DupPatch()
    {
        const string line = "the wind howls through the rigging tonight";
        return new ConversationPatch("c1", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, line, ""), new NodeTranslation(2, line, "")]
            }
        };
    }

    [Fact]
    public async Task DuplicatesSurface_AndIgnorePersistsToProject()
    {
        var vm = MakeVm();
        Inject(vm, "_provider", new FakeGameDataProvider("poe2", "en"));
        InjectProject(vm, DialogProject.Empty("T").WithPatch(DupPatch()));

        var sweep = await vm.RequestTextTagValidationAsync();
        Assert.NotNull(sweep);
        Assert.True(sweep!.HasDuplicates);

        // Ignore the duplicate → project gets the entry, dirty flips, row moves.
        sweep.DuplicateRows[0].IgnoreCommand.Execute(null);

        Assert.True(vm.IsModified);
        Assert.False(sweep.HasDuplicates);
        Assert.True(sweep.HasIgnoredDuplicates);

        // Restore → active duplicate returns.
        sweep.IgnoredDuplicateRows[0].RestoreCommand.Execute(null);
        Assert.True(sweep.HasDuplicates);
        Assert.False(sweep.HasIgnoredDuplicates);
    }
}
