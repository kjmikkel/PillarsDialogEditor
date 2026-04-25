using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.WPF.ViewModels;

public partial class ConversationFolderViewModel(string folderPath,
    IEnumerable<ConversationItemViewModel>? items = null,
    bool isExpanded = false) : ObservableObject
{
    public string FolderPath { get; } = folderPath;
    public string DisplayName { get; } = string.IsNullOrEmpty(folderPath) ? "(root)" : folderPath;

    [ObservableProperty]
    private bool _isExpanded = isExpanded;

    public ObservableCollection<ConversationItemViewModel> Items { get; } =
        items is null ? [] : new ObservableCollection<ConversationItemViewModel>(items);
}
