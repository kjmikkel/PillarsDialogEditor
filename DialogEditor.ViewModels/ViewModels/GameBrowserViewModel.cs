using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class GameBrowserViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;

    public GameBrowserViewModel(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ObservableCollection<ConversationFolderViewModel> Folders { get; } = [];

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
        }
        catch (OperationCanceledException) { }
    }

    public IEnumerable<ConversationFolderViewModel> FilteredFolders
    {
        get
        {
            var q = FilterText.Trim();
            if (string.IsNullOrEmpty(q)) return Folders;

            return Folders
                .Select(f => (folder: f, matches: f.Items
                    .Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .ToList()))
                .Where(t => t.matches.Count > 0)
                .Select(t => new ConversationFolderViewModel(t.folder.FolderPath, t.matches, isExpanded: true));
        }
    }

    public event Action<ConversationFile>? ConversationSelected;

    [ObservableProperty]
    private ConversationItemViewModel? _selectedItem;

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
        var folders = Folders.ToList();
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
        var folders = Folders.ToList();
        for (int i = 0; i < folders.Count; i++)
        {
            folders[i].IsExpanded = false;
            if (i % 5 == 4)
                await _dispatcher.YieldToBackground();
        }
    }

    public void Load(IGameDataProvider provider)
    {
        _filterCts?.Cancel();
        GameName = provider.GameName;
        FilterText = string.Empty;
        Folders.Clear();

        var byFolder = provider.EnumerateConversations()
            .GroupBy(f => f.FolderPath)
            .OrderBy(g => g.Key);

        foreach (var group in byFolder)
        {
            var folder = new ConversationFolderViewModel(group.Key);
            foreach (var file in group)
                folder.Items.Add(new ConversationItemViewModel(file));
            Folders.Add(folder);
        }
    }
}
