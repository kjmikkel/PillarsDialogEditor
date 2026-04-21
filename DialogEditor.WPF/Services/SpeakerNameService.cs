namespace DialogEditor.WPF.Services;

public static class SpeakerNameService
{
    private static readonly Dictionary<string, string> KnownGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        { "b1a8e901-0000-0000-0000-000000000000", "Player" },
        { "6a99a109-0000-0000-0000-000000000000", "Player" },
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

        // PoE2 companions
        { "fbeeeff7-ec6a-4a40-a47f-1843eaffc6ae", "Vela" },
        { "5529e4b7-42dc-4895-b9f8-23375a945413", "Aloth (PoE2)" },
        { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "Eder (PoE2)" },
        { "f1504d00-eb4f-423a-9c9b-e71b5b23adcc", "Xoti" },
        { "4d0750be-85ea-4838-8e52-666448927e83", "Serafen" },
        { "e41c506b-abcc-45f8-98ab-bba00a0ebc16", "Pallegina (PoE2)" },
        { "09b41c25-ce0a-4568-8f6b-2263f8a7493c", "Maia Rua" },
        { "688aa86c-fbe6-4a7f-9dd0-7ef3f8c943f4", "Tekehu" },
    };

    public static string Resolve(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return "Unknown";
        if (KnownGuids.TryGetValue(guid, out var name)) return name;
        return guid.Length >= 8 ? guid[..8] + "…" : guid;
    }
}
