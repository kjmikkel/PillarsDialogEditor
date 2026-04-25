using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;

namespace DialogEditor.WPF.ViewModels;

public partial class GameBrowserViewModel : ObservableObject
{
    public ObservableCollection<ConversationFolderViewModel> Folders { get; } = [];

    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredFolders))]
    [NotifyCanExecuteChangedFor(nameof(ClearFilterCommand))]
    private string _filterText = string.Empty;

    public IEnumerable<ConversationFolderViewModel> FilteredFolders
    {
        get
        {
            var q = FilterText.Trim();
            if (string.IsNullOrEmpty(q)) return Folders;

            return Folders
                .Select(f => (folder: f, matches: f.Items
                    .Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .ToList()))
                .Where(t => t.matches.Count > 0)
                .Select(t => new ConversationFolderViewModel(t.folder.FolderPath, t.matches, isExpanded: true));
        }
    }

    public event Action<ConversationFile>? ConversationSelected;

    [ObservableProperty]
    private ConversationItemViewModel? _selectedItem;

    partial void OnSelectedItemChanged(ConversationItemViewModel? value)
    {
        if (value is not null)
            ConversationSelected?.Invoke(value.File);
    }

    [RelayCommand(CanExecute = nameof(HasFilterText))]
    private void ClearFilter() => FilterText = string.Empty;
    private bool HasFilterText() => !string.IsNullOrEmpty(FilterText);

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var folder in Folders)
            folder.IsExpanded = true;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var folder in Folders)
            folder.IsExpanded = false;
    }

    public void Load(IGameDataProvider provider)
    {
        GameName = provider.GameName;
        FilterText = string.Empty;
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
