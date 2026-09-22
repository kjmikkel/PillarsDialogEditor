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

    [Fact] // End to end: the persisted toggle, the real corpus loader, and the real scanner.
    public async Task BaseGame_ToggleOn_ReportsCopyOfVanillaLine()
    {
        const string line = "the wind howls through the rigging tonight";
        var vanillaConv = new Conversation("vanilla_conv",
            [new ConversationNode(7, false, SpeakerCategory.Npc, "spk", "", [], [], [], "Conversation", "None")],
            new StringTable([new StringEntry(7, line, "")]));

        var vm = MakeVm();
        Inject(vm, "_provider", new FakeGameDataProvider("poe2", "en", vanillaConv));
        InjectProject(vm, DialogProject.Empty("T").WithPatch(
            new ConversationPatch("mine", ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                    { ["en"] = [new NodeTranslation(1, line, "")] }
            }));
        AppSettings.DuplicateIncludeBaseGame = true;

        // See EchoStringProvider: the marker is only visible through a real format.
        Loc.Configure(new EchoStringProvider(new() { ["Duplicate_Source"] = "{0} [{1}]" }));

        var sweep = await vm.RequestTextTagValidationAsync();
        Assert.NotNull(sweep);
        Assert.True(sweep!.CanCompareBaseGame);
        await sweep.BaseGameScanTask;

        Assert.True(sweep.HasDuplicates);
        Assert.Contains("[Duplicate_Source_BaseGame]", sweep.DuplicateRows[0].Locations);
    }

    [Fact]
    public async Task BaseGame_NoProvider_CannotCompare()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T").WithPatch(DupPatch()));

        var sweep = await vm.RequestTextTagValidationAsync();

        Assert.False(sweep!.CanCompareBaseGame);
    }

    [Fact]
    public async Task BaseGame_TogglePersistsToSettings()
    {
        var vm = MakeVm();
        Inject(vm, "_provider", new FakeGameDataProvider("poe2", "en"));
        InjectProject(vm, DialogProject.Empty("T").WithPatch(DupPatch()));

        var sweep = await vm.RequestTextTagValidationAsync();
        sweep!.IncludeBaseGame = true;
        await sweep.BaseGameScanTask;

        Assert.True(AppSettings.DuplicateIncludeBaseGame);
    }
}
