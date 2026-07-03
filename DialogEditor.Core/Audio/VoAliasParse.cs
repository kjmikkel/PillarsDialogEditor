namespace DialogEditor.Core.Audio;

/// <summary>
/// The target a PoE2 <c>ExternalVO</c> alias points at, decomposed from
/// "&lt;speakerFolder&gt;/&lt;conversation&gt;_&lt;nodeId&gt;".
/// </summary>
public record VoAliasTarget(string SpeakerFolder, string Conversation, int NodeId);

/// <summary>
/// Parses PoE2 <c>ExternalVO</c> alias paths. The writer emits
/// "&lt;folder&gt;/&lt;conversation&gt;_{nodeId:0000}" — nodeId padded to a MINIMUM
/// of four digits (ids ≥ 10000 produce five). 782 of the 787 shipped values match
/// this shape; the rest (and hand-crafted values) return null and the UI falls
/// back to showing the raw path.
/// </summary>
public static class VoAliasParse
{
    public static VoAliasTarget? TryParse(string? aliasPath)
    {
        if (string.IsNullOrWhiteSpace(aliasPath)) return null;

        // Exactly one separator: folder / file. Shipped data never nests deeper.
        var parts = aliasPath.Split('/', '\\');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return null;

        var file = parts[1];
        var us   = file.LastIndexOf('_');
        if (us <= 0 || us == file.Length - 1) return null;

        var digits = file[(us + 1)..];
        if (digits.Length < 4 || !digits.All(char.IsAsciiDigit)) return null;
        if (!int.TryParse(digits, out var id)) return null;

        return new VoAliasTarget(parts[0], file[..us], id);
    }
}
