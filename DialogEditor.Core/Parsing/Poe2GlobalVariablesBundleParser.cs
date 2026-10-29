using System.Text.Json;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

/// <summary>
/// Deadfire's global-variable declarations, <c>design/globalvariables/*.globalvariablesbundle</c>
/// (issue 199). The game keys each variable on its <c>Tag</c>, which is the string a condition or
/// script stores (<c>IsGlobalValue(String, …)</c>); the IDs are all zero. So each entry is the tag,
/// with an empty ID, as PoE1's global variables are.
/// </summary>
public static class Poe2GlobalVariablesBundleParser
{
    private record Variable(string? Tag, int InitialValue);
    private record VariableSet(List<Variable>? GlobalVariables);
    private record Bundle(List<VariableSet>? GlobalVariablesSets);

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<GameDataEntry> Parse(string json) =>
        Variables(json).Select(v => new GameDataEntry(Id: string.Empty, Name: v.Tag!)).ToList();

    public static IReadOnlyList<GameDataEntry> ParseFile(string path)
    {
        if (!File.Exists(path)) return [];
        return Parse(Read(path));
    }

    /// Each global's start-of-game value (issue 345), keyed on its Tag. <c>InitialValue</c> is a
    /// JSON integer (an int in GlobalVariablesData.GlobalVariable; a missing one is 0). A tag
    /// declared twice keeps its last value, as GlobalVariables.LoadGlobalVariablesBundle
    /// overwrites a tag it has already seen.
    public static IReadOnlyDictionary<string, int> ParseInitialValues(string json)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var v in Variables(json)) values[v.Tag!] = v.InitialValue;
        return values;
    }

    public static IReadOnlyDictionary<string, int> ParseInitialValuesFile(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, int>();
        return ParseInitialValues(Read(path));
    }

    private static IEnumerable<Variable> Variables(string json)
    {
        var bundle = JsonSerializer.Deserialize<Bundle>(
            Poe2GameDataBundleParser.EscapeControlCharactersInStrings(json), Options);
        return (bundle?.GlobalVariablesSets ?? [])
            .SelectMany(s => s.GlobalVariables ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v.Tag));
    }

    private static string Read(string path) => File.ReadAllText(path, new System.Text.UTF8Encoding(true));
}
