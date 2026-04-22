namespace DialogEditor.WPF.Services;

public static class SpeakerNameService
{
    private static readonly Dictionary<string, string> KnownGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        { "b1a8e901-0000-0000-0000-000000000000", "Player" },
        { "00000000-0000-0000-0000-000000000000", "Narrator" },

        // PoE1 companions
        { "fb6a7cbb-80b6-4b9c-8a99-41c8a031f380", "Aloth" },
        { "7c720723-c4eb-48d3-b082-498e9454c92e", "Iselmyr" },
        { "b1a7e803-0000-0000-0000-000000000000", "Eder" },
        { "b1a7e808-0000-0000-0000-000000000000", "Durance" },
        { "b1a7e804-0000-0000-0000-000000000000", "Grieving Mother" },
        { "b1a7e805-0000-0000-0000-000000000000", "Hiravias" },
        { "b1a7e806-0000-0000-0000-000000000000", "Kana Rua" },
        { "b1a7e807-0000-0000-0000-000000000000", "Sagani" },
        { "b1a7e809-0000-0000-0000-000000000000", "Pallegina" },
        { "b1a7e80a-0000-0000-0000-000000000000", "Devil of Caroc" },
        { "b1a7e80b-0000-0000-0000-000000000000", "Zahua" },
        { "b1a7e80c-0000-0000-0000-000000000000", "Maneha" },
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
        return guid.Length >= 8 ? guid[..8] + "…" : guid;
    }
}
