using DialogEditor.Core.GameData;

namespace DialogEditor.ViewModels;

public class ConversationItemViewModel(ConversationFile file)
{
    public string Name { get; } = file.Name;
    public ConversationFile File { get; } = file;
}
