using System.Text.Json.Nodes;

namespace DialogEditor.Patch.Schema;

/// Rewrites one file kind's raw JSON from FromVersion to FromVersion + 1 (issue #104).
/// Steps work on JSON rather than records because an old shape may no longer bind to
/// today's records: a renamed field would be silently dropped by the deserializer before
/// a post-deserialise step could move it.
/// Contract: pure (no I/O, deterministic) and scoped to its own document. Nested patches
/// inside a project are migrated separately by SchemaMigrator. Don't set SchemaVersion;
/// the migrator does that after Apply.
public interface ISchemaMigrationStep
{
    SchemaFileKind Kind        { get; }
    int            FromVersion { get; }
    void Apply(JsonObject root);
}
