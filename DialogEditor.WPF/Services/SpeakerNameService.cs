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

    public static string Resolve(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return "Unknown";
        if (KnownGuids.TryGetValue(guid, out var name)) return name;
        return guid;
    }
}
