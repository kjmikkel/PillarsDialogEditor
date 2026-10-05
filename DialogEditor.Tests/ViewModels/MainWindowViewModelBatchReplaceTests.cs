using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Batch Replace edits the open project, not the game folder (#124), so the main window
/// gates it on a project and takes the edited project back as unsaved changes.
public class MainWindowViewModelBatchReplaceTests
{
    public MainWindowViewModelBatchReplaceTests() => Loc.Configure(new StubStringProvider());

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void Inject(MainWindowViewModel vm, string field, object? value) =>
        typeof(MainWindowViewModel).GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, value);

    private static DialogProject? GetProject(MainWindowViewModel vm) =>
        (DialogProject?)typeof(MainWindowViewModel).GetField("_project",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vm);

    private static StubProvider Provider() =>
        new(new ConversationFile("conv", @"quests\conv.conversation", "quests", @"quests\conv.stringtable"),
            new ConversationEditSnapshot([
                new NodeEditSnapshot(1, false, SpeakerCategory.Npc, "", "", "Hello world", "",
                    "Conversation", "None", "", "", "", false, false, [], [], [])]));

    [Fact]
    public void BatchReplace_NeedsAnOpenProject()
    {
        var vm = MakeVm();
        Inject(vm, "_provider", Provider());

        Assert.False(vm.BatchReplaceCommand.CanExecute(null));

        Inject(vm, "_project", DialogProject.Empty("p"));
        Assert.True(vm.BatchReplaceCommand.CanExecute(null));
    }

    [Fact]
    public async Task BatchReplace_Apply_PutsTheEditsInTheProjectAsUnsavedChanges()
    {
        var vm       = MakeVm();
        var provider = Provider();
        Inject(vm, "_provider", provider);
        Inject(vm, "_project", DialogProject.Empty("p"));
        BatchReplaceViewModel? shown = null;
        vm.ShowBatchReplace = brVm => { shown = brVm; return Task.CompletedTask; };

        await vm.BatchReplaceCommand.ExecuteAsync(null);
        shown!.SearchText  = "world";
        shown.ReplaceText = "earth";
        await shown.PreviewCommand.ExecuteAsync(null);
        await shown.ApplyCommand.ExecuteAsync(null);

        Assert.Null(provider.SavedSnapshot);
        Assert.Equal([new NodeTranslation(1, "Hello earth", "")],
                     GetProject(vm)!.Patches["conv"].Translations["en"]);
        Assert.True(vm.IsModified);
    }
}
