using System.Text.Json;
using System.Text.Json.Serialization;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe2GameDataBundleParser
{
    private record BundleRoot(List<BundleObject> GameDataObjects);

    private record BundleObject(
        [property: JsonPropertyName("$type")] string? DataType,
        string Id,
        string DebugName,
        List<JsonElement>? Components = null);

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
    /// <param name="componentFilter">
    /// Optional predicate over the object's <c>Components</c> array (as raw <see cref="JsonElement"/>
    /// values). When non-null, only objects whose Components satisfy the predicate are returned.
    /// Objects with no Components array are excluded when a filter is supplied.
    /// Used e.g. to filter <c>BaseStatsGameData</c> to player classes via <c>IsPlayerClass</c>.
    /// </param>
    public static IReadOnlyList<GameDataEntry> Parse(
        string json,
        Func<string, string>? cleanName = null,
        string? typeFilter = null,
        Func<IReadOnlyList<JsonElement>, bool>? componentFilter = null)
    {
        var root = JsonSerializer.Deserialize<BundleRoot>(json, Options);
        if (root is null) return [];

        return root.GameDataObjects
            .Where(o => !string.IsNullOrWhiteSpace(o.Id)
                     && !string.IsNullOrWhiteSpace(o.DebugName)
                     && MatchesTypeFilter(o.DataType, typeFilter)
                     && MatchesComponentFilter(o.Components, componentFilter))
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
        string? typeFilter = null,
        Func<IReadOnlyList<JsonElement>, bool>? componentFilter = null)
    {
        if (!File.Exists(path)) return [];
        var text = File.ReadAllText(path,
            new System.Text.UTF8Encoding(true));
        return Parse(text, cleanName, typeFilter, componentFilter);
    }

    private static bool MatchesTypeFilter(string? dataType, string? typeFilter)
    {
        if (typeFilter is null) return true;
        return dataType?.Contains(typeFilter, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private static bool MatchesComponentFilter(
        List<JsonElement>? components,
        Func<IReadOnlyList<JsonElement>, bool>? componentFilter)
    {
        if (componentFilter is null) return true;
        return components is not null && componentFilter(components);
    }
}
