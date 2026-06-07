namespace DialogEditor.Patch.Changelog;

/// One released version with its grouped notes, newest-first in a changelog.
public sealed record ChangelogRelease(
    string Version,
    string Date,
    IReadOnlyList<ChangelogSection> Sections);

/// A group of notes under a release. Heading is null for a flat, unlabelled list
/// (bullets that appear before any "### " subheading).
public sealed record ChangelogSection(string? Heading, IReadOnlyList<string> Entries)
{
    public bool HasHeading => !string.IsNullOrWhiteSpace(Heading);
}
