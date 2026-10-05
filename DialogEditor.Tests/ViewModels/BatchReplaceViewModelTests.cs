using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class BatchReplaceViewModelTests
{
    public BatchReplaceViewModelTests() => Loc.Configure(new StubStringProvider());

    private static ConversationFile MakeFile(string name) =>
        new(name, $@"quests\{name}.conversation", "quests", $@"quests\{name}.stringtable");

    private static NodeEditSnapshot MakeNode(int id, string defaultText) =>
        new(id, false, SpeakerCategory.Npc, "", "", defaultText, "",
            "Conversation", "None", "", "", "", false, false, [], [], []);

    private static BatchReplaceViewModel MakeVm(
        StubProvider? provider = null,
        ConversationFile? open  = null)
    {
        var file = MakeFile("conv");
        provider ??= new StubProvider(file,
            new ConversationEditSnapshot([MakeNode(1, "Hello world")]));
        var files = provider.EnumerateConversations();
        return new BatchReplaceViewModel(
            provider, files,
            isOpenInEditor: f => open is not null && f.Name == open.Name,
            currentProject: () => DialogProject.Empty("p"),
            commitProject:  _ => { },
            isTestActive:   () => false);
    }

    // ── Initial state ─────────────────────────────────────────────────────

    [Fact]
    public void InitialState_ResultsEmpty_HasResultsFalse()
    {
        var vm = MakeVm();
        Assert.Empty(vm.Results);
        Assert.False(vm.HasResults);
    }

    [Fact]
    public void PreviewCommand_CannotExecute_WhenSearchTextEmpty()
    {
        var vm = MakeVm();
        vm.SearchText = string.Empty;
        Assert.False(vm.PreviewCommand.CanExecute(null));
    }

    [Fact]
    public void PreviewCommand_CanExecute_WhenSearchTextSet()
    {
        var vm = MakeVm();
        vm.SearchText = "hello";
        Assert.True(vm.PreviewCommand.CanExecute(null));
    }

    // ── Preview populates results ─────────────────────────────────────────

    [Fact]
    public async Task Preview_WithMatch_PopulatesResults()
    {
        var vm = MakeVm();
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);

        Assert.Single(vm.Results);
        Assert.True(vm.HasResults);
    }

    [Fact]
    public async Task Preview_NoMatch_ResultsEmpty()
    {
        var vm = MakeVm();
        vm.SearchText  = "xyz";
        vm.ReplaceText = "abc";
        await vm.PreviewCommand.ExecuteAsync(null);

        Assert.Empty(vm.Results);
        Assert.False(vm.HasResults);
    }

    [Fact]
    public async Task Preview_SetsStatusText()
    {
        var vm = MakeVm();
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public async Task Preview_ConversationResult_HasCorrectBeforeAfter()
    {
        var vm = MakeVm();
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);

        var match = vm.Results[0].Matches[0];
        Assert.Equal("Hello world", match.Before);
        Assert.Equal("Hello earth", match.After);
    }

    // ── Apply guarded ─────────────────────────────────────────────────────

    [Fact]
    public void ApplyCommand_CannotExecute_BeforePreview()
    {
        var vm = MakeVm();
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyCommand_CanExecute_AfterPreviewWithResults()
    {
        var vm = MakeVm();
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);

        Assert.True(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyCommand_CannotExecute_WhenAllConversationsDeselected()
    {
        var vm = MakeVm();
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);
        vm.Results[0].IsSelected = false;

        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    // ── Apply edits the project, never the game folder (#124) ─────────────

    [Fact]
    public async Task Apply_CommitsTheEditedProject_AndWritesNoGameFile()
    {
        var file     = MakeFile("conv");
        var provider = new StubProvider(file,
            new ConversationEditSnapshot([MakeNode(1, "Hello world")]));
        DialogProject? committed = null;
        var vm = new BatchReplaceViewModel(
            provider, provider.EnumerateConversations(), _ => false,
            currentProject: () => DialogProject.Empty("p"),
            commitProject:  p => committed = p,
            isTestActive:   () => false);
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Null(provider.SavedSnapshot);
        Assert.Equal([new NodeTranslation(1, "Hello earth", "")],
                     committed!.Patches["conv"].Translations["en"]);
        Assert.Equal("BatchReplace_StatusApplied", vm.StatusText);
    }

    [Fact]
    public async Task Apply_ReadsTheProjectAtApplyTime()
    {
        // The window is non-modal: the user can keep editing (and saving) between
        // Preview and Apply, so Apply must build on the project as it is now.
        var file     = MakeFile("conv");
        var provider = new StubProvider(file,
            new ConversationEditSnapshot([MakeNode(1, "Hello world")]));
        var current  = DialogProject.Empty("p");
        DialogProject? committed = null;
        var vm = new BatchReplaceViewModel(
            provider, provider.EnumerateConversations(), _ => false,
            currentProject: () => current,
            commitProject:  p => committed = p,
            isTestActive:   () => false);
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);

        var other = new ConversationPatch("other", ConversationPatch.CurrentSchemaVersion, [], [], []);
        current = current.WithPatch(other);
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Same(other, committed!.Patches["other"]);
    }

    [Fact]
    public async Task Apply_WhileATestIsActive_CommitsNothing()
    {
        // During a test (F5) the game files already carry the project's patch, so the
        // "vanilla" Apply diffs against would be the patched file and the new patch would
        // silently drop every earlier edit to the conversation. Restore (F6) first.
        var file     = MakeFile("conv");
        var provider = new StubProvider(file,
            new ConversationEditSnapshot([MakeNode(1, "Hello world")]));
        var testActive = false;
        var commits    = 0;
        var vm = new BatchReplaceViewModel(
            provider, provider.EnumerateConversations(), _ => false,
            currentProject: () => DialogProject.Empty("p"),
            commitProject:  _ => commits++,
            isTestActive:   () => testActive);
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);

        testActive = true;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(0, commits);
        Assert.Equal("BatchReplace_StatusTestActive", vm.StatusText);
        Assert.NotEmpty(vm.Results);   // kept, so Apply works once the test is restored
    }

    [Fact]
    public async Task Apply_WithNoProjectOpen_CommitsNothing()
    {
        // The project was closed while the window stayed open: nothing to edit.
        var file     = MakeFile("conv");
        var provider = new StubProvider(file,
            new ConversationEditSnapshot([MakeNode(1, "Hello world")]));
        DialogProject? open = DialogProject.Empty("p");
        var commits = 0;
        var vm = new BatchReplaceViewModel(
            provider, provider.EnumerateConversations(), _ => false,
            currentProject: () => open,
            commitProject:  _ => commits++,
            isTestActive:   () => false);
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);

        open = null;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(0, commits);
        Assert.Equal("BatchReplace_StatusNoProject", vm.StatusText);
        Assert.Null(provider.SavedSnapshot);
    }

    [Fact]
    public async Task Apply_ClearsResultsAfterwards()
    {
        var vm = MakeVm();
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Empty(vm.Results);
        Assert.False(vm.HasResults);
    }

    // ── Open-in-editor guard ──────────────────────────────────────────────

    [Fact]
    public async Task Preview_OpenConversation_ExcludedFromResults()
    {
        var file     = MakeFile("open_conv");
        var provider = new StubProvider(file,
            new ConversationEditSnapshot([MakeNode(1, "Hello world")]));
        var vm = new BatchReplaceViewModel(
            provider, provider.EnumerateConversations(),
            isOpenInEditor: f => f.Name == "open_conv",
            currentProject: () => DialogProject.Empty("p"),
            commitProject:  _ => { },
            isTestActive:   () => false);

        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);

        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task Preview_OpenConversation_StatusTextMentionsSkip()
    {
        var file     = MakeFile("open_conv");
        var provider = new StubProvider(file,
            new ConversationEditSnapshot([MakeNode(1, "Hello world")]));
        var vm = new BatchReplaceViewModel(
            provider, provider.EnumerateConversations(),
            isOpenInEditor: f => f.Name == "open_conv",
            currentProject: () => DialogProject.Empty("p"),
            commitProject:  _ => { },
            isTestActive:   () => false);

        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);

        Assert.Contains("skip", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }
}
