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

        string basePath;
        if (!string.IsNullOrEmpty(externalVO))
        {
            // ExternalVO ships with '/' separators; Path.Combine normalises on Windows.
            // Example: "eder/00_cv_test_0153" → "<voRoot>\eder\00_cv_test_0153"
            basePath = Path.Combine(voRoot, externalVO);
        }
        else
        {
            // Resolve the chatter prefix: Narrator is hardcoded; all others come from
            // ChatterPrefixService (loaded from speakers.gamedatabundle).
            var chatterPrefix = string.Equals(speakerGuid, NarratorGuid,
                                    StringComparison.OrdinalIgnoreCase)
                                ? "narrator"
                                : ChatterPrefixService.GetPrefix(speakerGuid);

            if (string.IsNullOrEmpty(chatterPrefix))
                return new VoCheckResult(VoPresence.Missing, false, null, null);

            // Conversation name is lowercased to match the VO pipeline file naming.
            // Node ID is zero-padded to four digits.
            basePath = Path.Combine(voRoot,
                chatterPrefix.ToLowerInvariant(),
                $"{conversationName.ToLowerInvariant()}_{nodeId:0000}");
        }

        var primary   = basePath + ".wem";
        var fem       = basePath + "_fem.wem";
        var femExists = File.Exists(fem);
        return new VoCheckResult(
            File.Exists(primary) ? VoPresence.Found : VoPresence.Missing,
            femExists,
            primary,            // always set when speaker is known; file may or may not exist
            femExists ? fem : null);
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
        string gameRoot)
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
        var femExists = File.Exists(localFem);
        return new VoCheckResult(VoPresence.Found, femExists,
            result.PrimaryWemPath, femExists ? localFem : null);
    }
}
