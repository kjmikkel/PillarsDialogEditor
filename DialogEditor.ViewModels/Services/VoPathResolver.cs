namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Resolves the expected .wem file path for a dialog node and reports whether
/// that file exists on disk. Only applies to PoE2 game folders.
///
/// Path convention (PoE2):
///   &lt;gameRoot&gt;/PillarsOfEternityII_Data/StreamingAssets/Audio/Windows/Voices/English(US)/
///     &lt;chatterPrefix&gt;/&lt;conversationName&gt;_&lt;nodeId:0000&gt;.wem
///
/// When <c>ExternalVO</c> is set it is used verbatim (relative to the Voices/English(US) root)
/// and takes priority over the HasVO/speaker-prefix path.
///
/// The Narrator GUID is hardcoded to prefix "narrator" and therefore does not
/// require an entry in <see cref="ChatterPrefixService"/>.
/// </summary>
public static class VoPathResolver
{
    private const string NarratorGuid = "6a99a109-0000-0000-0000-000000000000";

    /// <summary>
    /// Checks whether the VO file for <paramref name="nodeId"/> in
    /// <paramref name="conversationName"/> exists on disk.
    /// </summary>
    /// <returns>
    /// <c>null</c> when the feature does not apply (non-PoE2 game ID, or no game root set).
    /// <see cref="VoCheckResult"/> with <see cref="VoPresence.NotApplicable"/> when the node
    /// has neither <paramref name="hasVO"/> nor <paramref name="externalVO"/>.
    /// Otherwise a result with <see cref="VoPresence.Found"/> or <see cref="VoPresence.Missing"/>.
    /// </returns>
    public static VoCheckResult? Check(
        string speakerGuid,
        bool   hasVO,
        string externalVO,
        bool   hasFemaleText,
        int    nodeId,
        string conversationName,
        string gameRoot,
        string activeGameId)
    {
        // Feature only applies to PoE2 with a configured game folder.
        if (!string.Equals(activeGameId, "poe2", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(gameRoot))
            return null;

        // Node carries no VO information — nothing to check.
        if (!hasVO && string.IsNullOrEmpty(externalVO))
            return new VoCheckResult(VoPresence.NotApplicable, false, null, null);

        var voRoot = Path.Combine(gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");

        var relBase = ExpectedRelativePath(speakerGuid, externalVO, nodeId, conversationName);
        if (relBase is null)
            return new VoCheckResult(VoPresence.Missing, false, null, null);
        var basePath = Path.Combine(voRoot, relBase);

        var primary   = basePath + ".wem";
        var fem       = basePath + "_fem.wem";
        var femExists = hasFemaleText && File.Exists(fem);
        return new VoCheckResult(
            File.Exists(primary) ? VoPresence.Found : VoPresence.Missing,
            femExists,
            primary,            // always set when speaker is known; file may or may not exist
            femExists ? fem : null);
    }

    /// <summary>
    /// Canonical _vo/-relative base path (no extension) for a node's VO:
    /// <c>ExternalVO</c> verbatim when set, otherwise
    /// <c>&lt;chatterPrefix&gt;/&lt;conversation&gt;_&lt;nodeId:0000&gt;</c>.
    /// Null when the speaker prefix is unknown and no ExternalVO is set.
    /// </summary>
    public static string? ExpectedRelativePath(
        string speakerGuid, string externalVO, int nodeId, string conversationName)
    {
        if (!string.IsNullOrEmpty(externalVO))
            return Path.Combine(externalVO.Split('/', '\\'));

        var chatterPrefix = string.Equals(speakerGuid, NarratorGuid,
                                StringComparison.OrdinalIgnoreCase)
                            ? "narrator"
                            : ChatterPrefixService.GetPrefix(speakerGuid);
        if (string.IsNullOrEmpty(chatterPrefix)) return null;

        return Path.Combine(
            chatterPrefix.ToLowerInvariant(),
            $"{conversationName.ToLowerInvariant()}_{nodeId:0000}");
    }

    /// <summary>
    /// If the game copy is missing but the project's <c>_vo/</c> staging folder holds
    /// the file, report it as Found: staged files are synced to the game folder on F5
    /// and removed again by F6, so "only in _vo/" means present from the modder's view.
    /// Returns <paramref name="result"/> unchanged in every other case.
    /// </summary>
    public static VoCheckResult WithLocalVoFallback(
        VoCheckResult result,
        string? projectPath,
        string gameRoot,
        bool hasFemaleText)
    {
        if (result.Status != VoPresence.Missing
            || projectPath is null
            || result.PrimaryWemPath is null
            || string.IsNullOrEmpty(gameRoot))
            return result;

        var voRoot = Path.Combine(gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        var rel          = Path.GetRelativePath(voRoot, result.PrimaryWemPath);
        var localPrimary = Path.Combine(Path.GetDirectoryName(projectPath)!, "_vo", rel);
        if (!File.Exists(localPrimary)) return result;

        var localFem  = localPrimary[..^4] + "_fem.wem";
        var femExists = hasFemaleText && File.Exists(localFem);
        return new VoCheckResult(VoPresence.Found, femExists,
            result.PrimaryWemPath, femExists ? localFem : null)
            { LocalPrimaryWemPath = localPrimary };
    }
}
