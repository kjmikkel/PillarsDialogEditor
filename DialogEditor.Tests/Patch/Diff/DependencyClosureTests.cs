using System.Collections.Generic;
using DialogEditor.Patch.Diff;

namespace DialogEditor.Tests.Patch.Diff;

public class DependencyClosureTests
{
    private static Dictionary<int, IReadOnlyList<int>> Edges(
        params (int From, int[] To)[] items) =>
        items.ToDictionary(i => i.From, i => (IReadOnlyList<int>)i.To);

    [Fact]
    public void SingleEdge_PullsTarget()
    {
        var result = DependencyClosure.Expand(1, Edges((1, [2])), new HashSet<int> { 2 });
        Assert.Equal(new HashSet<int> { 2 }, result);
    }

    [Fact]
    public void TransitiveChain_PullsAllReachableAddedTargets()
    {
        var result = DependencyClosure.Expand(1, Edges((1, [2]), (2, [3])), new HashSet<int> { 2, 3 });
        Assert.Equal(new HashSet<int> { 2, 3 }, result);
    }

    [Fact]
    public void TargetsNotInAddedIds_AreExcluded()
    {
        // 3 is a link target but not an added node → not pulled.
        var result = DependencyClosure.Expand(1, Edges((1, [2, 3])), new HashSet<int> { 2 });
        Assert.Equal(new HashSet<int> { 2 }, result);
    }

    [Fact]
    public void Cycle_Terminates()
    {
        var result = DependencyClosure.Expand(1, Edges((1, [2]), (2, [1])), new HashSet<int> { 1, 2 });
        Assert.Equal(new HashSet<int> { 2 }, result); // start (1) is never added to the result
    }

    [Fact]
    public void NoQualifyingTargets_ReturnsEmpty()
    {
        Assert.Empty(DependencyClosure.Expand(1, Edges((1, [])), new HashSet<int>()));
        Assert.Empty(DependencyClosure.Expand(1, new Dictionary<int, IReadOnlyList<int>>(), new HashSet<int> { 2 }));
    }
}
