using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
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
            isOpenInEditor: f => open is not null && f.Name == open.Name);
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

    // ── Apply calls save ──────────────────────────────────────────────────

    [Fact]
    public async Task Apply_CallsSaveOnProvider()
    {
        var file     = MakeFile("conv");
        var provider = new StubProvider(file,
            new ConversationEditSnapshot([MakeNode(1, "Hello world")]));
        var vm = new BatchReplaceViewModel(
            provider, provider.EnumerateConversations(), _ => false);
        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.NotNull(provider.SavedSnapshot);
        Assert.Equal("Hello earth", provider.SavedSnapshot!.Nodes[0].DefaultText);
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
            isOpenInEditor: f => f.Name == "open_conv");

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
            isOpenInEditor: f => f.Name == "open_conv");

        vm.SearchText  = "world";
        vm.ReplaceText = "earth";
        await vm.PreviewCommand.ExecuteAsync(null);

        Assert.Contains("skip", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }
}
