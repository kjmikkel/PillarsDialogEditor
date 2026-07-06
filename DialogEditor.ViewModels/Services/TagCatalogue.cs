using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogEditor.ViewModels.Services;

/// One entry of the dialog-text tag vocabulary (tags.json).
/// Kind: "Token" (engine-substituted), "Markup" (Unity rich text), or
/// "Convention" (rendered literally; meaning is for the player).
/// Count is occurrences in shipped dialog text (2026-07-05 stringtable scan);
/// 0 on a Token means engine-supported but unused in shipped dialog.
public record TagEntry(
    string Name,
    string Kind,
    IReadOnlyList<string> Games,
    string Category,
    string Description,
    string? Example = null,
    int Count = 0,
    string? Notes = null,
    string? Insert = null);

/// The dialog-text tag vocabulary, engine-verified against both games'
/// decompiled Conversation.cs (+ PoE2 ShipDuelManager.cs) — see
/// docs/superpowers/specs/2026-07-05-tag-reference-window-design.md.
/// Mirrors ConditionCatalogue so the future token autocomplete/validation
/// feature can consume the same instance.
public sealed class TagCatalogue
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IReadOnlyList<TagEntry> _entries;

    private TagCatalogue(IReadOnlyList<TagEntry> entries)
        => _entries = entries;

    public IReadOnlyList<TagEntry> All => _entries;

    public IReadOnlyList<TagEntry> ForGame(string gameId)
        => _entries
            .Where(e => e.Games.Any(g => string.Equals(g, gameId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// Load from the embedded tags.json resource shipped with the assembly.
    public static TagCatalogue LoadEmbedded()
    {
        var assembly     = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("tags.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded tags.json resource not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var entries = JsonSerializer.Deserialize<List<TagEntry>>(stream, Options)
            ?? throw new InvalidOperationException("Failed to deserialise tags.json.");
        return new TagCatalogue(entries);
    }

    // ── Static instance (lazy-loaded once per process, matches ConditionCatalogue) ──
    private static TagCatalogue? _instance;

    public static TagCatalogue Instance
        => _instance ??= LoadEmbedded();

    public static void Configure(TagCatalogue catalogue)
        => _instance = catalogue;
}
