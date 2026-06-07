using DialogEditor.Patch.Changelog;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

/// Display wrapper for the parsed changelog. Holds the releases and the empty-state copy
/// for the reader window.
public sealed class ChangelogViewModel
{
    public IReadOnlyList<ChangelogRelease> Releases { get; }

    public ChangelogViewModel(IReadOnlyList<ChangelogRelease> releases) => Releases = releases;

    public bool IsEmpty => Releases.Count == 0;
    public bool HasReleases => Releases.Count > 0;
    public string EmptyMessage => Loc.Get("Changelog_Empty");
}
