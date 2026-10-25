using System.Text.Json;

namespace DialogEditor.Patch;

/// <summary>
/// Sorts a mod-file load failure for the person installing it (GitHub issue 249). A file that
/// isn't valid JSON, isn't a valid zip, or ends early is damaged (usually a broken download) or
/// isn't a mod at all. Either way the fix is the same, "download it again", and the parser's own
/// message ("'n' is an invalid JSON literal…") helps nobody but a developer, so it goes to the log.
/// </summary>
public static class ModFileErrors
{
    public static bool IsDamaged(Exception ex) =>
        ex is JsonException or InvalidDataException or EndOfStreamException
        || ex.InnerException is { } inner && IsDamaged(inner);
}
