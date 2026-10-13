using DialogEditor.Core.GameData;

namespace DialogEditor.ViewModels;

public class ConversationItemViewModel
{
    public string           Name        { get; }
    public ConversationFile File        { get; }
    public bool             IsNew       { get; }
    // Just the name (issue 85): the (new) folder and the green colour already mark a new one.
    public string           DisplayName => Name;

    public ConversationItemViewModel(ConversationFile file, bool isNew = false)
    {
        Name  = file.Name;
        File  = file;
        IsNew = isNew;
    }
}
