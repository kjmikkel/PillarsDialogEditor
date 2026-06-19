using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class GlobalVariablesCsvParser
{
    public static IReadOnlyList<GameDataEntry> Parse(string csvText)
    {
        if (string.IsNullOrWhiteSpace(csvText)) return [];

        return csvText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)                         // skip header row
            .Select(line => line.Split(',')[0].Trim().Trim('"', '\r'))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new GameDataEntry(Id: string.Empty, Name: name))
            .ToList();
    }

    public static IReadOnlyList<GameDataEntry> ParseFile(string path)
    {
        if (!File.Exists(path)) return [];
        return Parse(File.ReadAllText(path,
            new System.Text.UTF8Encoding(true)));
    }
}
