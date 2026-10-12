using DialogEditor.Core.Editing;

namespace DialogEditor.Tests.Editing;

public class NodeIdAllocatorTests
{
    // Issue 162: both games start a conversation at node 0 unless told otherwise, so a new
    // conversation's first node must be 0.
    [Fact]
    public void Next_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, NodeIdAllocator.Next([]));
    }

    [Fact]
    public void Next_ReturnMaxPlusOne()
    {
        Assert.Equal(6, NodeIdAllocator.Next([1, 3, 5]));
    }

    [Fact]
    public void Next_SingleElement_ReturnsPlusOne()
    {
        Assert.Equal(8, NodeIdAllocator.Next([7]));
    }

    [Fact]
    public void Next_SkipsReservedIds()
    {
        // A _vo/ file may exist for a deleted node's ID; reusing the ID would
        // silently attach the old audio to the new node (B-005 family).
        var next = NodeIdAllocator.Next([1, 2, 3], isReserved: id => id is 4 or 5);
        Assert.Equal(6, next);
    }
}
