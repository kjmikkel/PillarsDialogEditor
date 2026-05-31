using System;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeDiffDetailViewModelTests
{
    public NodeDiffDetailViewModelTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void Changed_PopulatesBothDefaultSides()
    {
        var vm = new NodeDiffDetailViewModel(42, DiffStatus.Changed,
            defaultLeft: "old", defaultRight: "new", femaleLeft: "", femaleRight: "");

        Assert.Equal("old", vm.DefaultBefore);
        Assert.Equal("new", vm.DefaultAfter);
        Assert.False(vm.IsStructuralOnly);
        Assert.True(vm.ShowTextRows);
    }

    [Fact]
    public void Added_BeforeIsPlaceholder_AfterIsText()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Added,
            defaultLeft: "", defaultRight: "hello", femaleLeft: "", femaleRight: "");

        Assert.Equal("Diff_Detail_NodeAdded", vm.DefaultBefore);
        Assert.Equal("hello", vm.DefaultAfter);
    }

    [Fact]
    public void Removed_AfterIsPlaceholder_BeforeIsText()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Removed,
            defaultLeft: "goodbye", defaultRight: "", femaleLeft: "", femaleRight: "");

        Assert.Equal("goodbye", vm.DefaultBefore);
        Assert.Equal("Diff_Detail_NodeRemoved", vm.DefaultAfter);
    }

    [Fact]
    public void FemaleRow_Hidden_WhenBothFemaleEmpty()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed,
            defaultLeft: "a", defaultRight: "b", femaleLeft: "", femaleRight: "");

        Assert.False(vm.HasFemaleRow);
        Assert.False(vm.ShowFemaleRow);
    }

    [Fact]
    public void FemaleRow_Shown_WhenEitherSideHasFemale()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed,
            defaultLeft: "a", defaultRight: "b", femaleLeft: "", femaleRight: "elle");

        Assert.True(vm.HasFemaleRow);
        Assert.True(vm.ShowFemaleRow);
        Assert.Equal("", vm.FemaleBefore);
        Assert.Equal("elle", vm.FemaleAfter);
    }

    [Fact]
    public void StructuralOnly_True_WhenChangedButTextIdentical()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed,
            defaultLeft: "same", defaultRight: "same", femaleLeft: "f", femaleRight: "f");

        Assert.True(vm.IsStructuralOnly);
        Assert.False(vm.ShowTextRows);
    }

    [Fact]
    public void StructuralOnly_False_WhenFemaleDiffers()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed,
            defaultLeft: "same", defaultRight: "same", femaleLeft: "f1", femaleRight: "f2");

        Assert.False(vm.IsStructuralOnly);
        Assert.True(vm.HasFemaleRow);
    }
}
