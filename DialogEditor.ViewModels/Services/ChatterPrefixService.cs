namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Stores a GUID → ChatterPrefix map loaded from speakers.gamedatabundle.
/// ChatterPrefix is the short token used by the VO pipeline (e.g. "eder", "aloth")
/// and differs from the human-readable speaker display name. Modelled on
/// <see cref="SpeakerNameService"/>: static singleton replaced wholesale on each
/// game-folder open; no partial-update API needed.
/// </summary>
public static class ChatterPrefixService
{
    private static Dictionary<string, string> _registered =
        new(StringComparer.OrdinalIgnoreCase);

    /// Replaces all previously registered prefixes with the supplied map.
    /// Keys are treated case-insensitively so GUID casing never matters.
    public static void Register(IReadOnlyDictionary<string, string> prefixes)
        => _registered = new Dictionary<string, string>(prefixes, StringComparer.OrdinalIgnoreCase);

    /// Returns the ChatterPrefix for the given speaker GUID, or null if unknown.
    public static string? GetPrefix(string speakerGuid)
        => _registered.TryGetValue(speakerGuid, out var prefix) ? prefix : null;

    /// Clears all registered prefixes (used when closing a game folder or in tests).
    public static void Clear()
        => _registered = new(StringComparer.OrdinalIgnoreCase);
}
