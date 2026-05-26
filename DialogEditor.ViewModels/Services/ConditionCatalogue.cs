using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogEditor.ViewModels.Services;

public record ConditionParameter(
    string Name,
    string Type,
    string Description,
    string Default,
    IReadOnlyList<string>? Options = null,
    IReadOnlyList<string>? Values  = null);

public record ConditionEntry(
    string MethodName,
    string DisplayName,
    string Category,
    IReadOnlyList<string> Games,
    string Description,
    IReadOnlyList<ConditionParameter> Parameters,
    string? FullName = null)
{
    /// The complete C# reflection-format name written into condition XML/JSON,
    /// e.g. "Boolean IsGlobalValue(String, Operator, Int32)".
    /// Falls back to MethodName if not set (allows forward-compat).
    public string ReflectionFullName => !string.IsNullOrEmpty(FullName) ? FullName : MethodName;
}

public sealed class ConditionCatalogue
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IReadOnlyList<ConditionEntry> _entries;

    private ConditionCatalogue(IReadOnlyList<ConditionEntry> entries)
        => _entries = entries;

    public IReadOnlyList<ConditionEntry> All => _entries;

    public ConditionEntry? Find(string methodName)
        => _entries.FirstOrDefault(e =>
            string.Equals(e.MethodName, methodName, StringComparison.OrdinalIgnoreCase));

    /// Looks up an entry by its exact C# reflection FullName.
    /// Prefer the overload that also takes a gameId when available.
    public ConditionEntry? FindByFullName(string fullName)
        => _entries.FirstOrDefault(e =>
            string.Equals(e.ReflectionFullName, fullName, StringComparison.OrdinalIgnoreCase));

    /// Looks up an entry by fullName AND game, returning the game-specific entry
    /// when two conditions share the same signature but different parameter options
    /// (e.g. IsMapVisibility with PoE1 vs PoE2 area names).
    public ConditionEntry? FindByFullName(string fullName, string gameId)
        => _entries.FirstOrDefault(e =>
            string.Equals(e.ReflectionFullName, fullName, StringComparison.OrdinalIgnoreCase) &&
            e.Games.Any(g => string.Equals(g, gameId, StringComparison.OrdinalIgnoreCase)))
        ?? FindByFullName(fullName);

    public IReadOnlyList<ConditionEntry> ForGame(string gameId)
        => _entries
            .Where(e => e.Games.Any(g => string.Equals(g, gameId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// Load from the embedded conditions.json resource shipped with the assembly.
    public static ConditionCatalogue LoadEmbedded()
    {
        var assembly     = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("conditions.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded conditions.json resource not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var entries = JsonSerializer.Deserialize<List<ConditionEntry>>(stream, Options)
            ?? throw new InvalidOperationException("Failed to deserialise conditions.json.");
        return new ConditionCatalogue(entries);
    }

    // ── Static instance (lazy-loaded once per process, matches SpeakerNameService pattern) ──
    private static ConditionCatalogue? _instance;

    public static ConditionCatalogue Instance
        => _instance ??= LoadEmbedded();

    public static void Configure(ConditionCatalogue catalogue)
        => _instance = catalogue;
}
