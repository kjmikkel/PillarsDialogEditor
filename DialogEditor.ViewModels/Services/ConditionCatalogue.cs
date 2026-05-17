using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogEditor.ViewModels.Services;

public record ConditionParameter(
    string Name,
    string Type,
    string Description,
    string Default,
    IReadOnlyList<string>? Options = null);

public record ConditionEntry(
    string MethodName,
    string DisplayName,
    string Category,
    IReadOnlyList<string> Games,
    string Description,
    IReadOnlyList<ConditionParameter> Parameters);

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
