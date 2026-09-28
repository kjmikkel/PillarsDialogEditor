using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Logging;
using DialogEditor.Patch.Schema;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// Backing model for the standalone Patch Manager's About window (GitHub issue 79). Besides
/// the version it states the patcher's compatibility contract: the newest version of each
/// file format it reads (SchemaFormats), so a player can tell whether a mod needs a newer
/// patcher before trying it. Kept apart from AboutViewModel, whose copy is the editor's.
public sealed partial class PatchManagerAboutViewModel : ObservableObject
{
    public string Version { get; }

    /// One line per format, e.g. "project format 1 or earlier". Lines rather than one joined
    /// sentence so a translator never has to localise a list separator.
    public IReadOnlyList<string> SupportedFormats =>
        SchemaFormats.Supported
            .Select(f => Loc.Format("PatchManagerAbout_FormatLine", SchemaVersionMessages.FormatName(f.Kind), f.Version))
            .ToList();

    [ObservableProperty]
    private string _status = "";

    public Func<string, bool> UrlOpener { get; set; } = ExternalLauncher.Open;

    public PatchManagerAboutViewModel(string version)
    {
        Version = version;
        LocaleService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocaleService.Revision))
                OnPropertyChanged(nameof(SupportedFormats));
        };
    }

    /// The player guide (issue #80): the same README that ships in the patcher zip, on
    /// GitHub so its screenshots render. On main rather than a tag so an old patcher still
    /// opens the current guide; a test keeps the path in step with the repository.
    public const string PlayerGuideUrl =
        MainWindowViewModel.RepositoryUrl + "/blob/main/docs/patcher/README.md";

    [RelayCommand] private void OpenReleases()    => Open(SchemaFormats.PatcherReleasesUrl);
    [RelayCommand] private void OpenRepository()  => Open(MainWindowViewModel.RepositoryUrl);
    [RelayCommand] private void OpenPlayerGuide() => Open(PlayerGuideUrl);

    private void Open(string url)
    {
        if (UrlOpener(url)) return;
        AppLog.Warn($"Patch Manager About: failed to open '{url}'.");
        Status = Loc.Get("PatchManagerAbout_OpenFailed");
    }
}
