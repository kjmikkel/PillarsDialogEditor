using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;
using DialogEditor.WPF.Resources;
using DialogEditor.WPF.Services;
using Microsoft.Win32;

namespace DialogEditor.WPF.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public GameBrowserViewModel Browser { get; } = new();
    public ConversationViewModel Canvas { get; } = new();
    public NodeDetailViewModel Detail { get; } = new();

    private IGameDataProvider? _provider;
    private ConversationFile? _currentFile;

    [ObservableProperty] private string _statusText = Loc.Get("Status_OpenFolder");
    [ObservableProperty] private IReadOnlyList<string> _availableLanguages = [];
    [ObservableProperty] private string _selectedLanguage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowserFlyoutOpen))]
    private bool _isBrowserExpanded = AppSettings.BrowserPinned; // expanded iff pinned on startup

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowserFlyoutOpen))]
    private bool _isBrowserPinned = AppSettings.BrowserPinned;

    [ObservableProperty] private bool _isDetailExpanded  = AppSettings.DetailExpanded;
    [ObservableProperty] private string? _currentConversationName;

    // True only when the panel is open as a temporary flyout (not pinned)
    public bool IsBrowserFlyoutOpen => IsBrowserExpanded && !IsBrowserPinned;

    partial void OnIsBrowserExpandedChanged(bool value) { /* expansion is transient, not persisted */ }
    partial void OnIsBrowserPinnedChanged(bool value)
    {
        AppSettings.BrowserPinned = value;
        IsBrowserExpanded = value; // pin → open, unpin → collapse to strip
    }
    partial void OnIsDetailExpandedChanged(bool value)  => AppSettings.DetailExpanded  = value;

    public MainWindowViewModel()
    {
        Browser.ConversationSelected += OnConversationSelected;
        Canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversationViewModel.SelectedNode))
                Detail.Load(Canvas.SelectedNode);
        };

        var last = AppSettings.LastGameDirectory;
        if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
            LoadDirectory(last);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_provider is null || string.IsNullOrEmpty(value)) return;
        _provider.Language = value;
        AppSettings.LastLanguage = value;
        if (_currentFile is not null)
            OnConversationSelected(_currentFile);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = Loc.Get("Dialog_SelectFolder") };
        if (dialog.ShowDialog() != true) return;
        LoadDirectory(dialog.FolderName);
    }

    private void LoadDirectory(string path)
    {
        var provider = GameDataProviderFactory.Detect(path);
        if (provider is null)
        {
            StatusText = Loc.Get("Status_FolderNotRecognized");
            return;
        }

        AppSettings.LastGameDirectory = path;
        _provider = provider;
        CurrentConversationName = null;
        SpeakerNameService.Register(provider.LoadSpeakerNames());
        AvailableLanguages = provider.AvailableLanguages;
        SelectedLanguage = AppSettings.PickLanguage(AvailableLanguages, AppSettings.LastLanguage);
        Browser.Load(provider);
        StatusText = Loc.Format("Status_FolderLoaded", provider.GameName, path);
    }

    private void OnConversationSelected(ConversationFile file)
    {
        if (_provider is null) return;
        try
        {
            _currentFile = file;
            var conversation = _provider.LoadConversation(file);
            Canvas.Load(conversation);
            Detail.Clear();
            CurrentConversationName = file.Name;
            if (!IsBrowserPinned) IsBrowserExpanded = false;
            StatusText = Loc.Format("Status_ConversationLoaded", file.Name, conversation.Nodes.Count);
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format("Status_LoadError", file.Name, ex.Message);
        }
    }
}
