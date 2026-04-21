using System.Collections.ObjectModel;

namespace DialogEditor.WPF.ViewModels;

public class ConversationFolderViewModel(string folderPath)
{
    public string FolderPath { get; } = folderPath;
    public string DisplayName { get; } = string.IsNullOrEmpty(folderPath) ? "(root)" : folderPath;
    public ObservableCollection<ConversationItemViewModel> Items { get; } = [];
}
