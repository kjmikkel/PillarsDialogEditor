namespace DialogEditor.ViewModels.Services;

public record SpeakerEntry(string Guid, string Name)
{
    // AutoCompleteBox filters by ToString(), so returning Name makes name-based
    // search work without needing a custom ItemFilter delegate.
    public override string ToString() => Name;
}

public static class SpeakerNameService
{
    // Built-in GUIDs always available regardless of which game is loaded
    private static readonly IReadOnlyDictionary<string, string> Defaults =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "b1a8e901-0000-0000-0000-000000000000", "Player" },
            { "00000000-0000-0000-0000-000000000000", "Narrator" },
        };

    // Game-specific names loaded via Register(); replaced entirely on each new game load
    private static Dictionary<string, string> _registered =
        new(StringComparer.OrdinalIgnoreCase);

    // True when game-specific speaker data has been loaded (PoE2 yes, PoE1 currently no)
    public static bool HasNames => _registered.Count > 0;

    // All known speakers sorted by name — built-ins always included
    public static IReadOnlyList<SpeakerEntry> All =>
        _registered
            .Select(kv => new SpeakerEntry(kv.Key, kv.Value))
            .Concat(Defaults.Select(kv => new SpeakerEntry(kv.Key, kv.Value)))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    // Replaces all previously registered names with those from the current game
    public static void Register(IReadOnlyDictionary<string, string> names)
    {
        _registered = new Dictionary<string, string>(names, StringComparer.OrdinalIgnoreCase);
    }

    // Returns the display name for a GUID, the GUID itself if unknown, or null if empty
    public static string? Resolve(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return null;
        if (_registered.TryGetValue(guid, out var name)) return name;
        if (Defaults.TryGetValue(guid, out name)) return name;
        return guid;
    }

    // Reverse lookup — returns null if the name isn't in the registered set
    public static SpeakerEntry? FindByName(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    // Finds the entry matching a given GUID (case-insensitive)
    public static SpeakerEntry? FindByGuid(string? guid) =>
        string.IsNullOrEmpty(guid) ? null :
        All.FirstOrDefault(s => string.Equals(s.Guid, guid, StringComparison.OrdinalIgnoreCase));
}
