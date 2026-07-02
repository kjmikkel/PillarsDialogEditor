namespace DialogEditor.Core.Editing;

public static class NodeIdAllocator
{
    /// <param name="isReserved">
    /// Optional veto for otherwise-free IDs. Used to skip IDs whose _vo/ VO file
    /// still exists from a deleted node, so new nodes never silently inherit audio.
    /// </param>
    public static int Next(IEnumerable<int> existingIds, Func<int, bool>? isReserved = null)
    {
        var ids = existingIds.ToList();
        var candidate = ids.Count == 0 ? 1 : ids.Max() + 1;
        while (isReserved?.Invoke(candidate) == true) candidate++;
        return candidate;
    }
}
