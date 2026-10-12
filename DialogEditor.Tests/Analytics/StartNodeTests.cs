using DialogEditor.Core.Analytics;

namespace DialogEditor.Tests.Analytics;

/// Issue 172: both games start a conversation at node 0.
public class StartNodeTests
{
    [Fact]
    public void Present_WhenNode0Exists() => Assert.True(StartNode.IsPresent([3, 0, 7]));

    [Fact]
    public void Missing_WhenNodesExistButNoneIsNode0() => Assert.False(StartNode.IsPresent([1, 2]));

    // An empty conversation's first node will be node 0 (NodeIdAllocator), so nothing is missing.
    [Fact]
    public void Present_ForAnEmptyConversation() => Assert.True(StartNode.IsPresent([]));
}
