using System.Text.Json.Nodes;

namespace DialogEditor.Patch.Schema.Steps;

/// ConversationPatch v2 (commit 01b2f49) added Translations and NodeComments. Both
/// default to empty when absent, so a v1 patch needs no rewrite; the step exists so
/// the chain has no gap and FindRegistryProblems stays meaningful.
public sealed class ConversationPatchV1ToV2 : ISchemaMigrationStep
{
    public SchemaFileKind Kind        => SchemaFileKind.ConversationPatch;
    public int            FromVersion => 1;
    public void Apply(JsonObject root) { }
}
