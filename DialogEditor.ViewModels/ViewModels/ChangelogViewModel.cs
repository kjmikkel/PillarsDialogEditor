using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Patch.Changelog;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// Display wrapper for the parsed changelog. Holds the releases and the empty-state copy
/// for the reader window.
public sealed partial class ChangelogViewModel : ObservableObject
{
    public IReadOnlyList<ChangelogRelease> Releases { get; }

    public ChangelogViewModel(IReadOnlyList<ChangelogRelease> releases)
    {
        Releases = releases;
        LocaleService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocaleService.Revision))
                OnPropertyChanged(string.Empty);
        };
    }

    public bool IsEmpty => Releases.Count == 0;
    public bool HasReleases => Releases.Count > 0;
    public string EmptyMessage => Loc.Get("Changelog_Empty");
}
