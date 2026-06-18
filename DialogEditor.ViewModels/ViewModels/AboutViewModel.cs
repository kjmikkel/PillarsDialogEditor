using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// Backing model for the About dialog: version + license/credits copy + repository/docs
/// link commands. Link opening goes through an injectable seam (default: ExternalLauncher).
public sealed partial class AboutViewModel : ObservableObject
{
    public string Version { get; }
    public string RepositoryUrl { get; }
    public string DocsUrl { get; }

    public string AppName => Loc.Get("About_AppName");
    public string Description => Loc.Get("About_Description");
    public string License => Loc.Get("About_License");
    public string Credits => Loc.Get("About_Credits");

    [ObservableProperty]
    private string _status = "";

    public Func<string, bool> UrlOpener { get; set; } = ExternalLauncher.Open;

    public AboutViewModel(string version, string repositoryUrl, string docsUrl)
    {
        Version = version;
        RepositoryUrl = repositoryUrl;
        DocsUrl = docsUrl;
        LocaleService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocaleService.Revision))
                OnPropertyChanged(string.Empty);
        };
    }

    [RelayCommand] private void OpenRepository() => Open(RepositoryUrl);
    [RelayCommand] private void OpenDocs() => Open(DocsUrl);

    private void Open(string url)
    {
        if (UrlOpener(url)) return;
        AppLog.Warn($"About: failed to open '{url}'.");
        Status = Loc.Get("About_OpenFailed");
    }
}
