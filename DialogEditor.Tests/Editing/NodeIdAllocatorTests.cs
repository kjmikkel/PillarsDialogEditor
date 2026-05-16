using DialogEditor.Core.Editing;

namespace DialogEditor.Tests.Editing;

public class NodeIdAllocatorTests
{
    [Fact]
    public void Next_EmptyList_ReturnsOne()
    {
        Assert.Equal(1, NodeIdAllocator.Next([]));
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
}
