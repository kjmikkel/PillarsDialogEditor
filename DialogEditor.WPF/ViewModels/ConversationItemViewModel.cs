using DialogEditor.Core.GameData;

namespace DialogEditor.WPF.ViewModels;

public class ConversationItemViewModel(ConversationFile file)
{
    public string Name { get; } = file.Name;
    public ConversationFile File { get; } = file;
}
