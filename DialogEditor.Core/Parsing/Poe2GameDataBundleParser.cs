using System.Text.Json;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe2GameDataBundleParser
{
    private record BundleRoot(List<BundleObject> GameDataObjects);

    private record BundleObject(
        [property: System.Text.Json.Serialization.JsonPropertyName("$type")] string? DataType,
        string Id,
        string DebugName);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <param name="typeFilter">
    /// Optional substring match against each object's <c>$type</c> field (case-insensitive).
    /// Pass <see langword="null"/> to return all objects regardless of type — the behaviour
    /// used for single-kind bundles like <c>items.gamedatabundle</c>.
    /// Pass e.g. <c>"RaceGameData"</c> to extract only races from a mixed bundle such as
    /// <c>characters.gamedatabundle</c>.
    /// </param>
    public static IReadOnlyList<GameDataEntry> Parse(
        string json,
        Func<string, string>? cleanName = null,
        string? typeFilter = null)
    {
        var root = JsonSerializer.Deserialize<BundleRoot>(json, Options);
        if (root is null) return [];

        return root.GameDataObjects
            .Where(o => !string.IsNullOrWhiteSpace(o.Id)
                     && !string.IsNullOrWhiteSpace(o.DebugName)
                     && MatchesTypeFilter(o.DataType, typeFilter))
            .Select(o => new GameDataEntry(
                Id:   o.Id,
                Name: cleanName?.Invoke(o.DebugName) ?? o.DebugName))
            // Drop entries whose display name became empty after the clean transform —
            // an empty Name produces " — <guid>" suggestions that crash the AutoCompleteBox.
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .ToList();
    }

    public static IReadOnlyList<GameDataEntry> ParseFile(
        string path,
        Func<string, string>? cleanName = null,
        string? typeFilter = null)
    {
        if (!File.Exists(path)) return [];
        var text = File.ReadAllText(path,
            new System.Text.UTF8Encoding(true));
        return Parse(text, cleanName, typeFilter);
    }

    private static bool MatchesTypeFilter(string? dataType, string? typeFilter)
    {
        if (typeFilter is null) return true;
        return dataType?.Contains(typeFilter, StringComparison.OrdinalIgnoreCase) ?? false;
    }
}
