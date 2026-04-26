using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.GameData;

namespace DialogEditor.WPF.ViewModels;

public partial class GameBrowserViewModel : ObservableObject
{
    public ObservableCollection<ConversationFolderViewModel> Folders { get; } = [];

    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearFilterCommand))]
    private string _filterText = string.Empty;

    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _scanCts;

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
                await Application.Current.Dispatcher.InvokeAsync(
                    static () => { }, DispatcherPriority.Background);
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
                await Application.Current.Dispatcher.InvokeAsync(
                    static () => { }, DispatcherPriority.Background);
        }
    }

    public void Load(IGameDataProvider provider)
    {
        _scanCts?.Cancel();
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

        _scanCts = new CancellationTokenSource();
        _ = ScanLinksAsync(_scanCts.Token);
    }

    private async Task ScanLinksAsync(CancellationToken ct)
    {
        try
        {
            var allItems = Folders.SelectMany(f => f.Items).ToList();
            await Task.Run(() =>
            {
                Parallel.ForEach(allItems,
                    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                    item =>
                    {
                        var (hasNever, hasAlways) = QuickScan(item.File.ConversationPath);
                        if (hasNever || hasAlways)
                        {
                            Application.Current.Dispatcher.BeginInvoke(() =>
                            {
                                if (ct.IsCancellationRequested) return;
                                item.HasNeverLinks  = hasNever;
                                item.HasAlwaysLinks = hasAlways;
                            });
                        }
                    });
            }, ct);
        }
        catch (OperationCanceledException) { }
    }

    private static (bool hasNever, bool hasAlways) QuickScan(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            if (path.EndsWith(".conversationbundle", StringComparison.OrdinalIgnoreCase))
            {
                // PoE2 JSON: QuestionNodeTextDisplay 1=Always, 2=Never
                return (
                    hasNever:  content.Contains("\"QuestionNodeTextDisplay\":2") ||
                               content.Contains("\"QuestionNodeTextDisplay\": 2"),
                    hasAlways: content.Contains("\"QuestionNodeTextDisplay\":1") ||
                               content.Contains("\"QuestionNodeTextDisplay\": 1")
                );
            }
            // PoE1 XML
            return (
                hasNever:  content.Contains("<QuestionNodeTextDisplay>Never</QuestionNodeTextDisplay>"),
                hasAlways: content.Contains("<QuestionNodeTextDisplay>Always</QuestionNodeTextDisplay>")
            );
        }
        catch { return (false, false); }
    }
}
