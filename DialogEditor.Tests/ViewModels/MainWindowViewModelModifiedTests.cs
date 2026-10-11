using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 142: viewing must never mark the conversation modified. Selecting a node highlights
/// its links (ConnectionViewModel.IsHighlighted), and a blanket PropertyChanged hook on every
/// connection used to count that view state as an edit — the title gained ●, and the next
/// conversation or language switch asked to save changes nobody made.
public class MainWindowViewModelModifiedTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowViewModelModifiedTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_mod_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static ConversationNode Node(int id, params int[] linksTo) => new(
        NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
        SpeakerGuid: "", ListenerGuid: "",
        Links: [.. linksTo.Select(to => new NodeLink(id, to, []))], Conditions: [], Scripts: [],
        DisplayType: "Conversation", Persistence: "None");

    /// 1 → 2 → 3, so node 2 has a link in and a link out.
    private static MainWindowViewModel LoadedVm()
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        vm.Canvas.Load(new Conversation("c", [Node(1, 2), Node(2, 3), Node(3)],
            new StringTable([new StringEntry(1, "a", ""), new StringEntry(2, "b", ""), new StringEntry(3, "c", "")])));
        Assert.False(vm.IsModified);
        return vm;
    }

    private static NodeViewModel NodeVm(MainWindowViewModel vm, int id) => vm.Canvas.Nodes.Single(n => n.NodeId == id);

    [Fact]
    public void SelectingALinkedNode_LeavesTheConversationUnmodified()
    {
        var vm = LoadedVm();

        vm.Canvas.SelectedNode = NodeVm(vm, 2);

        Assert.Contains(vm.Canvas.Connections, c => c.IsHighlighted);   // the trigger really fired
        Assert.False(vm.Canvas.IsModified);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public void SelectingAnotherNode_AndDeselecting_LeavesTheConversationUnmodified()
    {
        var vm = LoadedVm();

        vm.Canvas.SelectedNode = NodeVm(vm, 2);
        vm.Canvas.SelectedNode = NodeVm(vm, 1);
        vm.Canvas.SelectedNode = null;

        Assert.False(vm.IsModified);
    }

    [Fact]
    public void EditingALink_StillMarksTheConversationModified_AndIsUndoable()
    {
        var vm   = LoadedVm();
        var link = vm.Canvas.Connections.First();

        link.RandomWeight = 3;

        Assert.True(vm.IsModified);
        vm.Canvas.UndoCommand.Execute(null);
        Assert.Equal(1, link.RandomWeight);
    }

    [Theory]
    [InlineData("display")]
    [InlineData("conditions")]
    public void OtherLinkEdits_StillMarkTheConversationModified(string field)
    {
        var vm   = LoadedVm();
        var link = vm.Canvas.Connections.First();

        if (field == "display") link.QuestionNodeTextDisplay = "ShowAlways";
        else                    link.Conditions = [new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)", ["a", "EqualTo", "1"], false, "And")];

        Assert.True(vm.IsModified);
    }
}
