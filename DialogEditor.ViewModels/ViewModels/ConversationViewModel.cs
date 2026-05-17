using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Editing;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class ConversationViewModel : ObservableObject
{
    private readonly IDispatcher    _dispatcher;
    private readonly UndoRedoStack  _undoStack = new();

    public PendingConnectionViewModel PendingConnection { get; }

    public ConversationViewModel(IDispatcher dispatcher)
    {
        _dispatcher       = dispatcher;
        PendingConnection = new PendingConnectionViewModel(this);
        Nodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NodeCountText));
    }

    public ObservableCollection<NodeViewModel>      Nodes       { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    public string NodeCountText => Loc.Format("Node_CountFormat", Nodes.Count);

    [ObservableProperty]
    private NodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearSearchCommand))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isModified;

    // ── Undo/Redo state ───────────────────────────────────────────────────
    public bool    CanUndo         => _undoStack.CanUndo;
    public bool    CanRedo         => _undoStack.CanRedo;
    public string? UndoDescription => _undoStack.UndoDescription;
    public string? RedoDescription => _undoStack.RedoDescription;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo()
    {
        _undoStack.Undo();
        RefreshUndoRedo();
        IsModified = _undoStack.CanUndo || Connections.Count > 0 || Nodes.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    public void Redo()
    {
        _undoStack.Redo();
        RefreshUndoRedo();
        IsModified = true;
    }

    private void RefreshUndoRedo()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoDescription));
        OnPropertyChanged(nameof(RedoDescription));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    // ── Search ────────────────────────────────────────────────────────────
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
            var nodes   = Nodes.ToList();
            var results = await Task.Run(
                () => nodes.Select(n => (node: n, match: Matches(n, q))).ToList(), ct);
            for (int i = 0; i < results.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                results[i].node.IsSearchMatch = results[i].match;
                if (i % 25 == 24)
                    await _dispatcher.YieldToBackground();
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

    // ── Load ──────────────────────────────────────────────────────────────
    public void Load(Conversation conversation)
    {
        _searchCts?.Cancel();
        _undoStack.Clear();
        IsModified  = false;
        Nodes.Clear();
        Connections.Clear();
        SelectedNode = null;
        SearchQuery  = string.Empty;
        RefreshUndoRedo();

        var nodeMap = new Dictionary<int, NodeViewModel>();

        foreach (var node in conversation.Nodes)
        {
            var entry = conversation.Strings.Get(node.NodeId);
            var vm    = new NodeViewModel(node, entry);
            vm.OnSelected = n => SelectedNode = n;
            vm.UndoStack  = _undoStack;
            // Wire Owner on connectors so GetNodeId() works
            vm.Input.Owner  = vm;
            vm.Output.Owner = vm;
            nodeMap[node.NodeId] = vm;
            Nodes.Add(vm);
        }

        AutoLayoutService.Apply(conversation.Nodes, (id, x, y) =>
        {
            if (nodeMap.TryGetValue(id, out var vm))
                vm.Location = new LayoutPoint(x, y);
        });

        foreach (var node in conversation.Nodes)
        {
            foreach (var link in node.Links)
            {
                if (nodeMap.TryGetValue(link.FromNodeId, out var src) &&
                    nodeMap.TryGetValue(link.ToNodeId,   out var tgt))
                {
                    Connections.Add(new ConnectionViewModel(src.Output, tgt.Input,
                        link.QuestionNodeTextDisplay));
                }
            }
        }
    }

    // ── Structural edit methods ───────────────────────────────────────────
    public void AddNode(NodeViewModel node, LayoutPoint position)
    {
        node.Location   = position;
        node.UndoStack  = _undoStack;
        node.OnSelected = n => SelectedNode = n;
        node.Input.Owner  = node;
        node.Output.Owner = node;
        _undoStack.Execute(new AddNodeCommand(this, node));
        IsModified = true;
        RefreshUndoRedo();
    }

    public void DeleteNode(NodeViewModel node)
    {
        var removed = Connections
            .Where(c => c.Source.Owner == node || c.Target.Owner == node)
            .ToList();
        _undoStack.Execute(new DeleteNodeCommand(this, node, removed));
        IsModified = true;
        RefreshUndoRedo();
    }

    public void AddConnection(ConnectorViewModel source, ConnectorViewModel target)
    {
        var conn = new ConnectionViewModel(source, target);
        _undoStack.Execute(new AddConnectionCommand(this, conn));
        IsModified = true;
        RefreshUndoRedo();
    }

    public void DeleteConnection(ConnectionViewModel connection)
    {
        _undoStack.Execute(new DeleteConnectionCommand(this, connection));
        IsModified = true;
        RefreshUndoRedo();
    }

    public void AddConnectedNode(NodeViewModel parent, LayoutPoint position)
    {
        var newId  = NodeIdAllocator.Next(Nodes.Select(n => n.NodeId));
        var newNode = new NodeViewModel(
            new ConversationNode(newId, false, SpeakerCategory.Npc,
                parent.SpeakerGuid, parent.ListenerGuid, [], [], [],
                parent.DisplayType, parent.Persistence),
            new StringEntry(newId, string.Empty, string.Empty));
        newNode.UndoStack  = _undoStack;
        newNode.OnSelected = n => SelectedNode = n;
        newNode.Input.Owner  = newNode;
        newNode.Output.Owner = newNode;
        newNode.Location     = position;

        _undoStack.Execute(new AddNodeCommand(this, newNode));
        _undoStack.Execute(new AddConnectionCommand(this,
            new ConnectionViewModel(parent.Output, newNode.Input)));
        IsModified   = true;
        SelectedNode = newNode;
        RefreshUndoRedo();
    }

    // ── Relay commands for UI binding (context menus, keyboard) ───────────
    [RelayCommand]
    private void DeleteNodeCmd(NodeViewModel? node)
    {
        if (node is not null) DeleteNode(node);
    }

    [RelayCommand]
    private void AddConnectedNodeCmd(NodeViewModel? parent)
    {
        if (parent is null) return;
        AddConnectedNode(parent, new LayoutPoint(parent.Location.X + 250, parent.Location.Y));
    }

    [RelayCommand]
    private void DeleteConnectionCmd(ConnectionViewModel? connection)
    {
        if (connection is not null) DeleteConnection(connection);
    }

    // ── Snapshot for save ─────────────────────────────────────────────────
    public ConversationEditSnapshot BuildSnapshot() =>
        new(Nodes.Select(n =>
        {
            var links = Connections
                .Where(c => c.Source.Owner == n)
                .Select(c => new LinkEditSnapshot(
                    n.NodeId,
                    c.Target.Owner!.NodeId,
                    1f,
                    c.QuestionNodeTextDisplay,
                    c.HasConditions))
                .ToList();
            return n.ToSnapshot(links);
        }).ToList());
}
