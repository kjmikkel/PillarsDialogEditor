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
///
/// It also carries the names of the two packs involved (issue #6): the conflict itself
/// only knows their positions in the load order, and a row that says what clashes but
/// not between whom leaves the user to guess which entry to move.
/// </summary>
public sealed class PatchConflictRowViewModel(
    PatchConflict conflict, string firstProjectName, string secondProjectName)
{
    /// The conflict this row renders. PatchManagerViewModel reads the patch indices off
    /// it to flag the entries involved.
    public PatchConflict Conflict => conflict;

    public string ConversationName => conflict.ConversationName;
    public int    NodeId           => conflict.NodeId;
    public string FirstProjectName  => firstProjectName;
    public string SecondProjectName => secondProjectName;

    /// The field the two patches disagree about — or, for the kinds with no single field
    /// to name, a localised marker: "(deleted)", "(added by both)", "link to node N" or "text (fr)".
    /// PatchConflict.FieldName is null for those; it used to carry the English "(deleted)"
    /// itself, which made the de-duplication key depend on the UI language.
    public string FieldLabel => conflict.Kind switch
    {
        PatchConflictKind.Deletion  => Loc.Get("PatchManager_DeletedField"),
        PatchConflictKind.AddedNode => Loc.Get("PatchManager_AddedNodeField"),
        PatchConflictKind.Link      => Loc.Format("PatchManager_LinkField", conflict.LinkToNodeId ?? 0),
        PatchConflictKind.Text      => Loc.Format("PatchManager_TextField", conflict.Language ?? string.Empty),
        _                           => conflict.FieldName ?? string.Empty,
    };

    /// The whole row as one sentence. The view used to assemble this from a hard-coded
    /// MultiBinding StringFormat, which put the word "conversation" and the separators
    /// out of a translator's reach — and word order differs between languages, so the
    /// line has to be a single resource rather than three bindings glued together.
    public string Description =>
        Loc.Format("PatchManager_ConflictRow", ConversationName, NodeId, FieldLabel,
                   FirstProjectName, SecondProjectName);
}
