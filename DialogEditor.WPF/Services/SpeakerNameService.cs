namespace DialogEditor.WPF.Services;

public static class SpeakerNameService
{
    private static readonly Dictionary<string, string> KnownGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        { "b1a8e901-0000-0000-0000-000000000000", "Player" },
        { "00000000-0000-0000-0000-000000000000", "Narrator" },
    };

    public static void Register(IReadOnlyDictionary<string, string> names)
    {
        foreach (var (guid, name) in names)
            KnownGuids[guid] = name;
    }

    // Returns null when the GUID is empty/unset; callers apply the
    // localised fallback rather than comparing against a hard-coded string.
    public static string? Resolve(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return null;
        if (KnownGuids.TryGetValue(guid, out var name)) return name;
        return guid;
    }
}
