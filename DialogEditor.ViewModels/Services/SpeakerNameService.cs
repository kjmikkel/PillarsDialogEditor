using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;

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

    // True when game-specific speaker data has been loaded (both PoE1 and PoE2 supply names)
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

    private const string PlayerGuid   = "b1a8e901-0000-0000-0000-000000000000";
    private const string NarratorGuid = "00000000-0000-0000-0000-000000000000";

    // Returns the display name for a GUID, a readable "Unknown speaker (c0a1c0a1…)" label if
    // unknown, or null if empty. Never the bare GUID: a Reader can't tell who that is (GitHub
    // issue 257); the full GUID stays in Node Details.
    public static string? Resolve(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return null;
        if (_registered.TryGetValue(guid, out var name)) return name;
        if (Defaults.TryGetValue(guid, out name)) return name;
        return Loc.Format("Speaker_UnknownWithId", guid.Length > 8 ? guid[..8] + "…" : guid);
    }

    // True when the GUID names a registered or built-in speaker
    public static bool IsKnown(string? guid) =>
        !string.IsNullOrWhiteSpace(guid) &&
        (_registered.ContainsKey(guid) || Defaults.ContainsKey(guid));

    // The category a built-in speaker GUID stands for, so analytics can fold it together with
    // blank-GUID nodes of the same kind instead of listing "Player" twice (GitHub issue 257).
    public static SpeakerCategory? BuiltInCategory(string? guid) =>
        string.Equals(guid, PlayerGuid, StringComparison.OrdinalIgnoreCase)   ? SpeakerCategory.Player :
        string.Equals(guid, NarratorGuid, StringComparison.OrdinalIgnoreCase) ? SpeakerCategory.Narrator :
        null;

    // Reverse lookup — returns null if the name isn't in the registered set
    public static SpeakerEntry? FindByName(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    // Finds the entry matching a given GUID (case-insensitive)
    public static SpeakerEntry? FindByGuid(string? guid) =>
        string.IsNullOrEmpty(guid) ? null :
        All.FirstOrDefault(s => string.Equals(s.Guid, guid, StringComparison.OrdinalIgnoreCase));
}
