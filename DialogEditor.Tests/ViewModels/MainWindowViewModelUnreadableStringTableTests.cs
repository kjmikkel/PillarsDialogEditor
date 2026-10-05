using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// A damaged stringtable (#119) must not stop a conversation from opening, must be named to
/// the user, and must never be overwritten by Test Patch.
public class MainWindowViewModelUnreadableStringTableTests : IDisposable
{
    private static readonly byte[] Garbage = [0x2C, 0x56, 0x00, 0x1F, 0x2A, 0xD9, 0x25, 0x06];

    private readonly FakePoe2Game _game = new("en", "de");
    private readonly string _settingsPath;
    private readonly string _projectDir;

    public MainWindowViewModelUnreadableStringTableTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_unreadable_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _projectDir = Path.Combine(Path.GetTempPath(), $"unreadable_proj_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectDir);
        File.WriteAllBytes(_game.StPath("de"), Garbage);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_projectDir, recursive: true); } catch (Exception) { /* best-effort */ }
        _game.Dispose();
    }

    private MainWindowViewModel MakeVm(IGameDataProvider provider)
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Inject(vm, "_provider", provider);
        Inject(vm, "_currentGameDirectory", _game.Root);
        Inject(vm, "_activeGameId", "poe2");
        Inject(vm, "_projectPath", Path.Combine(_projectDir, "proj.dialogproj"));
        typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [DialogProject.Empty("Unreadable").WithPatch(PatchWithText())]);
        return vm;
    }

    private static void Inject(MainWindowViewModel vm, string field, object? value) =>
        typeof(MainWindowViewModel)
            .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, value);

    private static ConversationPatch PatchWithText() =>
        new("test_conv", ConversationPatch.CurrentSchemaVersion,
            [new NodeEditSnapshot(99, false, SpeakerCategory.Npc, "spk", "lst", "", "",
                "Conversation", "None", "", "", "", false, false,
                [new LinkEditSnapshot(99, 1, 1, "", false)], [], [])], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(99, "added line", "")],
                ["de"] = [new NodeTranslation(99, "neue Zeile", "")],
            },
        };

    [Fact]
    public void OpeningTheConversationInTheDamagedLanguage_LoadsItAndNamesTheFile()
    {
        var provider = _game.Provider;
        provider.Language = "de";
        var vm = MakeVm(provider);

        typeof(MainWindowViewModel)
            .GetMethod("OnConversationSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [provider.FindConversation("test_conv")!]);

        Assert.NotEmpty(vm.Canvas.Nodes);
        Assert.Equal("Status_StringTableUnreadable", vm.StatusText);
    }

    [Fact]
    public async Task TestPatch_LeavesTheDamagedFileUntouched_AndSaysSo()
    {
        var vm = MakeVm(_game.Provider);

        await vm.TestPatchCommand.ExecuteAsync(null);

        Assert.Equal(Garbage, File.ReadAllBytes(_game.StPath("de")));
        Assert.Contains("added line", File.ReadAllText(_game.StPath("en")));
        Assert.StartsWith("Status_TestPatchSkippedUnreadable", vm.StatusText);
    }
}
