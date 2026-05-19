using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class ConditionEditingTests
{
    private readonly NodeDetailViewModel _vm = new();

    public ConditionEditingTests()
    {
        Loc.Configure(new StubStringProvider());
    }

    private static NodeViewModel MakeNodeWithCondition()
    {
        var leaf = new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)",
            ["myFlag", "EqualTo", "1"], false, "And");
        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [],
            Conditions: [leaf], Scripts: [],
            DisplayType: "Conversation", Persistence: "None");
        return new NodeViewModel(node, new StringEntry(1, "text", ""));
    }

    private static NodeViewModel MakeNodeNoConditions()
    {
        var node = new ConversationNode(
            NodeId: 2, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [],
            Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None");
        return new NodeViewModel(node, new StringEntry(2, "text", ""));
    }

    // ── Load populates ConditionRows ──────────────────────────────────────

    [Fact]
    public void Load_NodeWithCondition_PopulatesConditionRows()
    {
        _vm.Load(MakeNodeWithCondition());
        Assert.Single(_vm.ConditionRows);
        Assert.Contains("IsGlobalValue", _vm.ConditionRows[0].FullName);
    }

    [Fact]
    public void Load_NodeNoConditions_EmptyConditionRows()
    {
        _vm.Load(MakeNodeNoConditions());
        Assert.Empty(_vm.ConditionRows);
    }

    [Fact]
    public void Clear_ResetsConditionRows()
    {
        _vm.Load(MakeNodeWithCondition());
        _vm.Clear();
        Assert.Empty(_vm.ConditionRows);
    }

    // ── Add condition ─────────────────────────────────────────────────────

    [Fact]
    public void AddCondition_AddsRowToConditionRows()
    {
        var nodeVm = MakeNodeNoConditions();
        nodeVm.UndoStack = new DialogEditor.Core.Editing.UndoRedoStack();
        _vm.Load(nodeVm);

        var entry = new ConditionEntry("IsGlobalValue", "Is Global Value", "Globals",
            ["poe1", "poe2"], "Tests a global flag.",
            [new ConditionParameter("Tag", "GlobalVariable", "Flag name", "MyFlag")]);
        _vm.AddCondition(entry);

        Assert.Single(_vm.ConditionRows);
        Assert.Equal("IsGlobalValue", _vm.ConditionRows[0].FullName);
    }

    // ── Delete condition ──────────────────────────────────────────────────

    [Fact]
    public void DeleteConditionRow_RemovesRow()
    {
        var nodeVm = MakeNodeWithCondition();
        nodeVm.UndoStack = new DialogEditor.Core.Editing.UndoRedoStack();
        _vm.Load(nodeVm);
        var row = _vm.ConditionRows[0];
        _vm.DeleteConditionRow(row);
        Assert.Empty(_vm.ConditionRows);
    }

    // ── ConditionBranch round-trip ────────────────────────────────────────

    [Fact]
    public void ConditionBranch_IsShownAsReadOnlyRow()
    {
        var branch = new ConditionBranch(
            [new ConditionLeaf("Boolean B()", [], false, "Or")],
            false, "And");
        var node = new ConversationNode(
            1, false, SpeakerCategory.Npc, "", "", [],
            [new ConditionLeaf("Boolean A()", [], false, "And"), branch],
            [], "Conversation", "None");
        var nodeVm = new NodeViewModel(node, new StringEntry(1, "text", ""));
        _vm.Load(nodeVm);

        Assert.Equal(2, _vm.ConditionRows.Count);
        Assert.True(_vm.ConditionRows[0].IsLeaf);
        Assert.True(_vm.ConditionRows[1].IsBranch);
    }

    [Fact]
    public void ConditionBranch_PreservedAfterEditConfirm()
    {
        var leaf   = new ConditionLeaf("Boolean A()", [], false, "And");
        var branch = new ConditionBranch(
            [new ConditionLeaf("Boolean B()", [], false, "Or")],
            false, "And");
        var node = new ConversationNode(
            1, false, SpeakerCategory.Npc, "", "", [],
            [leaf, branch], [], "Conversation", "None");
        var nodeVm = new NodeViewModel(node, new StringEntry(1, "text", ""));
        nodeVm.UndoStack = new DialogEditor.Core.Editing.UndoRedoStack();

        var editorVm = new ConditionEditorViewModel(nodeVm);
        editorVm.ConfirmCommand.Execute(null);

        Assert.Equal(2, nodeVm.Conditions.Count);
        Assert.IsType<ConditionLeaf>(nodeVm.Conditions[0]);
        Assert.IsType<ConditionBranch>(nodeVm.Conditions[1]);
    }

    // ── CommitConditions ──────────────────────────────────────────────────

    [Fact]
    public void CommitConditions_UpdatesNodeViewModelConditions()
    {
        var nodeVm = MakeNodeNoConditions();
        nodeVm.UndoStack = new DialogEditor.Core.Editing.UndoRedoStack();
        _vm.Load(nodeVm);

        var entry = new ConditionEntry("IsInCombat", "Is In Combat", "General",
            ["poe1", "poe2"], "True if in combat.", []);
        _vm.AddCondition(entry);

        Assert.Single(nodeVm.Conditions);
        Assert.IsType<ConditionLeaf>(nodeVm.Conditions[0]);
    }
}
