using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Canvas Undo and Redo only run while the canvas is editable (issue 234): reading mode, and a
/// canvas with no project, must not be able to change the conversation through Ctrl+Z / Ctrl+Y.
public class ConversationViewModelUndoGateTests
{
    public ConversationViewModelUndoGateTests() => Loc.Configure(new StubStringProvider());

    private static ConversationViewModel MakeVmWithUndoAndRedo()
    {
        var vm = new ConversationViewModel(new StubDispatcher()) { IsEditable = true };
        vm.Load(new Conversation("tavern", [], StringTable.Empty));
        var node = new NodeViewModel(new ConversationNode(1, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);
        vm.AddNode(node, new LayoutPoint(0, 0));
        vm.Undo();   // leaves one entry on each stack's far side: Redo now available
        vm.AddNode(node, new LayoutPoint(0, 0));
        vm.Undo();
        vm.Redo();
        vm.Undo();
        return vm;
    }

    [Fact]
    public void UndoAndRedo_CannotRun_WhenNotEditable()
    {
        var vm = MakeVmWithUndoAndRedo();
        vm.AddNode(new NodeViewModel(new ConversationNode(2, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null), new LayoutPoint(0, 0));
        Assert.True(vm.UndoCommand.CanExecute(null));

        vm.IsEditable = false;

        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.RedoCommand.CanExecute(null));
    }

    [Fact]
    public void Redo_CanRun_WhenEditableAndRedoAvailable()
    {
        var vm = MakeVmWithUndoAndRedo();
        Assert.True(vm.CanRedo);

        Assert.True(vm.RedoCommand.CanExecute(null));
        vm.IsEditable = false;
        Assert.False(vm.RedoCommand.CanExecute(null));
        vm.IsEditable = true;
        Assert.True(vm.RedoCommand.CanExecute(null));
    }
}
