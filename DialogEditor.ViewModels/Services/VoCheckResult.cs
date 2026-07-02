namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Represents whether a VO (.wem) file was found for a dialog node.
/// </summary>
public enum VoPresence
{
    /// <summary>Node has neither HasVO nor ExternalVO set — VO check is not applicable.</summary>
    NotApplicable,

    /// <summary>The expected .wem file exists on disk.</summary>
    Found,

    /// <summary>The expected .wem file is absent (or the speaker prefix is unknown).</summary>
    Missing
}

/// <summary>
/// Result of a VO path check for a single dialog node.
/// </summary>
/// <param name="Status">Whether the primary .wem file was found.</param>
/// <param name="FemaleVariantFound">
/// True when a <c>_fem.wem</c> companion file also exists alongside the primary file.
/// Informational only — does not affect <see cref="Status"/>.
/// </param>
/// <param name="PrimaryWemPath">
/// Full path to the primary <c>.wem</c> file, or <c>null</c> when the path cannot be
/// resolved (NotApplicable nodes, or nodes with an unknown speaker GUID).
/// </param>
/// <param name="FemWemPath">
/// Full path to the <c>_fem.wem</c> companion file, or <c>null</c> if it does not exist.
/// </param>
public record VoCheckResult(
    VoPresence Status,
    bool       FemaleVariantFound,
    string?    PrimaryWemPath,
    string?    FemWemPath)
{
    /// <summary>
    /// Path to the copy staged in the project's <c>_vo/</c> folder, set only when the
    /// game copy is absent and <see cref="VoPathResolver.WithLocalVoFallback"/> found
    /// the file there. Playback should prefer this over <see cref="PrimaryWemPath"/>,
    /// which stays the canonical game path (import/batch code derives destination
    /// paths from it and must not receive a project-relative path).
    /// </summary>
    public string? LocalPrimaryWemPath { get; init; }
}
