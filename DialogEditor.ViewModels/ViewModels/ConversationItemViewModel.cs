using DialogEditor.Core.GameData;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

public class ConversationItemViewModel
{
    public string           Name        { get; }
    public ConversationFile File        { get; }
    public bool             IsNew       { get; }
    public string           DisplayName => IsNew
        ? Name + Loc.Get("Label_NewConversation_Suffix")
        : Name;

    public ConversationItemViewModel(ConversationFile file, bool isNew = false)
    {
        Name  = file.Name;
        File  = file;
        IsNew = isNew;
    }
}
