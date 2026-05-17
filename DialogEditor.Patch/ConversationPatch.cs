using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

// A single field value change — both sides stored as JSON-encoded strings so the
// format is uniform regardless of whether the field is a string, bool, or float.
public record FieldChange(string From, string To);

public record DeletedLink(int ToNodeId, bool HasConditions);

public record NodeModification(
    int                                      NodeId,
    IReadOnlyDictionary<string, FieldChange> FieldChanges,
    IReadOnlyList<LinkEditSnapshot>          AddedLinks,
    IReadOnlyList<DeletedLink>               DeletedLinks);

public record ConversationPatch(
    string                          ConversationName,
    int                             SchemaVersion,
    IReadOnlyList<NodeEditSnapshot> AddedNodes,
    IReadOnlyList<int>              DeletedNodeIds,
    IReadOnlyList<NodeModification> ModifiedNodes)
{
    public static readonly int CurrentSchemaVersion = 1;

    public bool IsEmpty =>
        AddedNodes.Count == 0 &&
        DeletedNodeIds.Count == 0 &&
        ModifiedNodes.Count == 0;
}
