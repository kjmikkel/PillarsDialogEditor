using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// <summary>
/// Drives the "Find in Project" search: collects query inputs, invokes
/// <see cref="ProjectFindService"/> synchronously, and exposes results plus a
/// navigation event for the (Task 4) window to hook up. The open-conversation
/// accessor is re-invoked on every search so a live unsaved snapshot is used
/// instead of stale on-disk data.
/// </summary>
public partial class ProjectFindViewModel : ObservableObject
{
    private readonly DialogProject _project;
    private readonly IGameDataProvider _provider;
    private readonly string _primaryLanguage;
    private readonly Func<(string? Name, ConversationEditSnapshot? Snapshot)> _openAccessor;

    public ProjectFindViewModel(
        DialogProject project, IGameDataProvider provider, string primaryLanguage,
        Func<(string? Name, ConversationEditSnapshot? Snapshot)> openConversationAccessor)
    {
        _project = project;
        _provider = provider;
        _primaryLanguage = primaryLanguage;
        _openAccessor = openConversationAccessor;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchText = string.Empty;

    [ObservableProperty] private bool _caseSensitive;
    [ObservableProperty] private bool _inLinkDisplaySetting;
    [ObservableProperty] private bool _inTranslations;
    [ObservableProperty] private bool _inNodeComments;
    [ObservableProperty] private string _statusText = string.Empty;

    public IReadOnlyList<FindMatchRow> Results { get; private set; } = [];

    private bool _hasSearched;

    /// Why the results list is empty, or "" when it has rows (GitHub issue 254).
    public string EmptyMessage => Results.Count > 0 ? ""
        : Loc.Get(_hasSearched ? "EmptyState_Find_NoMatches" : "EmptyState_Find_NotSearched");

    /// What to do to fill the results list, or "" when it has rows.
    public string EmptyHint => Results.Count > 0 ? ""
        : Loc.Get(_hasSearched ? "EmptyState_Find_NoMatchesHint" : "EmptyState_Find_NotSearchedHint");

    /// Raised with (conversationName, nodeId) when the user activates a result.
    public event Action<string, int>? RequestNavigate;

    private bool CanSearch() => !string.IsNullOrEmpty(SearchText);

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private void Search()
    {
        var (name, snap) = _openAccessor();
        var query = new ProjectFindQuery(SearchText, CaseSensitive, InLinkDisplaySetting, InTranslations, InNodeComments);
        Results = ProjectFindService.Search(_project, _provider, _primaryLanguage, query, name, snap);
        StatusText = Results.Count > 0
            ? Loc.FormatCount("FindInProject_Matches", Results.Count)
            : Loc.Get("FindInProject_NoMatches");
        _hasSearched = true;
        OnPropertyChanged(nameof(Results));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(EmptyHint));
    }

    public void NavigateTo(FindMatchRow row) => RequestNavigate?.Invoke(row.ConversationName, row.NodeId);
}
