using DialogEditor.Core.Localisation;
using System.Text.Json.Nodes;
using DialogEditor.Patch.Schema.Steps;

namespace DialogEditor.Patch.Schema;

/// The single place older files are brought forward and newer files refused (GitHub issues
/// 62 + 104). The three serializers call Default.Migrate on the parsed JSON before
/// deserialising, so no load path can bypass it.
///
/// To bump a format: raise the model's CurrentSchemaVersion, add ONE step here from the
/// old version, and add a fixture file at the old version under
/// DialogEditor.Tests/Patch/Schema/Fixtures. The registry test fails until the step exists.
[NotLocalised("Diagnostic messages: corrupt-file errors surface through the generic load-error text; registry problems are test output")]
public sealed class SchemaMigrator(
    IReadOnlyList<ISchemaMigrationStep> steps,
    IReadOnlyDictionary<SchemaFileKind, int> currentVersions)
{
    private const string VersionKey = "SchemaVersion";
    private const string PatchesKey = "Patches";

    public static IReadOnlyList<ISchemaMigrationStep> RegisteredSteps { get; } =
    [
        new ConversationPatchV1ToV2(),
    ];

    public static IReadOnlyDictionary<SchemaFileKind, int> CurrentVersions { get; } =
        new Dictionary<SchemaFileKind, int>
        {
            [SchemaFileKind.Project]           = DialogProject.CurrentSchemaVersion,
            [SchemaFileKind.ConversationPatch] = ConversationPatch.CurrentSchemaVersion,
            [SchemaFileKind.PatchList]         = PatchList.CurrentSchemaVersion,
        };

    public static SchemaMigrator Default { get; } = new(RegisteredSteps, CurrentVersions);

    /// Brings <paramref name="root"/> up to the current version in place. Throws
    /// UnsupportedSchemaVersionException when it (or, for a project, any nested patch) is
    /// newer than supported, and InvalidDataException when SchemaVersion is missing or < 1.
    /// A project's own steps run before its nested patches, so a project step may
    /// restructure "Patches" first.
    public void Migrate(JsonObject root, SchemaFileKind kind)
    {
        MigrateOne(root, kind, conversationName: null);

        if (kind == SchemaFileKind.Project && root[PatchesKey] is JsonObject patches)
            foreach (var (name, patch) in patches)
                if (patch is JsonObject patchObject)
                    MigrateOne(patchObject, SchemaFileKind.ConversationPatch, name);
    }

    private void MigrateOne(JsonObject root, SchemaFileKind kind, string? conversationName)
    {
        var current = currentVersions[kind];
        var found   = ReadVersion(root, kind);

        if (found > current)
            throw new UnsupportedSchemaVersionException(kind, found, current, conversationName);

        for (var v = found; v < current; v++)
        {
            var step = steps.Single(s => s.Kind == kind && s.FromVersion == v);
            step.Apply(root);
            root[VersionKey] = v + 1;
        }
    }

    private static int ReadVersion(JsonObject root, SchemaFileKind kind)
    {
        // Every save writes SchemaVersion, so missing / non-integer / < 1 means the file was
        // hand-edited or damaged. Reject rather than guess which version it is.
        if (root[VersionKey] is JsonValue value && value.TryGetValue<int>(out var version) && version >= 1)
            return version;
        throw new InvalidDataException(
            $"Not a valid {kind} file: '{VersionKey}' is missing or is not a whole number of at least 1.");
    }

    /// Registry invariants (GitHub issue 104): for each kind, versions 1 .. current-1 each have exactly
    /// one step, and no step starts at or past current. Checked by a unit test, not at runtime.
    public static IReadOnlyList<string> FindRegistryProblems(
        IReadOnlyList<ISchemaMigrationStep> steps,
        IReadOnlyDictionary<SchemaFileKind, int> currentVersions)
    {
        var problems = new List<string>();
        foreach (var (kind, current) in currentVersions)
        {
            var ofKind = steps.Where(s => s.Kind == kind).ToList();
            for (var v = 1; v < current; v++)
            {
                var count = ofKind.Count(s => s.FromVersion == v);
                if (count == 0) problems.Add($"{kind}: no step from version {v}");
                if (count > 1)  problems.Add($"{kind}: {count} steps from version {v}");
            }
            foreach (var s in ofKind.Where(s => s.FromVersion < 1 || s.FromVersion >= current))
                problems.Add($"{kind}: step from version {s.FromVersion} is outside 1..{current - 1}");
        }
        return problems;
    }
}
