using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFolderPicker _folderPicker;

    public GameBrowserViewModel Browser { get; }
    public ConversationViewModel Canvas { get; }
    public NodeDetailViewModel Detail { get; } = new();

    private IGameDataProvider? _provider;
    private ConversationFile? _currentFile;

    [ObservableProperty] private string _statusText = Loc.Get("Status_OpenFolder");
    [ObservableProperty] private IReadOnlyList<string> _availableLanguages = [];
    [ObservableProperty] private string _selectedLanguage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowserFlyoutOpen))]
    private bool _isBrowserExpanded = AppSettings.BrowserPinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowserFlyoutOpen))]
    private bool _isBrowserPinned = AppSettings.BrowserPinned;

    [ObservableProperty] private bool _isDetailExpanded  = AppSettings.DetailExpanded;
    [ObservableProperty] private string? _currentConversationName;

    public bool IsBrowserFlyoutOpen => IsBrowserExpanded && !IsBrowserPinned;

    partial void OnIsBrowserPinnedChanged(bool value)
    {
        AppSettings.BrowserPinned = value;
        IsBrowserExpanded = value;
    }
    partial void OnIsDetailExpandedChanged(bool value) => AppSettings.DetailExpanded = value;

    public MainWindowViewModel(IDispatcher dispatcher, IFolderPicker folderPicker)
    {
        _folderPicker = folderPicker;
        Browser = new GameBrowserViewModel(dispatcher);
        Canvas  = new ConversationViewModel(dispatcher);

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
    private async Task OpenFolder()
    {
        var path = await _folderPicker.PickFolderAsync(Loc.Get("Dialog_SelectFolder"));
        if (path is null) return;
        LoadDirectory(path);
    }

    private void LoadDirectory(string path)
    {
        var provider = GameDataProviderFactory.Detect(path);
        if (provider is null)
        {
            AppLog.Warn($"Folder not recognised as PoE1 or PoE2 root: {path}");
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
        AppLog.Info($"Loaded {provider.GameName} from {path}");
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
            AppLog.Error($"Failed to load conversation '{file.Name}'", ex);
            StatusText = Loc.Format("Status_LoadError", file.Name, ex.Message);
        }
    }
}
