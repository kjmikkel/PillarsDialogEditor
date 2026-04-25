using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;

namespace DialogEditor.WPF.ViewModels;

public partial class ConversationViewModel : ObservableObject
{
    public ObservableCollection<NodeViewModel> Nodes { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    [ObservableProperty]
    private NodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearSearchCommand))]
    private string _searchQuery = string.Empty;

    private CancellationTokenSource? _searchCts;

    partial void OnSelectedNodeChanged(NodeViewModel? value)
    {
        foreach (var connection in Connections)
        {
            connection.IsHighlighted = value is not null &&
                (connection.Source == value.Output || connection.Target == value.Input);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();
        var q = value.Trim();
        if (string.IsNullOrEmpty(q))
        {
            foreach (var node in Nodes)
                node.IsSearchMatch = true;
            return;
        }
        _searchCts = new CancellationTokenSource();
        _ = ApplySearchAsync(q, _searchCts.Token);
    }

    private async Task ApplySearchAsync(string q, CancellationToken ct)
    {
        try
        {
            await Task.Delay(150, ct);

            // Compute matches on a background thread — no UI access here
            var nodes = Nodes.ToList();
            var results = await Task.Run(
                () => nodes.Select(n => (node: n, match: Matches(n, q))).ToList(),
                ct);

            // Apply back to UI in small batches, yielding below render priority
            for (int i = 0; i < results.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                results[i].node.IsSearchMatch = results[i].match;
                if (i % 25 == 24)
                    await Application.Current.Dispatcher.InvokeAsync(
                        static () => { }, DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand(CanExecute = nameof(HasSearchQuery))]
    private void ClearSearch() => SearchQuery = string.Empty;
    private bool HasSearchQuery() => !string.IsNullOrEmpty(SearchQuery);

    private static bool Matches(NodeViewModel node, string q) =>
        node.NodeId.ToString() == q ||
        node.DefaultText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        node.SpeakerName.Contains(q, StringComparison.OrdinalIgnoreCase);

    public void Load(Conversation conversation)
    {
        _searchCts?.Cancel();
        Nodes.Clear();
        Connections.Clear();
        SelectedNode = null;
        SearchQuery = string.Empty;

        var nodeMap = new Dictionary<int, NodeViewModel>();

        foreach (var node in conversation.Nodes)
        {
            var entry = conversation.Strings.Get(node.NodeId);
            var vm = new NodeViewModel(node, entry);
            vm.OnSelected = n => SelectedNode = n;
            nodeMap[node.NodeId] = vm;
            Nodes.Add(vm);
        }

        AutoLayoutService.Apply(conversation.Nodes, (id, x, y) =>
        {
            if (nodeMap.TryGetValue(id, out var vm))
                vm.Location = new Point(x, y);
        });

        foreach (var node in conversation.Nodes)
        {
            foreach (var link in node.Links)
            {
                if (nodeMap.TryGetValue(link.FromNodeId, out var src) &&
                    nodeMap.TryGetValue(link.ToNodeId, out var tgt))
                {
                    Connections.Add(new ConnectionViewModel(src.Output, tgt.Input));
                }
            }
        }
    }
}
