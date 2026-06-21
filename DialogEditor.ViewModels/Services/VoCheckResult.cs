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
public record VoCheckResult(VoPresence Status, bool FemaleVariantFound);
