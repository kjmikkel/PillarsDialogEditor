using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.ViewModels;

/// <summary>
/// A folder in the Conversations dock. Folders nest (issue 85, part B): a game folder such as
/// <c>companions\poi</c> is a "Poi" subfolder inside "Companions", not a flat row with a raw path.
/// </summary>
public partial class ConversationFolderViewModel(string folderPath,
    IEnumerable<ConversationItemViewModel>? items = null,
    bool isExpanded = false,
    string? displayName = null,
    string? toolTip = null) : ObservableObject
{
    /// <summary>The raw game folder path; the dock sorts by it and shows it as the tooltip.</summary>
    public string FolderPath { get; } = folderPath;

    /// <summary>The folder's own name, tidied, unless a caller names it (the New conversations
    /// folder, a DLC group).</summary>
    public string DisplayName { get; } = displayName ?? FolderDisplayName.For(LastSegment(folderPath));

    /// <summary>The raw path, so a writer who knows the file layout can still see it. Folders with
    /// no real path (New conversations) show their name, unless a caller explains them (the
    /// prototype and test group).</summary>
    public string ToolTip { get; } = toolTip ?? (string.IsNullOrEmpty(folderPath) ? displayName ?? string.Empty : folderPath);

    [ObservableProperty]
    private bool _isExpanded = isExpanded;

    public ObservableCollection<ConversationFolderViewModel> Subfolders { get; } = [];

    public ObservableCollection<ConversationItemViewModel> Items { get; } =
        items is null ? [] : new ObservableCollection<ConversationItemViewModel>(items);

    /// <summary>What the tree shows inside the folder: subfolders first, then conversations, as a
    /// file tree does. Built once the folder is filled (the dock doesn't add to a shown folder).</summary>
    public IEnumerable<object> Children => Subfolders.Cast<object>().Concat(Items);

    private static string LastSegment(string path)
    {
        var i = path.LastIndexOfAny(['\\', '/']);
        return i < 0 ? path : path[(i + 1)..];
    }
}
