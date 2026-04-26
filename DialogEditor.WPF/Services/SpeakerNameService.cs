namespace DialogEditor.WPF.Services;

public static class SpeakerNameService
{
    // Built-in GUIDs that are always available regardless of which game is loaded
    private static readonly IReadOnlyDictionary<string, string> Defaults =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "b1a8e901-0000-0000-0000-000000000000", "Player" },
            { "00000000-0000-0000-0000-000000000000", "Narrator" },
        };

    // Game-specific names loaded via Register(); replaced entirely on each new game load
    private static Dictionary<string, string> _registered =
        new(StringComparer.OrdinalIgnoreCase);

    // Replaces all previously registered names with those from the current game
    public static void Register(IReadOnlyDictionary<string, string> names)
    {
        _registered = new Dictionary<string, string>(names, StringComparer.OrdinalIgnoreCase);
    }

    // Returns null when the GUID is empty/unset; callers apply the localised fallback
    public static string? Resolve(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return null;
        if (_registered.TryGetValue(guid, out var name)) return name;
        if (Defaults.TryGetValue(guid, out name)) return name;
        return guid;
    }
}
