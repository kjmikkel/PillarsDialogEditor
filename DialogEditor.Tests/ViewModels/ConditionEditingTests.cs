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

    // ── Labeled enum (Options = display labels, Values = stored GUIDs) ───

    private static ConditionEntry MakeFactionEntry() => new(
        "IsReputation", "Is Reputation", "Faction", ["poe2"],
        "Tests faction reputation.",
        [
            new ConditionParameter(
                "Faction", "GameData", "Faction to check.", "Huana",
                Options: ["Huana", "Royal Deadfire Company"],
                Values:  ["aaaa-guid", "bbbb-guid"]),
            new ConditionParameter("Rank Type", "Enum:RankType", "", "Good",
                Options: ["Default", "Good", "Bad", "Mixed"]),
            new ConditionParameter("Rank Value", "Int32", "", "0"),
            new ConditionParameter("Operator", "Operator", "", "GreaterThanOrEqualTo"),
        ]);

    [Fact]
    public void EffectiveValue_WithoutValues_ReturnsSameAsValue()
    {
        var pvm = new ParameterValueViewModel
        {
            Name = "Tag", Type = "String", Description = "", Options = null, Value = "myFlag"
        };
        Assert.Equal("myFlag", pvm.EffectiveValue);
    }

    [Fact]
    public void EffectiveValue_WithLabeledOptions_ReturnsCorrespondingStoredValue()
    {
        var pvm = new ParameterValueViewModel
        {
            Name    = "Faction",
            Type    = "GameData",
            Description = "",
            Options = ["Huana", "Royal Deadfire Company"],
            Values  = ["aaaa-guid", "bbbb-guid"],
            Value   = "Huana",
        };
        Assert.Equal("aaaa-guid", pvm.EffectiveValue);
    }

    [Fact]
    public void EffectiveValue_UnrecognizedLabel_ReturnsFallback()
    {
        var pvm = new ParameterValueViewModel
        {
            Name    = "Faction",
            Type    = "GameData",
            Description = "",
            Options = ["Huana"],
            Values  = ["aaaa-guid"],
            Value   = "unknown-faction",
        };
        Assert.Equal("unknown-faction", pvm.EffectiveValue);
    }

    [Fact]
    public void LoadFromLeaf_StoredGuid_DisplaysLabel()
    {
        var leaf = new ConditionLeaf("Boolean IsReputation(Guid, RankType, Int32, Operator)",
            ["aaaa-guid", "Good", "0", "GreaterThanOrEqualTo"], false, "And");
        var row = new ConditionRowViewModel(leaf, MakeFactionEntry());
        // First parameter stored as GUID should be shown as its label
        Assert.Equal("Huana", row.Parameters[0].Value);
    }

    [Fact]
    public void ToLeaf_LabeledEnum_StoredValueIsGuid()
    {
        var leaf = new ConditionLeaf("Boolean IsReputation(Guid, RankType, Int32, Operator)",
            ["aaaa-guid", "Good", "0", "GreaterThanOrEqualTo"], false, "And");
        var row = new ConditionRowViewModel(leaf, MakeFactionEntry());
        // Round-trip: editing nothing, serialising back should preserve the GUID
        var serialised = row.ToLeaf();
        Assert.Equal("aaaa-guid", serialised.Parameters[0]);
    }

    [Fact]
    public void ToLeaf_NonLabeledParam_StoredValueUnchanged()
    {
        var leaf = new ConditionLeaf("Boolean IsReputation(Guid, RankType, Int32, Operator)",
            ["aaaa-guid", "Good", "0", "GreaterThanOrEqualTo"], false, "And");
        var row = new ConditionRowViewModel(leaf, MakeFactionEntry());
        var serialised = row.ToLeaf();
        // Non-labeled params are stored as-is
        Assert.Equal("Good", serialised.Parameters[1]);
        Assert.Equal("0",    serialised.Parameters[2]);
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
