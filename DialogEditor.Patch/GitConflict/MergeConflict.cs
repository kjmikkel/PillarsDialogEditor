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

/// How much one side of a whole-conversation conflict changed. Reported as counts
/// rather than as a rendered summary because DialogEditor.Patch references only Core
/// and so cannot reach Loc — ConflictRowViewModel formats these for display.
public record PatchCounts(int AddedNodes, int ModifiedNodes, int DeletedNodes, int TextChanges);

/// One resolvable conflict between the mine and theirs projects.
/// Value fields are display strings (for FieldEdit, the JSON-encoded `To` values;
/// for TranslationEdit, the differing localized text; for DeleteVsEdit, the editing
/// side's touched fields — the deleting side is empty and flagged by DeletedSide).
public record MergeConflict(
    MergeConflictKind Kind,
    string            ConversationName,
    int               NodeId,        // -1 when not node-scoped (ConversationLevel)
    string?           FieldName,     // FieldEdit: field name; TranslationEdit: language code
    string            MineValue,
    string            TheirsValue)
{
    /// Which side deleted the node in a DeleteVsEdit conflict; null on every other kind.
    /// This used to be a "(deleted)" string placed in that side's value, which made one
    /// literal both the text the dialog showed and the sentinel MergeBuilder compared
    /// against — so it could not be translated without making the merge
    /// language-dependent. The side is now reported as data and ConflictRowViewModel
    /// renders the label, as with PatchCounts below.
    public MergeSide? DeletedSide { get; init; }

    /// Set only on a ConversationLevel conflict, where the two sides are too broad to
    /// show as values and are summarised instead. Null on every other kind, which is
    /// what tells ConflictRowViewModel to pass MineValue/TheirsValue through as-is.
    public PatchCounts? MineCounts   { get; init; }
    public PatchCounts? TheirsCounts { get; init; }

    /// Female-variant text for a TranslationEdit conflict (mine side).
    /// Empty for every other conflict kind. Display-only: the merge replaces
    /// the whole NodeTranslation regardless of which sub-field differs.
    public string MineFemaleValue { get; init; } = "";

    /// Female-variant text for a TranslationEdit conflict (theirs side). See MineFemaleValue.
    public string TheirsFemaleValue { get; init; } = "";
}
