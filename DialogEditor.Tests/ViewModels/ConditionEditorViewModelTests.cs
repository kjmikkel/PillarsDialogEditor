using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class ConditionEditorViewModelTests
{
    public ConditionEditorViewModelTests() => Loc.Configure(new StubStringProvider());

    private static ConditionEntry MakeEntry(string name = "IsGlobalValue") =>
        new(name, $"Display {name}", "Globals", ["poe1", "poe2"], "A condition.",
            [new ConditionParameter("Tag", "String", "Flag name", "MyFlag")]);

    private static ConditionLeaf MakeLeaf(string name = "Boolean A()") =>
        new(name, [], false, "And");

    // ── Constructor populates Rows from initial conditions ────────────────

    [Fact]
    public void Constructor_WithLeaves_PopulatesRows()
    {
        var vm = new ConditionEditorViewModel("Node 1",
            [MakeLeaf("Boolean A()"), MakeLeaf("Boolean B()")],
            _ => { });
        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public void Constructor_EmptyConditions_EmptyRows()
    {
        var vm = new ConditionEditorViewModel("Node 1", [], _ => { });
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Constructor_WithBranch_AddsBranchRow()
    {
        var branch = new ConditionBranch([MakeLeaf()], false, "And");
        var vm     = new ConditionEditorViewModel("Node 1", [branch], _ => { });
        Assert.Single(vm.Rows);
        Assert.True(vm.Rows[0].IsBranch);
    }

    // ── AddSelectedCondition ──────────────────────────────────────────────

    [Fact]
    public void AddSelectedCondition_WhenEntrySelected_AddsLeafRow()
    {
        var vm = new ConditionEditorViewModel("Node 1", [], _ => { });
        vm.SelectedNewCondition = MakeEntry("IsGlobalValue");
        vm.AddSelectedConditionCommand.Execute(null);
        Assert.Single(vm.Rows);
        Assert.True(vm.Rows[0].IsLeaf);
    }

    [Fact]
    public void AddSelectedCondition_ClearsSelection()
    {
        var vm = new ConditionEditorViewModel("Node 1", [], _ => { });
        vm.SelectedNewCondition = MakeEntry();
        vm.AddSelectedConditionCommand.Execute(null);
        Assert.Null(vm.SelectedNewCondition);
    }

    [Fact]
    public void AddSelectedCondition_WhenNoEntrySelected_DoesNothing()
    {
        var vm = new ConditionEditorViewModel("Node 1", [], _ => { });
        vm.SelectedNewCondition = null;
        vm.AddSelectedConditionCommand.Execute(null);
        Assert.Empty(vm.Rows);
    }

    // ── AddGroup ──────────────────────────────────────────────────────────

    [Fact]
    public void AddGroup_AddsBranchRow()
    {
        var vm = new ConditionEditorViewModel("Node 1", [], _ => { });
        vm.AddGroupCommand.Execute(null);
        Assert.Single(vm.Rows);
        Assert.True(vm.Rows[0].IsBranch);
    }

    // ── DeleteRow ─────────────────────────────────────────────────────────

    [Fact]
    public void DeleteRow_RemovesSpecifiedRow()
    {
        var vm = new ConditionEditorViewModel("Node 1",
            [MakeLeaf("Boolean A()"), MakeLeaf("Boolean B()")], _ => { });
        var rowToRemove = vm.Rows[0];
        vm.DeleteRowCommand.Execute(rowToRemove);
        Assert.Single(vm.Rows);
        Assert.DoesNotContain(rowToRemove, vm.Rows);
    }

    [Fact]
    public void DeleteRow_WithNull_DoesNothing()
    {
        var vm = new ConditionEditorViewModel("Node 1", [MakeLeaf()], _ => { });
        vm.DeleteRowCommand.Execute(null);
        Assert.Single(vm.Rows);
    }

    // ── MoveUp ────────────────────────────────────────────────────────────

    [Fact]
    public void MoveUp_MovesRowEarlierInList()
    {
        var vm = new ConditionEditorViewModel("Node 1",
            [MakeLeaf("Boolean A()"), MakeLeaf("Boolean B()")], _ => { });
        var rowB = vm.Rows[1];
        vm.MoveUpCommand.Execute(rowB);
        Assert.Equal(rowB, vm.Rows[0]);
    }

    [Fact]
    public void MoveUp_FirstRow_DoesNothing()
    {
        var vm = new ConditionEditorViewModel("Node 1",
            [MakeLeaf("Boolean A()"), MakeLeaf("Boolean B()")], _ => { });
        var firstRow = vm.Rows[0];
        vm.MoveUpCommand.Execute(firstRow);
        Assert.Equal(firstRow, vm.Rows[0]);
    }

    // ── MoveDown ──────────────────────────────────────────────────────────

    [Fact]
    public void MoveDown_MovesRowLaterInList()
    {
        var vm = new ConditionEditorViewModel("Node 1",
            [MakeLeaf("Boolean A()"), MakeLeaf("Boolean B()")], _ => { });
        var rowA = vm.Rows[0];
        vm.MoveDownCommand.Execute(rowA);
        Assert.Equal(rowA, vm.Rows[1]);
    }

    [Fact]
    public void MoveDown_LastRow_DoesNothing()
    {
        var vm = new ConditionEditorViewModel("Node 1",
            [MakeLeaf("Boolean A()"), MakeLeaf("Boolean B()")], _ => { });
        var lastRow = vm.Rows[1];
        vm.MoveDownCommand.Execute(lastRow);
        Assert.Equal(lastRow, vm.Rows[1]);
    }

    // ── Confirm ───────────────────────────────────────────────────────────

    [Fact]
    public void Confirm_CallsCommitWithCurrentRows()
    {
        IReadOnlyList<ConditionNode>? committed = null;
        var vm = new ConditionEditorViewModel("Node 1",
            [MakeLeaf("Boolean A()")], nodes => committed = nodes);
        vm.ConfirmCommand.Execute(null);
        Assert.NotNull(committed);
        Assert.Single(committed);
    }

    [Fact]
    public void Confirm_CommitsEmptyListWhenNoRows()
    {
        IReadOnlyList<ConditionNode>? committed = null;
        var vm = new ConditionEditorViewModel("Node 1", [], nodes => committed = nodes);
        vm.ConfirmCommand.Execute(null);
        Assert.NotNull(committed);
        Assert.Empty(committed);
    }

    [Fact]
    public void Confirm_FiresConfirmedEvent()
    {
        var fired = false;
        var vm    = new ConditionEditorViewModel("Node 1", [], _ => { });
        vm.Confirmed += () => fired = true;
        vm.ConfirmCommand.Execute(null);
        Assert.True(fired);
    }

    // ── Cancel ────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_FiresCancelledEvent()
    {
        var fired = false;
        var vm    = new ConditionEditorViewModel("Node 1", [], _ => { });
        vm.Cancelled += () => fired = true;
        vm.CancelCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void Cancel_DoesNotCallCommit()
    {
        var commitCalled = false;
        var vm = new ConditionEditorViewModel("Node 1", [], _ => commitCalled = true);
        vm.CancelCommand.Execute(null);
        Assert.False(commitCalled);
    }

    // ── NodeViewModel convenience constructor ─────────────────────────────

    [Fact]
    public void NodeConstructor_ConfirmUpdatesNodeConditions()
    {
        var node   = new ConversationNode(1, false, SpeakerCategory.Npc, "", "", [], [], [],
                                          "Conversation", "None");
        var nodeVm = new NodeViewModel(node, new StringEntry(1, "text", ""));
        nodeVm.UndoStack = new DialogEditor.Core.Editing.UndoRedoStack();

        var editorVm = new ConditionEditorViewModel(nodeVm);
        editorVm.AddGroupCommand.Execute(null);
        editorVm.ConfirmCommand.Execute(null);

        Assert.Single(nodeVm.Conditions);
    }
}
