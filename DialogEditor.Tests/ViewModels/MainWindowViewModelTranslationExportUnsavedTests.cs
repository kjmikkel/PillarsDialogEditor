using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 326: Export for translation reads the project as last saved, so with unsaved canvas
/// edits open it silently left them out. It now asks first (Save / Discard / Stay Here), the way
/// Merge Projects, Validate Text, Find in Project and Batch Replace do.
public class MainWindowViewModelTranslationExportUnsavedTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _dir;
    private readonly string _fallback;
    private string ProjectPath => Path.Combine(_dir, "a.dialogproject");
    private string ExportPath  => Path.Combine(_dir, "export.csv");

    public MainWindowViewModelTranslationExportUnsavedTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_te_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _dir = Path.Combine(Path.GetTempPath(), $"te_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _fallback = Path.Combine(Path.GetTempPath(), $"te_fb_{Guid.NewGuid():N}");
        AutosaveRecovery.FallbackRootOverride = _fallback;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        AutosaveRecovery.FallbackRootOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_fallback, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private const System.Reflection.BindingFlags Flags =
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    private static ConversationNode PlainNode(int id) =>
        new(id, false, SpeakerCategory.Npc, "spk", "lst", [], [], [], "Conversation", "None");

    private static ConversationPatch EditPatch(string text) =>
        new ConversationPatch("game", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, text, "")],
            },
        };

    /// "game" open with its saved text "saved text", then edited on the canvas to "dirty edit",
    /// then Export for translation with the prompt answered by <paramref name="answer"/>.
    private async Task<MainWindowViewModel> DirtyThenExport(Action<MainWindowViewModel> answer)
    {
        DialogProjectSerializer.SaveToFile(ProjectPath, DialogProject.Empty("A").WithPatch(EditPatch("saved text")));
        var provider = new FakeGameDataProvider("poe2", "en",
            new Conversation("game", [PlainNode(1)], new StringTable([new StringEntry(1, "vanilla text", "")])));
        AppSettings.Mode = EditorMode.Editing;
        AppSettings.DefaultLocalizationFormat = "Csv";
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(),
            new StubFilePicker(saveResult: ExportPath));
        typeof(MainWindowViewModel).GetField("_provider", Flags)!.SetValue(vm, provider);
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadProjectAsync", Flags)!.Invoke(vm, [ProjectPath, false])!;
        typeof(MainWindowViewModel).GetMethod("OnConversationSelected", Flags)!
            .Invoke(vm, [provider.BuildNewConversationFile("game")]);
        vm.RequestLanguageCode = (_, _) => Task.FromResult<string?>("en");

        vm.Canvas.Nodes.Single(n => n.NodeId == 1).DefaultText = "dirty edit";
        Assert.True(vm.IsModified);
        var asked = 0;
        vm.UnsavedChangesRequested += () => { asked++; answer(vm); };

        await vm.ExportForTranslationCommand.ExecuteAsync(null);

        Assert.Equal(1, asked);
        return vm;
    }

    [Fact]
    public async Task ExportForTranslation_WithUnsavedEdits_Save_ExportsTheEdits()
    {
        await DirtyThenExport(v => v.SaveAndProceed());

        Assert.Contains("dirty edit", File.ReadAllText(ExportPath));
    }

    [Fact]
    public async Task ExportForTranslation_WithUnsavedEdits_Discard_ExportsTheSavedText()
    {
        await DirtyThenExport(v => v.DiscardAndProceed());

        var exported = File.ReadAllText(ExportPath);
        Assert.Contains("saved text", exported);
        Assert.DoesNotContain("dirty edit", exported);
    }

    [Fact]
    public async Task ExportForTranslation_WithUnsavedEdits_StayHere_ExportsNothing()
    {
        var vm = await DirtyThenExport(v => v.CancelPendingNavigation());

        Assert.False(File.Exists(ExportPath));
        Assert.True(vm.IsModified);
    }
}
