using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class GameBrowserViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;

    public GameBrowserViewModel(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>Top-level folders; each nests its subfolders (issue 85, part B).</summary>
    public ObservableCollection<ConversationFolderViewModel> Folders { get; } = [];

    /// <summary>Conversations in the game's top conversations folder, shown at the top level
    /// after the folders rather than in a "(root)" folder.</summary>
    public ObservableCollection<ConversationItemViewModel> RootItems { get; } = [];

    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearFilterCommand))]
    private string _filterText = string.Empty;

    private CancellationTokenSource? _filterCts;

    // ── The open project's conversations (issue 85, part C) ─────────────────

    /// <summary>A project is open, so "Only this project's conversations" is offered.</summary>
    [ObservableProperty]
    private bool _hasProject;

    /// <summary>Narrow the tree to conversations the project changes or adds.</summary>
    [ObservableProperty]
    private bool _onlyProjectConversations;

    partial void OnOnlyProjectConversationsChanged(bool value)
    {
        OnPropertyChanged(nameof(FilteredFolders));
        OnPropertyChanged(nameof(FilteredRoots));
    }

    /// <summary>
    /// Marks the conversations the open project changes (<paramref name="edited"/>: names with a
    /// non-empty patch), or clears every mark when no project is open (<c>null</c>). Called on
    /// every project change, so it only sets flags; the tree itself is rebuilt by
    /// <see cref="Load"/>, which stays tied to the new-conversation list.
    /// </summary>
    public void SetProjectConversations(IReadOnlySet<string>? edited)
    {
        _projectEdited = edited;
        ApplyProjectMarks();
        HasProject = edited is not null;
        if (edited is null) OnlyProjectConversations = false;
        else if (OnlyProjectConversations) OnOnlyProjectConversationsChanged(true);   // marks moved
    }

    // Kept so Load can mark the items it builds: a game folder can load after the project opens.
    private IReadOnlySet<string>? _projectEdited;

    private void ApplyProjectMarks()
    {
        foreach (var item in AllItems())
            item.IsEdited = _projectEdited?.Contains(item.Name) == true;
    }

    private IEnumerable<ConversationItemViewModel> AllItems() =>
        AllFolders().SelectMany(f => f.Items).Concat(RootItems);

    partial void OnFilterTextChanged(string value)
    {
        _filterCts?.Cancel();
        if (string.IsNullOrWhiteSpace(value))
        {
            OnPropertyChanged(nameof(FilteredFolders));
            OnPropertyChanged(nameof(FilteredRoots));
            return;
        }
        _filterCts = new CancellationTokenSource();
        _ = ApplyFilterAsync(_filterCts.Token);
    }

    private async Task ApplyFilterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(150, ct);
            OnPropertyChanged(nameof(FilteredFolders));
            OnPropertyChanged(nameof(FilteredRoots));
        }
        catch (OperationCanceledException) { }
    }

    public IEnumerable<ConversationFolderViewModel> FilteredFolders
    {
        get
        {
            var q = FilterText.Trim();
            if (string.IsNullOrEmpty(q) && !OnlyProjectConversations) return Folders;

            return Folders.Select(f => Filtered(f, i => Matches(i, q))).OfType<ConversationFolderViewModel>();
        }
    }

    /// <summary>What the tree shows: the (filtered) folders, then the (filtered) top-level
    /// conversations, as a file tree lists folders before files.</summary>
    public IEnumerable<object> FilteredRoots
    {
        get
        {
            var q = FilterText.Trim();
            return FilteredFolders.Cast<object>().Concat(RootItems.Where(i => Matches(i, q)));
        }
    }

    // The text filter (empty matches everything) and, when it's on, "only this project's
    // conversations": the ones the project changes or adds.
    private bool Matches(ConversationItemViewModel item, string q) =>
        (q.Length == 0 || item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) &&
        (!OnlyProjectConversations || item.IsEdited || item.IsNew);

    // A copy of the folder holding only matches, at any depth, or null if nothing below it
    // matches. Every folder kept is expanded, so a match deep in a nested folder is in view.
    private static ConversationFolderViewModel? Filtered(ConversationFolderViewModel folder,
                                                         Func<ConversationItemViewModel, bool> keep)
    {
        var subfolders = folder.Subfolders.Select(f => Filtered(f, keep)).OfType<ConversationFolderViewModel>().ToList();
        var items = folder.Items.Where(keep).ToList();
        if (subfolders.Count == 0 && items.Count == 0) return null;

        var copy = new ConversationFolderViewModel(folder.FolderPath, items, isExpanded: true,
                                                   displayName: folder.DisplayName, toolTip: folder.ToolTip);
        foreach (var f in subfolders) copy.Subfolders.Add(f);
        return copy;
    }

    private IEnumerable<ConversationFolderViewModel> AllFolders()
    {
        var stack = new Stack<ConversationFolderViewModel>(Folders.Reverse());
        while (stack.Count > 0)
        {
            var f = stack.Pop();
            yield return f;
            foreach (var s in f.Subfolders.Reverse()) stack.Push(s);
        }
    }

    public event Action<ConversationFile>? ConversationSelected;

    [ObservableProperty]
    private ConversationItemViewModel? _selectedItem;

    // Avalonia TreeView.SelectedItem is typed as object? and may contain folders;
    // this coercing wrapper only fires ConversationSelected for the right type.
    private object? _selectedTreeItem;
    public object? SelectedTreeItem
    {
        get => _selectedTreeItem;
        set
        {
            _selectedTreeItem = value;
            OnPropertyChanged();
            if (value is ConversationItemViewModel item)
                SelectedItem = item;
        }
    }

    partial void OnSelectedItemChanged(ConversationItemViewModel? value)
    {
        if (value is not null)
            ConversationSelected?.Invoke(value.File);
    }

    [RelayCommand(CanExecute = nameof(HasFilterText))]
    private void ClearFilter() => FilterText = string.Empty;
    private bool HasFilterText() => !string.IsNullOrEmpty(FilterText);

    [RelayCommand]
    private async Task ExpandAll()
    {
        var folders = AllFolders().ToList();   // nested folders too (issue 85)
        for (int i = 0; i < folders.Count; i++)
        {
            folders[i].IsExpanded = true;
            if (i % 5 == 4)
                await _dispatcher.YieldToBackground();
        }
    }

    [RelayCommand]
    private async Task CollapseAll()
    {
        var folders = AllFolders().ToList();   // nested folders too (issue 85)
        for (int i = 0; i < folders.Count; i++)
        {
            folders[i].IsExpanded = false;
            if (i % 5 == 4)
                await _dispatcher.YieldToBackground();
        }
    }

    public void Load(IGameDataProvider provider,
                     IReadOnlyList<string>? newConversationNames = null)
    {
        _filterCts?.Cancel();
        GameName = provider.GameName;
        FilterText = string.Empty;
        Folders.Clear();
        RootItems.Clear();

        var tree = ConversationTreeBuilder.Build(provider.EnumerateConversations());
        foreach (var folder in tree.Folders) Folders.Add(folder);
        foreach (var item in tree.RootItems) RootItems.Add(item);

        if (newConversationNames is { Count: > 0 })
        {
            var newFolder = new ConversationFolderViewModel(string.Empty, isExpanded: true,
                                                            displayName: Loc.Get("Label_NewConversationsFolder"));
            foreach (var name in newConversationNames)
            {
                var file = provider.BuildNewConversationFile(name);
                newFolder.Items.Add(new ConversationItemViewModel(file, isNew: true));
            }
            // Insert at the top so new conversations are immediately visible
            Folders.Insert(0, newFolder);
        }

        ApplyProjectMarks();

        // FilteredRoots is a computed list, not an observable collection: tell the tree it changed.
        OnPropertyChanged(nameof(FilteredFolders));
        OnPropertyChanged(nameof(FilteredRoots));
    }
}
