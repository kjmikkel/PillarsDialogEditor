using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;
using Microsoft.Win32;

namespace DialogEditor.WPF.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public GameBrowserViewModel Browser { get; } = new();
    public ConversationViewModel Canvas { get; } = new();
    public NodeDetailViewModel Detail { get; } = new();

    private IGameDataProvider? _provider;

    [ObservableProperty]
    private string _statusText = "Open a game folder to begin.";

    public MainWindowViewModel()
    {
        Browser.ConversationSelected += OnConversationSelected;
        Canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversationViewModel.SelectedNode))
                Detail.Load(Canvas.SelectedNode);
        };
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
        Browser.Load(provider);
        StatusText = $"{provider.GameName} \u2014 {dialog.FolderName}";
    }

    private void OnConversationSelected(ConversationFile file)
    {
        if (_provider is null) return;
        try
        {
            var conversation = _provider.LoadConversation(file);
            Canvas.Load(conversation);
            Detail.Clear();
            StatusText = $"{file.Name} \u2014 {conversation.Nodes.Count} nodes";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading {file.Name}: {ex.Message}";
        }
    }
}
