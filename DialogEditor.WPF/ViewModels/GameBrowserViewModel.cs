using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.GameData;

namespace DialogEditor.WPF.ViewModels;

public partial class GameBrowserViewModel : ObservableObject
{
    public ObservableCollection<ConversationFolderViewModel> Folders { get; } = [];

    [ObservableProperty]
    private string _gameName = string.Empty;

    public event Action<ConversationFile>? ConversationSelected;

    [ObservableProperty]
    private ConversationItemViewModel? _selectedItem;

    partial void OnSelectedItemChanged(ConversationItemViewModel? value)
    {
        if (value is not null)
            ConversationSelected?.Invoke(value.File);
    }

    public void Load(IGameDataProvider provider)
    {
        GameName = provider.GameName;
        Folders.Clear();

        var byFolder = provider.EnumerateConversations()
            .GroupBy(f => f.FolderPath)
            .OrderBy(g => g.Key);

        foreach (var group in byFolder)
        {
            var folder = new ConversationFolderViewModel(group.Key);
            foreach (var file in group)
                folder.Items.Add(new ConversationItemViewModel(file));
            Folders.Add(folder);
        }
    }
}
