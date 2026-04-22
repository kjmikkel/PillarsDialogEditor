using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;
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

    [ObservableProperty] private string _statusText = "Open a game folder to begin.";
    [ObservableProperty] private IReadOnlyList<string> _availableLanguages = [];
    [ObservableProperty] private string _selectedLanguage = string.Empty;

    public MainWindowViewModel()
    {
        Browser.ConversationSelected += OnConversationSelected;
        Canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversationViewModel.SelectedNode))
                Detail.Load(Canvas.SelectedNode);
        };
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
        var dialog = new OpenFolderDialog { Title = "Select game root folder" };
        if (dialog.ShowDialog() != true) return;

        var provider = GameDataProviderFactory.Detect(dialog.FolderName);
        if (provider is null)
        {
            StatusText = "Folder not recognized as PoE1 or PoE2 root.";
            return;
        }

        _provider = provider;
        SpeakerNameService.Register(provider.LoadSpeakerNames());
        AvailableLanguages = provider.AvailableLanguages;
        SelectedLanguage = AppSettings.PickLanguage(AvailableLanguages, AppSettings.LastLanguage);
        Browser.Load(provider);
        StatusText = $"{provider.GameName} — {dialog.FolderName}";
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
            StatusText = $"{file.Name} — {conversation.Nodes.Count} nodes";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading {file.Name}: {ex.Message}";
        }
    }
}
