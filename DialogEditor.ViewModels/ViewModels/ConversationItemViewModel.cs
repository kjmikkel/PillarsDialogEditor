using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.GameData;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

public partial class ConversationItemViewModel : ObservableObject
{
    public string           Name        { get; }
    public ConversationFile File        { get; }
    public bool             IsNew       { get; }
    // Just the name (issue 85): the (new) folder and the green colour already mark a new one.
    public string           DisplayName => Name;

    /// <summary>The open project changes this conversation: it has a non-empty patch (issue 85,
    /// part C). Set by <see cref="GameBrowserViewModel.SetProjectConversations"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditedStatus))]
    private bool _isEdited;

    /// <summary>The row's accessible item status: what the "●" says, for screen readers.</summary>
    public string? EditedStatus => IsEdited ? Loc.Get("Browser_EditedInProject") : null;

    public ConversationItemViewModel(ConversationFile file, bool isNew = false)
    {
        Name  = file.Name;
        File  = file;
        IsNew = isNew;
    }
}
