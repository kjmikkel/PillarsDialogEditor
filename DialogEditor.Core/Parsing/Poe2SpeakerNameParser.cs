using System.Text.Json.Nodes;

namespace DialogEditor.Core.Parsing;

public static class Poe2SpeakerNameParser
{
    private static readonly HashSet<string> CategoryPrefixes =
        new(StringComparer.OrdinalIgnoreCase) { "Companion", "NPC", "PRO", "Hired", "Merc", "Mercenary" };

    public static IReadOnlyDictionary<string, string> Parse(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = JsonNode.Parse(json);
        var objects = root?["GameDataObjects"]?.AsArray();
        if (objects is null) return result;

        foreach (var entry in objects)
        {
            var id = entry?["ID"]?.GetValue<string>();
            var debugName = entry?["DebugName"]?.GetValue<string>();
            if (string.IsNullOrEmpty(id) || debugName is null) continue;
            result[id] = FormatDisplayName(debugName);
        }

        return result;
    }

    public static IReadOnlyDictionary<string, string> ParseFile(string path)
    {
        var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
        // Handle UTF-8 BOM
        if (json.StartsWith('﻿')) json = json[1..];
        return Parse(json);
    }

    private static string FormatDisplayName(string debugName)
    {
        var tokens = debugName.Split('_');
        var start = 0;

        if (tokens.Length > 0 && tokens[0].Equals("SPK", StringComparison.OrdinalIgnoreCase))
            start = 1;

        if (start < tokens.Length && CategoryPrefixes.Contains(tokens[start]))
            start++;

        return string.Join(" ", tokens[start..]);
    }
}
