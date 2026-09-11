using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// The last of the ViewModels-layer user-visible strings: two editor titles, three
/// file/folder picker titles, two language prompts, the flow-analytics "no text"
/// snippet, and the two voice-over failure messages.
///
/// Picker and prompt titles are easy to overlook because they are arguments rather
/// than properties, but the user reads them in the OS dialog's chrome.
/// </summary>
public class RemainingViewModelStringTests
{
    public RemainingViewModelStringTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel Node(int id = 7) =>
        new(new ConversationNode(
                NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
                DisplayType: "Conversation", Persistence: "None"),
            new StringEntry(id, "Hello", ""));

    [Fact]
    public void ConditionEditorNodeTitle_ComesFromResources()
    {
        Assert.Equal("Editor_NodeTitle", new ConditionEditorViewModel(Node()).NodeTitle);
    }

    [Fact]
    public void ScriptEditorNodeTitle_ComesFromResources()
    {
        Assert.Equal("Editor_NodeTitle", new ScriptEditorViewModel(Node()).NodeTitle);
    }

    [Fact]
    public async Task NullVoImporter_FailureMessage_ComesFromResources()
    {
        var result = await NullVoImporter.Instance.ImportAsync(
            new VoImportRequest("a.wem", "a.wav", null, null), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Status_VoNoImporter", result.ErrorMessage);
    }
}

/// <summary>Tier two: the real Strings.axaml.</summary>
public class RemainingViewModelStringResourceTests
{
    public RemainingViewModelStringResourceTests() => Loc.Configure(new AvaloniaStringProvider());

    private static NodeViewModel Node(int id = 7) =>
        new(new ConversationNode(
                NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
                DisplayType: "Conversation", Persistence: "None"),
            new StringEntry(id, "Hello", ""));

    [AvaloniaFact]
    public void EditorNodeTitles_NameTheNode()
    {
        Assert.Equal("Node 7", new ConditionEditorViewModel(Node()).NodeTitle);
        Assert.Equal("Node 7", new ScriptEditorViewModel(Node()).NodeTitle);
    }

    [AvaloniaFact]
    public async Task NullVoImporter_FailureMessage_UsesRealResources()
    {
        var result = await NullVoImporter.Instance.ImportAsync(
            new VoImportRequest("a.wem", "a.wav", null, null), CancellationToken.None);

        Assert.Equal("No importer configured.", result.ErrorMessage);
    }

    [AvaloniaFact]
    public void ExportConversations_PickerTitles_UseRealResources()
    {
        // One selected conversation saves a file; several write into a folder — two
        // different dialogs, so two separate strings rather than a count-driven plural.
        Assert.Equal("Export Conversation",  Loc.Get("Export_PickFileTitle"));
        Assert.Equal("Export Conversations", Loc.Get("Export_PickFolderTitle"));
    }

    [AvaloniaFact]
    public void TranslationPickerTitlesAndLanguagePrompts_UseRealResources()
    {
        Assert.Equal("Export for Translation", Loc.Get("Localization_ExportPickerTitle"));
        Assert.Equal("Import Translation",     Loc.Get("Localization_ImportPickerTitle"));
        Assert.Equal("Source language",        Loc.Get("Localization_SourceLanguagePrompt"));
        Assert.Equal("Target language",        Loc.Get("Localization_TargetLanguagePrompt"));
    }

    [AvaloniaFact]
    public void FlowAnalyticsNoTextSnippet_NamesTheSpeakerCategory()
    {
        Assert.Equal("(npc, no text)", Loc.Format("FlowAnalytics_NoTextSnippet", "npc"));
        Assert.Equal("unknown", Loc.Get("FlowAnalytics_UnknownSpeaker"));
    }

    [AvaloniaFact]
    public void UnknownErrorFallback_UsesRealResources()
    {
        Assert.Equal("unknown error", Loc.Get("Status_UnknownError"));
    }
}

/// <summary>
/// The picker and prompt titles MainWindowViewModel passes when exporting or importing
/// a translation. Asserted through the recording stubs, because these are arguments to
/// a dialog rather than properties on the ViewModel.
/// </summary>
public class TranslationPickerTitleTests
{
    public TranslationPickerTitleTests() => Loc.Configure(new StubStringProvider());

    // Both commands are gated on IsProjectLoaded, and SetProject is private.
    private static void InjectProject(MainWindowViewModel vm)
    {
        var patch = new ConversationPatch("test_conv", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "Hello", "")],
            },
        };
        typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic
                                   | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [DialogProject.Empty("TestProject").WithPatch(patch)]);
    }

    [Fact]
    public async Task ExportForTranslation_UsesResourcesForItsTitleAndPrompt()
    {
        var picker = new StubFilePicker(saveResult: null);
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), picker);
        InjectProject(vm);

        string? prompt = null;
        vm.RequestLanguageCode = (title, _) => { prompt = title; return Task.FromResult<string?>(null); };

        await vm.ExportForTranslationCommand.ExecuteAsync(null);

        Assert.Contains("Localization_ExportPickerTitle", picker.Titles);
        // The picker was cancelled (saveResult null), so the prompt is never reached.
        Assert.Null(prompt);
    }

    [Fact]
    public async Task ImportTranslation_UsesResourcesForItsTitle()
    {
        var picker = new StubFilePicker(openResult: null);
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), picker);
        InjectProject(vm);

        await vm.ImportTranslationCommand.ExecuteAsync(null);

        Assert.Contains("Localization_ImportPickerTitle", picker.Titles);
    }
}
