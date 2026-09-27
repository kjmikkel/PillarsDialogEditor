using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DialogEditor.Patch.Schema;

namespace DialogEditor.Patch;

public static class DialogProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented              = true,
        PropertyNamingPolicy       = null,
        DefaultIgnoreCondition     = JsonIgnoreCondition.Never,
    };

    public static string Serialize(DialogProject project)
        => JsonSerializer.Serialize(project, Options);

    /// Parses, refuses newer / migrates older (SchemaMigrator, GitHub issues 62 + 104), then binds.
    public static DialogProject Deserialize(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidDataException("Project file is not a JSON object.");
        SchemaMigrator.Default.Migrate(root, SchemaFileKind.Project);
        return root.Deserialize<DialogProject>(Options)
               ?? throw new InvalidOperationException("Deserialised project was null.");
    }

    public static void SaveToFile(string path, DialogProject project)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Serialize(project), Encoding.UTF8);
    }

    public static DialogProject LoadFromFile(string path)
        => Deserialize(File.ReadAllText(path));
}
