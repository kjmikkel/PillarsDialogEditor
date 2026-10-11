using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Issue 141: PoE1's link model has no conditions (only PoE2's does), so the detail pane
/// offers the link-conditions editor for PoE2 only and, for PoE1, says where a condition goes.
public class NodeDetailViewModelLinkConditionsTests
{
    public NodeDetailViewModelLinkConditionsTests() => Loc.Configure(new StubStringProvider());

    [Theory]
    [InlineData("poe2", true)]
    [InlineData("poe1", false)]
    public void SupportsLinkConditions_OnlyForPoe2(string gameId, bool expected)
    {
        var vm = new NodeDetailViewModel { ActiveGameId = gameId };
        Assert.Equal(expected, vm.SupportsLinkConditions);
    }

    [Theory]
    [InlineData("poe2", "ToolTip_LinkConditions_None")]
    [InlineData("poe1", "ToolTip_LinkConditions_Poe1")]
    public void LinkConditionsToolTip_ExplainsWhereThePoe1ConditionGoes(string gameId, string key)
    {
        var vm = new NodeDetailViewModel { ActiveGameId = gameId };
        Assert.Equal(key, vm.LinkConditionsToolTip);   // the stub echoes the key
    }

    [Fact]
    public void ChangingTheGame_RaisesBothProperties()
    {
        var vm      = new NodeDetailViewModel { ActiveGameId = "poe2" };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ActiveGameId = "poe1";

        Assert.Contains(nameof(vm.SupportsLinkConditions), changed);
        Assert.Contains(nameof(vm.LinkConditionsToolTip),  changed);
    }
}
