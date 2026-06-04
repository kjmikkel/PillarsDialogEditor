namespace DialogEditor.Patch.GitConflict;

public enum MergeConflictKind
{
    FieldEdit,        // same (node, field) set to different values on each side → field-level merge
    TranslationEdit,  // same (node, language) localized text differs → field-level merge on text
    DeleteVsEdit,     // one side deletes a node the other modifies/adds
    NodeAddAdd,       // both sides add the same NodeId with different content
    ConversationLevel // whole-conversation divergence not reducible to the above
}

public enum MergeSide { Mine, Theirs }

/// One resolvable conflict between the mine and theirs projects.
/// Value fields are display strings (for FieldEdit, the JSON-encoded `To` values;
/// for TranslationEdit, the differing localized text).
public record MergeConflict(
    MergeConflictKind Kind,
    string            ConversationName,
    int               NodeId,        // -1 when not node-scoped (ConversationLevel)
    string?           FieldName,     // FieldEdit: field name; TranslationEdit: language code
    string            MineValue,
    string            TheirsValue)
{
    /// Sentinel placed in MineValue/TheirsValue for the side that deletes a node
    /// in a DeleteVsEdit conflict.
    public const string DeletedMarker = "(deleted)";

    /// Female-variant text for a TranslationEdit conflict (mine side).
    /// Empty for every other conflict kind. Display-only: the merge replaces
    /// the whole NodeTranslation regardless of which sub-field differs.
    public string MineFemaleValue { get; init; } = "";

    /// Female-variant text for a TranslationEdit conflict (theirs side). See MineFemaleValue.
    public string TheirsFemaleValue { get; init; } = "";
}
