using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DialogEditor.Patch.Schema;

namespace DialogEditor.Patch;

public static class PatchListSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static void SaveToFile(string path, PatchList list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(list, Options), Encoding.UTF8);
    }

    public static PatchList LoadFromFile(string path) => Deserialize(File.ReadAllText(path));

    /// Parses, refuses newer / migrates older (SchemaMigrator, GitHub issues 62 + 104), then binds.
    public static PatchList Deserialize(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidDataException("Load-order file is not a JSON object.");
        SchemaMigrator.Default.Migrate(root, SchemaFileKind.PatchList);
        return root.Deserialize<PatchList>(Options)
               ?? throw new InvalidOperationException("Deserialised PatchList was null.");
    }

    /// Resolves the project path from a PatchListEntry.
    /// Tries relative (anchored to the .patchlist file's directory) first;
    /// falls back to the stored absolute path if the relative one doesn't exist.
    public static string ResolvePath(string patchlistPath, PatchListEntry entry)
    {
        var dir      = Path.GetDirectoryName(Path.GetFullPath(patchlistPath)) ?? string.Empty;
        var relative = Path.GetFullPath(Path.Combine(dir, entry.RelativePath));
        return File.Exists(relative) ? relative : entry.AbsolutePath;
    }

    /// Builds a PatchListEntry for a project file, computing the relative path
    /// from a given .patchlist file location.
    public static PatchListEntry BuildEntry(string patchlistPath, string projectPath)
    {
        var patchlistDir = Path.GetDirectoryName(Path.GetFullPath(patchlistPath)) ?? string.Empty;
        var relative     = Path.GetRelativePath(patchlistDir, projectPath);
        return new PatchListEntry(relative, Path.GetFullPath(projectPath));
    }
}
