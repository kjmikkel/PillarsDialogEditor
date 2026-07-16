using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.ViewModels;

public class ConditionSearchViewModelTests
{
    public ConditionSearchViewModelTests() => Loc.Configure(new StubStringProvider());

    private static NodeEditSnapshot Node(int id, params ConditionNode[] conds) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false, [], conds, []);

    private static ConditionLeaf Disp(string axis) =>
        new("Boolean DispositionEqual(Axis, Rank)", new[] { axis, "2" }, false, "And");

    [Fact]
    public void Search_EntrySelected_NoPins_HighlightsAllUsers()
    {
        var snap = new ConversationEditSnapshot(new[] { Node(0, Disp("Benevolent")), Node(1) });

        IReadOnlySet<int>? applied = null;
        var vm = new ConditionSearchViewModel("poe1",
            () => snap, m => applied = m, () => applied = null);

        vm.SelectedEntry = vm.Entries.First(e => e.ReflectionFullName == "Boolean DispositionEqual(Axis, Rank)");
        vm.SearchCommand.Execute(null);

        Assert.NotNull(applied);
        Assert.Contains(0, applied!);
        Assert.DoesNotContain(1, applied!);
    }

    [Fact]
    public void Search_WithPin_NarrowsToMatchingValue()
    {
        var snap = new ConversationEditSnapshot(new[] { Node(0, Disp("Benevolent")), Node(1, Disp("Cruel")) });

        IReadOnlySet<int>? applied = null;
        var vm = new ConditionSearchViewModel("poe1", () => snap, m => applied = m, () => applied = null);
        vm.SelectedEntry = vm.Entries.First(e => e.ReflectionFullName == "Boolean DispositionEqual(Axis, Rank)");
        vm.PinRows[0].Value = "Benevolent";   // pin the Axis parameter
        vm.SearchCommand.Execute(null);

        Assert.Contains(0, applied!);
        Assert.DoesNotContain(1, applied!);
    }

    [Fact]
    public void Clear_InvokesClearHighlight()
    {
        var cleared = false;
        var vm = new ConditionSearchViewModel("poe1",
            () => new ConversationEditSnapshot([]), _ => { }, () => cleared = true);
        vm.ClearCommand.Execute(null);
        Assert.True(cleared);
    }
}
