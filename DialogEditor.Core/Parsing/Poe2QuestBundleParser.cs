using System.Text.Json;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe2QuestBundleParser
{
    private record QuestBundleRoot(List<QuestEntry> Quests);
    private record QuestEntry(string Id, string Filename);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<GameDataEntry> Parse(string json)
    {
        var root = JsonSerializer.Deserialize<QuestBundleRoot>(json, Options);
        if (root is null) return [];

        return root.Quests
            .Where(q => !string.IsNullOrWhiteSpace(q.Id)
                     && !string.IsNullOrWhiteSpace(q.Filename))
            .Select(q => new GameDataEntry(
                Id:   q.Id,
                Name: Path.GetFileNameWithoutExtension(q.Filename)))
            .ToList();
    }

    public static IReadOnlyList<GameDataEntry> ParseFile(string path)
    {
        if (!File.Exists(path)) return [];
        var text = File.ReadAllText(path, new System.Text.UTF8Encoding(true));
        return Parse(text);
    }
}
