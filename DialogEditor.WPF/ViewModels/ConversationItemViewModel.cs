using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.GameData;

namespace DialogEditor.WPF.ViewModels;

public partial class ConversationItemViewModel(ConversationFile file) : ObservableObject
{
    public string Name { get; } = file.Name;
    public ConversationFile File { get; } = file;

    [ObservableProperty] private bool _hasNeverLinks;
    [ObservableProperty] private bool _hasAlwaysLinks;
}
