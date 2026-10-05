using System.Text.Json.Serialization;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
#pragma warning disable CS8618 // non-nullable property set in [JsonConstructor]

namespace DialogEditor.Patch;

// A single field value change — both sides stored as JSON-encoded strings so the
// format is uniform regardless of whether the field is a string, bool, or float.
public record FieldChange(string From, string To);

public record DeletedLink(int ToNodeId, bool HasConditions);

public record ModifiedLink(
    int    ToNodeId,
    int    RandomWeight,
    string QuestionNodeTextDisplay,
    IReadOnlyList<ConditionNode>? Conditions = null);

// NodeModification is a class (not a positional record) so System.Text.Json can
// unambiguously select the [JsonConstructor]-annotated 5-parameter overload while
// the 4-parameter convenience constructor remains available for test code.
public sealed class NodeModification
{
    [JsonConstructor]
    public NodeModification(
        int                                      nodeId,
        IReadOnlyDictionary<string, FieldChange> fieldChanges,
        IReadOnlyList<LinkEditSnapshot>          addedLinks,
        IReadOnlyList<DeletedLink>               deletedLinks,
        IReadOnlyList<ModifiedLink>              modifiedLinks)
    {
        NodeId        = nodeId;
        FieldChanges  = fieldChanges;
        AddedLinks    = addedLinks;
        DeletedLinks  = deletedLinks;
        ModifiedLinks = modifiedLinks;
    }

    public NodeModification(
        int NodeId,
        IReadOnlyDictionary<string, FieldChange> FieldChanges,
        IReadOnlyList<LinkEditSnapshot> AddedLinks,
        IReadOnlyList<DeletedLink> DeletedLinks)
        : this(NodeId, FieldChanges, AddedLinks, DeletedLinks, []) { }

    // Conditions / Scripts changed — store full new lists (replace-all semantics)
    public IReadOnlyList<ConditionNode>? UpdatedConditions { get; init; }
    public IReadOnlyList<ScriptCall>?    UpdatedScripts    { get; init; }

    public int                                      NodeId        { get; }
    public IReadOnlyDictionary<string, FieldChange> FieldChanges  { get; }
    public IReadOnlyList<LinkEditSnapshot>          AddedLinks    { get; }
    public IReadOnlyList<DeletedLink>               DeletedLinks  { get; }
    public IReadOnlyList<ModifiedLink>              ModifiedLinks { get; }
}

public record ConversationPatch(
    string                          ConversationName,
    int                             SchemaVersion,
    IReadOnlyList<NodeEditSnapshot> AddedNodes,
    IReadOnlyList<int>              DeletedNodeIds,
    IReadOnlyList<NodeModification> ModifiedNodes)
{
    public static readonly int CurrentSchemaVersion = 2;

    // key = language code e.g. "en", "fr", "de"
    public IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> Translations { get; init; }
        = new Dictionary<string, IReadOnlyList<NodeTranslation>>();

    // key = node ID; language-neutral translator context
    public IReadOnlyDictionary<int, string> NodeComments { get; init; }
        = new Dictionary<int, string>();

    /// A freshly diffed patch only knows the one language it was diffed in. Every
    /// other language's translations come over from <paramref name="prior"/> (the patch
    /// this one replaces), or a re-diff would silently erase imported translations.
    /// <paramref name="language"/> always keeps this patch's value, including "no
    /// entry" when its text was reverted to vanilla.
    public ConversationPatch CarryingTranslationsFrom(ConversationPatch? prior, string language)
    {
        if (prior is null || prior.Translations.Count == 0) return this;

        var merged = new Dictionary<string, IReadOnlyList<NodeTranslation>>(prior.Translations);
        merged.Remove(language);
        foreach (var (lang, entries) in Translations)
            merged[lang] = entries;
        return this with { Translations = merged };
    }

    public bool IsEmpty =>
        AddedNodes.Count     == 0 &&
        DeletedNodeIds.Count == 0 &&
        ModifiedNodes.Count  == 0 &&
        Translations.Count   == 0 &&
        NodeComments.Count   == 0;
}
