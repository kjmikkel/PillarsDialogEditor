namespace DialogEditor.Patch.Diff;

public record ConversationChange(
    string             Name,
    IReadOnlyList<int> Added,
    IReadOnlyList<int> Removed,
    IReadOnlyList<int> Modified)
{
    public int  AddedCount    => Added.Count;
    public int  RemovedCount  => Removed.Count;
    public int  ModifiedCount => Modified.Count;
    public bool HasChanges    => Added.Count + Removed.Count + Modified.Count > 0;
}
