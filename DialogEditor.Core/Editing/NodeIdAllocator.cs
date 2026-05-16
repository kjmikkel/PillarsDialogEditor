namespace DialogEditor.Core.Editing;

public static class NodeIdAllocator
{
    public static int Next(IEnumerable<int> existingIds)
    {
        var ids = existingIds.ToList();
        return ids.Count == 0 ? 1 : ids.Max() + 1;
    }
}
