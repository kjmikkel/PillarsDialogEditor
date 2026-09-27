using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DialogEditor.Patch.Schema;

namespace DialogEditor.Patch;

public static class PatchSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = null,                    // preserve PascalCase
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(ConversationPatch patch)
        => JsonSerializer.Serialize(patch, Options);

    /// Parses, refuses newer / migrates older (SchemaMigrator, GitHub issues 62 + 104), then binds.
    public static ConversationPatch Deserialize(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidDataException("Patch file is not a JSON object.");
        SchemaMigrator.Default.Migrate(root, SchemaFileKind.ConversationPatch);
        return root.Deserialize<ConversationPatch>(Options)
               ?? throw new InvalidOperationException("Deserialised patch was null.");
    }

    public static void SaveToFile(string path, ConversationPatch patch)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Serialize(patch), Encoding.UTF8);
    }

    public static ConversationPatch LoadFromFile(string path)
        => Deserialize(File.ReadAllText(path));
}
