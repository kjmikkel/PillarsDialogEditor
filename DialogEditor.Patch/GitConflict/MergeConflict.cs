namespace DialogEditor.Patch.GitConflict;

public enum MergeConflictKind
{
    FieldEdit,        // same (node, field) set to different values on each side → field-level merge
    DeleteVsEdit,     // one side deletes a node the other modifies/adds
    NodeAddAdd,       // both sides add the same NodeId with different content
    ConversationLevel // whole-conversation divergence not reducible to the above
}

public enum MergeSide { Mine, Theirs }

/// One resolvable conflict between the mine and theirs projects.
/// Value fields are JSON-encoded strings (same encoding as FieldChange) for display.
public record MergeConflict(
    MergeConflictKind Kind,
    string            ConversationName,
    int               NodeId,        // -1 when not node-scoped (ConversationLevel)
    string?           FieldName,     // set only for FieldEdit
    string            MineValue,
    string            TheirsValue)
{
    /// Sentinel placed in MineValue/TheirsValue for the side that deletes a node
    /// in a DeleteVsEdit conflict.
    public const string DeletedMarker = "(deleted)";
}
