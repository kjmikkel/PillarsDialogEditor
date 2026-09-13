using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

/// <summary>
/// One row of the Patch Manager's conflict list.
///
/// It exists because PatchConflict has nowhere to put words: the record is the identity
/// of a conflict (ConflictDetector de-duplicates on its fields) as much as its content,
/// and DialogEditor.Patch references only Core, so it cannot reach Loc. Every string the
/// conflict list shows is therefore resolved here, at the first layer allowed to speak a
/// language. Mirrors ConflictRowViewModel for git merges.
/// </summary>
public sealed class PatchConflictRowViewModel(PatchConflict conflict)
{
    /// The conflict this row renders. PatchManagerViewModel reads the patch indices off
    /// it to flag the entries involved.
    public PatchConflict Conflict => conflict;

    public string ConversationName => conflict.ConversationName;
    public int    NodeId           => conflict.NodeId;

    /// The field the two patches disagree about — or, for a delete-vs-modify conflict,
    /// the localised "(deleted)" marker. PatchConflict.FieldName is null there because
    /// there is no single field to name; it used to carry the English marker itself,
    /// which made the de-duplication key depend on the UI language.
    public string FieldLabel => conflict.FieldName ?? Loc.Get("PatchManager_DeletedField");

    /// The whole row as one sentence. The view used to assemble this from a hard-coded
    /// MultiBinding StringFormat, which put the word "conversation" and the separators
    /// out of a translator's reach — and word order differs between languages, so the
    /// line has to be a single resource rather than three bindings glued together.
    public string Description =>
        Loc.Format("PatchManager_ConflictRow", ConversationName, NodeId, FieldLabel);
}
