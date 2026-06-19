using System.Text.Json;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe2GameDataBundleParser
{
    private record BundleRoot(List<BundleObject> GameDataObjects);
    private record BundleObject(string Id, string DebugName);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<GameDataEntry> Parse(
        string json,
        Func<string, string>? cleanName = null)
    {
        var root = JsonSerializer.Deserialize<BundleRoot>(json, Options);
        if (root is null) return [];

        return root.GameDataObjects
            .Where(o => !string.IsNullOrWhiteSpace(o.Id)
                     && !string.IsNullOrWhiteSpace(o.DebugName))
            .Select(o => new GameDataEntry(
                Id:   o.Id,
                Name: cleanName?.Invoke(o.DebugName) ?? o.DebugName))
            .ToList();
    }

    public static IReadOnlyList<GameDataEntry> ParseFile(
        string path,
        Func<string, string>? cleanName = null)
    {
        if (!File.Exists(path)) return [];
        var text = File.ReadAllText(path);
        return Parse(text, cleanName);
    }
}
