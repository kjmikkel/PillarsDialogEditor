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
            if (string.IsNullOrEmpty(q)) return Folders;

            return Folders.Select(f => Filtered(f, q)).OfType<ConversationFolderViewModel>();
        }
    }

    /// <summary>What the tree shows: the (filtered) folders, then the (filtered) top-level
    /// conversations, as a file tree lists folders before files.</summary>
    public IEnumerable<object> FilteredRoots
    {
        get
        {
            var q = FilterText.Trim();
            var items = string.IsNullOrEmpty(q) ? RootItems : RootItems.Where(i => Matches(i, q));
            return FilteredFolders.Cast<object>().Concat(items);
        }
    }

    private static bool Matches(ConversationItemViewModel item, string q) =>
        item.Name.Contains(q, StringComparison.OrdinalIgnoreCase);

    // A copy of the folder holding only matches, at any depth, or null if nothing below it
    // matches. Every folder kept is expanded, so a match deep in a nested folder is in view.
    private static ConversationFolderViewModel? Filtered(ConversationFolderViewModel folder, string q)
    {
        var subfolders = folder.Subfolders.Select(f => Filtered(f, q)).OfType<ConversationFolderViewModel>().ToList();
        var items = folder.Items.Where(i => Matches(i, q)).ToList();
        if (subfolders.Count == 0 && items.Count == 0) return null;

        var copy = new ConversationFolderViewModel(folder.FolderPath, items, isExpanded: true, displayName: folder.DisplayName);
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

        // FilteredRoots is a computed list, not an observable collection: tell the tree it changed.
        OnPropertyChanged(nameof(FilteredFolders));
        OnPropertyChanged(nameof(FilteredRoots));
    }
}
