using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Issue 240: with only a game folder open ("Viewing only") the canvas is read-only, but
/// Node Details still took edits — the card changed and the title got the dirty marker,
/// with no project to save to. Node Details follows the canvas's editability.
public class NodeDetailViewModelReadOnlyTests
{
    public NodeDetailViewModelReadOnlyTests() => Loc.Configure(new StubStringProvider());

    private static (NodeDetailViewModel vm, ConversationViewModel canvas) Make(bool editable)
    {
        var canvas = new ConversationViewModel(new StubDispatcher()) { IsEditable = editable };
        var vm = new NodeDetailViewModel { Canvas = canvas };
        vm.Load(new NodeViewModel(new ConversationNode(
                NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
                DisplayType: "Conversation", Persistence: "None", ActorDirection: "",
                Comments: "", ExternalVO: "alias", HasVO: false, HideSpeaker: false),
            new StringEntry(1, "Hello", "")));
        return (vm, canvas);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsEditable_FollowsTheCanvas(bool editable)
    {
        var (vm, _) = Make(editable);

        Assert.Equal(editable, vm.IsEditable);
        Assert.Equal(!editable, vm.IsReadOnly);
    }

    [Fact]
    public void OpeningAProject_RaisesEditability()
    {
        var (vm, canvas) = Make(editable: false);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        canvas.IsEditable = true;   // MainWindowViewModel.SetProject

        Assert.True(vm.IsEditable);
        Assert.Contains(nameof(vm.IsEditable), changed);
        Assert.Contains(nameof(vm.IsReadOnly), changed);
    }

    [Fact]
    public void ReadOnly_DisablesTheEditingCommands()
    {
        var (vm, _) = Make(editable: false);
        vm.AddLinkTargetId = "2";

        Assert.False(vm.AddLinkCommand.CanExecute(null));
        Assert.False(vm.DeleteLinkCommand.CanExecute(null));
        Assert.False(vm.PickVoAliasCommand.CanExecute(null));
        Assert.False(vm.ClearVoAliasCommand.CanExecute(null));
    }

    [Fact]
    public void OpeningAProject_ReEnablesTheEditingCommands()
    {
        var (vm, canvas) = Make(editable: false);
        var raised = 0;
        vm.AddLinkCommand.CanExecuteChanged += (_, _) => raised++;

        canvas.IsEditable = true;

        Assert.True(raised > 0);
        Assert.True(vm.AddLinkCommand.CanExecute(null));
        Assert.True(vm.PickVoAliasCommand.CanExecute(null));
    }

    [Fact]
    public void SwappingTheCanvas_StopsFollowingTheOldOne()
    {
        var (vm, oldCanvas) = Make(editable: true);
        vm.Canvas = new ConversationViewModel(new StubDispatcher()) { IsEditable = true };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        oldCanvas.IsEditable = false;

        Assert.True(vm.IsEditable);
        Assert.DoesNotContain(nameof(vm.IsEditable), changed);
    }
}
