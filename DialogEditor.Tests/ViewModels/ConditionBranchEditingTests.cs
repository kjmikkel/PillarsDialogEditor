using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class ConditionBranchEditingTests
{
    private static ConditionLeaf MakeLeaf(string flag = "myFlag") =>
        new("Boolean IsGlobalValue(String, Operator, Int32)",
            [flag, "EqualTo", "1"], false, "And");

    private static ConditionBranch MakeBranch(
        IReadOnlyList<ConditionNode>? components = null,
        bool not = false,
        string op = "And") =>
        new(components ?? [], not, op);

    // ── DisplayName reflects branch ───────────────────────────────────────

    [Fact]
    public void BranchRow_DisplayName_ReflectsBranchFormat()
    {
        var branch = MakeBranch([MakeLeaf()]);
        var row    = new ConditionRowViewModel(branch);
        Assert.Equal(branch.Format(), row.DisplayName);
    }

    // ── UpdateBranchComponents ────────────────────────────────────────────

    [Fact]
    public void UpdateBranchComponents_ChangesDisplayName()
    {
        var row = new ConditionRowViewModel(MakeBranch());
        var before = row.DisplayName;

        row.UpdateBranchComponents([MakeLeaf("updated")]);

        Assert.NotEqual(before, row.DisplayName);
        Assert.Contains("IsGlobalValue", row.DisplayName);
    }

    [Fact]
    public void UpdateBranchComponents_ToNode_ReturnsUpdatedBranch()
    {
        var row = new ConditionRowViewModel(MakeBranch());
        row.UpdateBranchComponents([MakeLeaf(), MakeLeaf("second")]);

        var node = (ConditionBranch)row.ToNode();
        Assert.Equal(2, node.Components.Count);
    }

    [Fact]
    public void UpdateBranchComponents_PreservesNotAndOperator()
    {
        var row = new ConditionRowViewModel(MakeBranch(not: true, op: "Or"));
        row.UpdateBranchComponents([MakeLeaf()]);

        var node = (ConditionBranch)row.ToNode();
        Assert.True(node.Not);
        Assert.Equal("Or", node.Operator);
    }

    // ── AddGroupCommand ───────────────────────────────────────────────────

    [Fact]
    public void AddGroup_AddsRowToConditionEditor()
    {
        var vm = new ConditionEditorViewModel("Test", [], _ => {});
        vm.AddGroupCommand.Execute(null);
        Assert.Single(vm.Rows);
    }

    [Fact]
    public void AddGroup_NewRow_IsBranch_WithEmptyComponents()
    {
        var vm = new ConditionEditorViewModel("Test", [], _ => {});
        vm.AddGroupCommand.Execute(null);

        var row = vm.Rows[0];
        Assert.True(row.IsBranch);
        Assert.Empty(row.BranchComponents);
    }
}
