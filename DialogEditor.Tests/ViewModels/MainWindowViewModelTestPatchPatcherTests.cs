using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Install;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Test Patch (F5) over files that mods installed with the Patch Manager / dialog-patcher
/// also manage must ask first (issue #76): testing would patch the modded versions, and the
/// patcher would see the editor's writes as "changed outside the patcher" on its next apply.
public class MainWindowViewModelTestPatchPatcherTests : IDisposable
{
    private readonly FakePoe2Game _game = new();
    private readonly string _settingsPath;

    public MainWindowViewModelTestPatchPatcherTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_patcher_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        _game.Dispose();
    }

    private static void Inject(MainWindowViewModel vm, string field, object? value) =>
        typeof(MainWindowViewModel)
            .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, value);

    private static void InjectProject(MainWindowViewModel vm, DialogProject project) =>
        typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [project]);

    /// Adds node 99 on test_conv whose text ("added line") lives in the en string table.
    private static DialogProject ProjectWithAddedNode()
    {
        var addedNode = new NodeEditSnapshot(99, false, SpeakerCategory.Npc, "spk", "lst",
            "", "", "Conversation", "None", "", "", "", false, false,
            [new LinkEditSnapshot(99, 1, 1, "", false)], [], []);
        var patch = new ConversationPatch(
            "test_conv", ConversationPatch.CurrentSchemaVersion, [addedNode], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(99, "added line", "")],
            },
        };
        return DialogProject.Empty("TestPatchProj").WithPatch(patch);
    }

    private MainWindowViewModel MakeVm()
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Inject(vm, "_provider", new Poe2GameDataProvider(_game.Root));
        Inject(vm, "_currentGameDirectory", _game.Root);
        InjectProject(vm, ProjectWithAddedNode());
        return vm;
    }

    [Theory]
    [InlineData(false, false)]   // patcher manages test_conv, user cancels → nothing written
    [InlineData(true,  true)]    // user continues → test patch applied
    public async Task TestPatch_OverPatcherMods_AsksFirst(bool answer, bool expectWritten)
    {
        PatchInstaller.Install(_game.Provider, _game.Root,
            [new InstallEntry(FakePoe2Game.ExternalVoMod("A", "a"))], new InstallOptions());
        var vm = MakeVm();
        int? asked = null;
        vm.ConfirmTestOverPatcherMods = n => { asked = n; return Task.FromResult(answer); };
        var stBefore = File.ReadAllText(_game.StPath("en"));

        await vm.TestPatchCommand.ExecuteAsync(null);

        Assert.True(asked > 0);
        Assert.Equal(expectWritten, File.ReadAllText(_game.StPath("en")).Contains("added line"));
        if (!answer)
        {
            Assert.Equal(stBefore, File.ReadAllText(_game.StPath("en")));
            Assert.Equal("Status_TestPatchCancelledPatcherMods", vm.StatusText);
        }
    }

    [Fact]
    public async Task TestPatch_NoPatcherMods_DoesNotAsk()
    {
        var vm = MakeVm();
        var asked = false;
        vm.ConfirmTestOverPatcherMods = _ => { asked = true; return Task.FromResult(true); };

        await vm.TestPatchCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.Contains("added line", File.ReadAllText(_game.StPath("en")));
    }
}
