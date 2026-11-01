using System.Text.Json.Nodes;

namespace DialogEditor.Patch.Schema.Steps;

/// ConversationPatch v4 (GitHub issue 359) adds the optional ModifiedNodes[].UpdatedLinkOrder, the
/// target node ids of a node's links in their new order. A v3 patch has no such field and means
/// exactly what it did, so this step changes nothing but the version number the migrator stamps.
///
/// Why the version is bumped anyway: the field is optional, but an older editor that read a v4
/// patch would drop it silently and apply the links in the wrong order. With the bump it refuses
/// the file instead, as for earlier format changes.
public sealed class ConversationPatchV3ToV4 : ISchemaMigrationStep
{
    public SchemaFileKind Kind        => SchemaFileKind.ConversationPatch;
    public int            FromVersion => 3;

    public void Apply(JsonObject root) { }
}
