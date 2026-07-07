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
    public bool IsWhatsNew { get; }
    private readonly string _version;

    public ChangelogViewModel(
        IReadOnlyList<ChangelogRelease> releases,
        bool isWhatsNew = false,
        string version = "")
    {
        Releases   = releases;
        IsWhatsNew = isWhatsNew;
        _version   = version;
        LocaleService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocaleService.Revision))
                OnPropertyChanged(string.Empty);
        };
    }

    public bool IsEmpty => Releases.Count == 0;
    public bool HasReleases => Releases.Count > 0;
    public string EmptyMessage => Loc.Get("Changelog_Empty");

    // What's-new mode shows a version header and a distinct window title; the normal
    // changelog reader path keeps the "Changelog" title and hides the header.
    public bool ShowHeader => IsWhatsNew;
    public string HeaderText => Loc.Format("WhatsNew_Header", _version);
    public string WindowTitle => IsWhatsNew
        ? Loc.Get("WhatsNew_Title")
        : Loc.Get("Changelog_Title");
}
