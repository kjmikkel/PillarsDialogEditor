using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class ScriptEditorViewModelTests
{
    public ScriptEditorViewModelTests() => Loc.Configure(new StubStringProvider());

    private static ScriptCall MakeCall(string name, ScriptCategory cat) =>
        new(name, [], cat);

    private static ScriptCatalogueEntry MakeEntry(string name = "MyScript") =>
        new(name, $"Display {name}", "General", ["poe1", "poe2"], "A script.", []);

    // ── Constructor populates per-category rows ───────────────────────────

    [Fact]
    public void Constructor_SortsCallsIntoCorrectCategories()
    {
        var vm = new ScriptEditorViewModel("Node 1", [
            MakeCall("Enter1", ScriptCategory.Enter),
            MakeCall("Exit1",  ScriptCategory.Exit),
            MakeCall("Upd1",   ScriptCategory.Update)
        ], _ => { });

        Assert.Single(vm.EnterRows);
        Assert.Single(vm.ExitRows);
        Assert.Single(vm.UpdateRows);
    }

    [Fact]
    public void Constructor_EmptyScripts_AllRowsEmpty()
    {
        var vm = new ScriptEditorViewModel("Node 1", [], _ => { });
        Assert.Empty(vm.EnterRows);
        Assert.Empty(vm.ExitRows);
        Assert.Empty(vm.UpdateRows);
    }

    // ── AddEnterScript ────────────────────────────────────────────────────

    [Fact]
    public void AddEnterScript_FromCatalogueEntry_AddsToEnterRows()
    {
        var vm = new ScriptEditorViewModel("Node 1", [], _ => { });
        vm.SelectedEnterEntry = MakeEntry("MyScript");
        vm.AddEnterScriptCommand.Execute(null);
        Assert.Single(vm.EnterRows);
    }

    [Fact]
    public void AddEnterScript_ClearsSelectionAndText()
    {
        var vm = new ScriptEditorViewModel("Node 1", [], _ => { });
        vm.SelectedEnterEntry = MakeEntry();
        vm.NewEnterText       = "something";
        vm.AddEnterScriptCommand.Execute(null);
        Assert.Null(vm.SelectedEnterEntry);
        Assert.Equal(string.Empty, vm.NewEnterText);
    }

    [Fact]
    public void AddEnterScript_FromRawText_AddsToEnterRows()
    {
        var vm = new ScriptEditorViewModel("Node 1", [], _ => { });
        vm.NewEnterText = "Void CustomScript()";
        vm.AddEnterScriptCommand.Execute(null);
        Assert.Single(vm.EnterRows);
    }

    [Fact]
    public void AddEnterScript_EmptyText_NoEntrySelected_DoesNothing()
    {
        var vm = new ScriptEditorViewModel("Node 1", [], _ => { });
        vm.NewEnterText = "   ";
        vm.AddEnterScriptCommand.Execute(null);
        Assert.Empty(vm.EnterRows);
    }

    // ── AddExitScript / AddUpdateScript ───────────────────────────────────

    [Fact]
    public void AddExitScript_AddsToExitRows()
    {
        var vm = new ScriptEditorViewModel("Node 1", [], _ => { });
        vm.SelectedExitEntry = MakeEntry("ExitScript");
        vm.AddExitScriptCommand.Execute(null);
        Assert.Single(vm.ExitRows);
        Assert.Empty(vm.EnterRows);
    }

    [Fact]
    public void AddUpdateScript_AddsToUpdateRows()
    {
        var vm = new ScriptEditorViewModel("Node 1", [], _ => { });
        vm.SelectedUpdateEntry = MakeEntry("UpdateScript");
        vm.AddUpdateScriptCommand.Execute(null);
        Assert.Single(vm.UpdateRows);
        Assert.Empty(vm.EnterRows);
    }

    // ── DeleteRow ─────────────────────────────────────────────────────────

    [Fact]
    public void DeleteRow_RemovesFromCorrectCategory()
    {
        var vm = new ScriptEditorViewModel("Node 1", [
            MakeCall("E1", ScriptCategory.Enter),
            MakeCall("X1", ScriptCategory.Exit)
        ], _ => { });
        var enterRow = vm.EnterRows[0];
        vm.DeleteRowCommand.Execute(enterRow);
        Assert.Empty(vm.EnterRows);
        Assert.Single(vm.ExitRows);
    }

    [Fact]
    public void DeleteRow_WithNull_DoesNothing()
    {
        var vm = new ScriptEditorViewModel("Node 1",
            [MakeCall("E1", ScriptCategory.Enter)], _ => { });
        vm.DeleteRowCommand.Execute(null);
        Assert.Single(vm.EnterRows);
    }

    // ── MoveUp ────────────────────────────────────────────────────────────

    [Fact]
    public void MoveUp_WithinCategory_MovesRowEarlier()
    {
        var vm = new ScriptEditorViewModel("Node 1", [
            MakeCall("E1", ScriptCategory.Enter),
            MakeCall("E2", ScriptCategory.Enter)
        ], _ => { });
        var row2 = vm.EnterRows[1];
        vm.MoveUpCommand.Execute(row2);
        Assert.Equal(row2, vm.EnterRows[0]);
    }

    [Fact]
    public void MoveUp_FirstRowInCategory_DoesNothing()
    {
        var vm = new ScriptEditorViewModel("Node 1", [
            MakeCall("E1", ScriptCategory.Enter),
            MakeCall("E2", ScriptCategory.Enter)
        ], _ => { });
        var first = vm.EnterRows[0];
        vm.MoveUpCommand.Execute(first);
        Assert.Equal(first, vm.EnterRows[0]);
    }

    // ── MoveDown ──────────────────────────────────────────────────────────

    [Fact]
    public void MoveDown_WithinCategory_MovesRowLater()
    {
        var vm = new ScriptEditorViewModel("Node 1", [
            MakeCall("E1", ScriptCategory.Enter),
            MakeCall("E2", ScriptCategory.Enter)
        ], _ => { });
        var first = vm.EnterRows[0];
        vm.MoveDownCommand.Execute(first);
        Assert.Equal(first, vm.EnterRows[1]);
    }

    [Fact]
    public void MoveDown_LastRowInCategory_DoesNothing()
    {
        var vm = new ScriptEditorViewModel("Node 1", [
            MakeCall("E1", ScriptCategory.Enter),
            MakeCall("E2", ScriptCategory.Enter)
        ], _ => { });
        var last = vm.EnterRows[1];
        vm.MoveDownCommand.Execute(last);
        Assert.Equal(last, vm.EnterRows[1]);
    }

    // ── Confirm ───────────────────────────────────────────────────────────

    [Fact]
    public void Confirm_CommitsAllCategoriesInOrder()
    {
        IReadOnlyList<ScriptCall>? committed = null;
        var vm = new ScriptEditorViewModel("Node 1", [
            MakeCall("E1",  ScriptCategory.Enter),
            MakeCall("X1",  ScriptCategory.Exit),
            MakeCall("U1",  ScriptCategory.Update)
        ], calls => committed = calls);
        vm.ConfirmCommand.Execute(null);
        Assert.NotNull(committed);
        Assert.Equal(3, committed.Count);
    }

    [Fact]
    public void Confirm_CommitsEmptyWhenNoRows()
    {
        IReadOnlyList<ScriptCall>? committed = null;
        var vm = new ScriptEditorViewModel("Node 1", [], calls => committed = calls);
        vm.ConfirmCommand.Execute(null);
        Assert.NotNull(committed);
        Assert.Empty(committed);
    }

    [Fact]
    public void Confirm_FiresConfirmedEvent()
    {
        var fired = false;
        var vm    = new ScriptEditorViewModel("Node 1", [], _ => { });
        vm.Confirmed += () => fired = true;
        vm.ConfirmCommand.Execute(null);
        Assert.True(fired);
    }

    // ── Cancel ────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_FiresCancelledEvent()
    {
        var fired = false;
        var vm    = new ScriptEditorViewModel("Node 1", [], _ => { });
        vm.Cancelled += () => fired = true;
        vm.CancelCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void Cancel_DoesNotCallCommit()
    {
        var commitCalled = false;
        var vm = new ScriptEditorViewModel("Node 1", [], _ => commitCalled = true);
        vm.CancelCommand.Execute(null);
        Assert.False(commitCalled);
    }

    // ── NodeViewModel convenience constructor ─────────────────────────────

    [Fact]
    public void NodeConstructor_ConfirmUpdatesNodeScripts()
    {
        var node   = new ConversationNode(1, false, SpeakerCategory.Npc, "", "", [], [], [],
                                          "Conversation", "None");
        var nodeVm = new NodeViewModel(node, new StringEntry(1, "text", ""));
        nodeVm.UndoStack = new DialogEditor.Core.Editing.UndoRedoStack();

        var editorVm = new ScriptEditorViewModel(nodeVm);
        editorVm.NewEnterText = "Void SomeScript()";
        editorVm.AddEnterScriptCommand.Execute(null);
        editorVm.ConfirmCommand.Execute(null);

        Assert.Single(nodeVm.Scripts);
        Assert.Equal(ScriptCategory.Enter, nodeVm.Scripts[0].Category);
    }
}
