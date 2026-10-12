namespace DialogEditor.Core.Analytics;

/// Both games start a conversation at node 0 unless a script names another node: PoE1's
/// and Deadfire's StartConversation default nodeID to 0 (issues 162, 172).
public static class StartNode
{
    public const int Id = 0;

    /// False only when the conversation has nodes and none of them is node 0. An empty
    /// conversation is fine: its first node will be node 0 (NodeIdAllocator).
    public static bool IsPresent(IEnumerable<int> nodeIds)
    {
        var any = false;
        foreach (var id in nodeIds)
        {
            if (id == Id) return true;
            any = true;
        }
        return !any;
    }
}
