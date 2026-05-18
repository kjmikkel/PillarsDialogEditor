using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogEditor.ViewModels.Services;

public record ScriptCatalogueEntry(
    string MethodName,
    string DisplayName,
    string Category,
    IReadOnlyList<string> Games,
    string Description,
    IReadOnlyList<ConditionParameter> Parameters,
    string? FullName = null)
{
    /// C# reflection-format FullName, e.g. "Void SetGlobalValue(String, Int32)".
    public string ReflectionFullName
        => !string.IsNullOrEmpty(FullName) ? FullName : $"Void {MethodName}()";

    /// Display label for ComboBox / AutoCompleteBox: "Set Global  (Globals)".
    public string Label => $"{DisplayName}  ({Category})";

    public override string ToString() => DisplayName;
}

public sealed class ScriptCatalogue
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IReadOnlyList<ScriptCatalogueEntry> _entries;

    private ScriptCatalogue(IReadOnlyList<ScriptCatalogueEntry> entries)
        => _entries = entries;

    public IReadOnlyList<ScriptCatalogueEntry> All => _entries;

    public ScriptCatalogueEntry? Find(string methodName)
        => _entries.FirstOrDefault(e =>
            string.Equals(e.MethodName, methodName, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ScriptCatalogueEntry> ForGame(string gameId)
        => _entries
            .Where(e => e.Games.Any(g => string.Equals(g, gameId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    public static ScriptCatalogue LoadEmbedded()
    {
        var assembly     = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("scripts.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded scripts.json resource not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var entries = JsonSerializer.Deserialize<List<ScriptCatalogueEntry>>(stream, Options)
            ?? throw new InvalidOperationException("Failed to deserialise scripts.json.");
        return new ScriptCatalogue(entries);
    }

    private static ScriptCatalogue? _instance;

    public static ScriptCatalogue Instance => _instance ??= LoadEmbedded();

    public static void Configure(ScriptCatalogue catalogue) => _instance = catalogue;
}
