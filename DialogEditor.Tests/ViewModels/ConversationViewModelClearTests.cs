using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Clear() resets the canvas to the empty no-conversation state — used by
/// Close Project (the closed project's patched content must not stay visible).
/// Spec: docs/superpowers/specs/2026-07-05-close-project-design.md
public class ConversationViewModelClearTests
{
    public ConversationViewModelClearTests()
    {
        Loc.Configure(new StubStringProvider());
    }

    private static ConversationViewModel MakeVm() =>
        new(new StubDispatcher());

    private static NodeViewModel MakeNode(int id) =>
        new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);

    [Fact]
    public void Clear_EmptiesCanvasAndResetsState()
    {
        var vm = MakeVm();
        vm.Load(new Conversation("tavern", [], StringTable.Empty));
        vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));   // populates + dirties + pushes undo

        vm.Clear();

        Assert.Empty(vm.Nodes);
        Assert.Empty(vm.Connections);
        Assert.Empty(vm.Annotations);
        Assert.False(vm.IsModified);
        Assert.Null(vm.SelectedNode);
        Assert.Equal("", vm.ConversationName);
        Assert.Null(vm.BaseSnapshot);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Load_AfterClear_StillWorks()
    {
        var vm = MakeVm();
        vm.Clear();

        vm.Load(new Conversation("tavern", [], StringTable.Empty));

        Assert.Equal("tavern", vm.ConversationName);
        Assert.False(vm.IsModified);
    }
}
