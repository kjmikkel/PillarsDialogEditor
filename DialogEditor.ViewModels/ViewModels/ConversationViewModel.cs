using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Editing;
using DialogEditor.ViewModels.Models;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class ConversationViewModel : ObservableObject
{
    private readonly IDispatcher    _dispatcher;
    private readonly UndoRedoStack  _undoStack = new();

    public PendingConnectionViewModel PendingConnection { get; }

    private readonly HashSet<NodeViewModel> _subscribedNodes = [];

    public ConversationViewModel(IDispatcher dispatcher)
    {
        _dispatcher       = dispatcher;
        PendingConnection = new PendingConnectionViewModel(this);
        Nodes.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                // Clear() fires Reset with OldItems=null — unsubscribe via tracked set
                foreach (var n in _subscribedNodes)
                    n.PropertyChanged -= OnNodeTextChanged;
                _subscribedNodes.Clear();
            }
            else
            {
                if (args.NewItems is not null)
                    foreach (NodeViewModel n in args.NewItems)
                    {
                        n.PropertyChanged += OnNodeTextChanged;
                        _subscribedNodes.Add(n);
                    }
                if (args.OldItems is not null)
                    foreach (NodeViewModel n in args.OldItems)
                    {
                        n.PropertyChanged -= OnNodeTextChanged;
                        _subscribedNodes.Remove(n);
                    }
            }
            RefreshStatistics();
        };
    }

    public ObservableCollection<NodeViewModel>      Nodes       { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    private void OnNodeTextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodeViewModel.DefaultText)
                           or nameof(NodeViewModel.FemaleText)
                           or nameof(NodeViewModel.IsPlayerChoice))
            RefreshStatistics();
    }

    private void RefreshStatistics()
    {
        OnPropertyChanged(nameof(Statistics));
        OnPropertyChanged(nameof(StatisticsText));
        OnPropertyChanged(nameof(StatisticsTooltip));
    }

    public ConversationStatistics Statistics
    {
        get
        {
            int npc = 0, player = 0, words = 0, femaleWords = 0;
            foreach (var n in Nodes)
            {
                if (n.IsPlayerChoice) player++; else npc++;
                words       += CountWords(n.DefaultText);
                femaleWords += CountWords(n.FemaleText);
            }
            return new ConversationStatistics(Nodes.Count, npc, player, words, femaleWords);
        }
    }

    public string StatisticsText
    {
        get
        {
            var s = Statistics;
            return Loc.Format("Statistics_Summary", s.NodeCount, s.NpcCount, s.PlayerCount, s.WordCount);
        }
    }

    public string StatisticsTooltip
    {
        get
        {
            var s = Statistics;
            return s.FemaleWordCount > 0
                ? Loc.Format("Statistics_FemaleDetail", s.FemaleWordCount)
                : Loc.Get("ToolTip_Statistics");
        }
    }

    private static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    [ObservableProperty]
    private NodeViewModel? _selectedNode;

    // ── Keyboard "connect mode" (Gaps.md Accessibility item 4 follow-up) ──────
    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private NodeViewModel? _connectionSource;

    /// <summary>
    /// Raised when keyboard connect mode starts, completes with a new connection, or
    /// is cancelled. <see cref="MainWindowViewModel"/> turns this into a
    /// <c>StatusText</c> announcement for the existing live region.
    /// </summary>
    public event EventHandler<ConnectModeEventArgs>? ConnectModeChanged;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearSearchCommand))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteNodeCmdCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddConnectedNodeCmdCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteConnectionCmdCommand))]
    private bool _isEditable;

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
        // Remember the last real selection (mouse or keyboard) so keyboard focus
        // can resume where the user left off (spec: entry = restore last selection).
        if (value is not null) _lastSelection = value;

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

    // ── NodeComments — translator context for each node ───────────────────
    private Dictionary<int, string> _nodeComments = new();

    internal void LoadNodeComments(IReadOnlyDictionary<int, string> comments)
    {
        _nodeComments = new Dictionary<int, string>(comments);
    }

    internal void SetNodeComment(int nodeId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            _nodeComments.Remove(nodeId);
        else
            _nodeComments[nodeId] = comment.Trim();
        IsModified = true;
    }

    internal string GetNodeComment(int nodeId) =>
        _nodeComments.TryGetValue(nodeId, out var c) ? c : string.Empty;

    internal IReadOnlyDictionary<int, string> NodeComments => _nodeComments;

    // ── Base snapshot (used for patch diffing) ────────────────────────────
    internal ConversationEditSnapshot? BaseSnapshot { get; private set; }

    // ── Load ──────────────────────────────────────────────────────────────
    /// Name of the currently loaded conversation; used to key per-node git attribution.
    public string ConversationName { get; private set; } = "";

    public void Load(Conversation conversation)
    {
        ConversationName = conversation.Name;
        _searchCts?.Cancel();
        _undoStack.Clear();
        IsModified  = false;
        Nodes.Clear();
        Connections.Clear();
        SelectedNode = null;
        SearchQuery  = string.Empty;
        BaseSnapshot = null;
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
                    var conn = new ConnectionViewModel(src.Output, tgt.Input,
                        link.QuestionNodeTextDisplay, link.RandomWeight, link.Conditions)
                        { UndoStack = _undoStack };
                    Connections.Add(conn);
                }
            }
        }
        BaseSnapshot = BuildSnapshot();
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
        // Connect mode cannot reference a deleted node.
        if (node == ConnectionSource)
            CancelConnect();

        var removed = Connections
            .Where(c => c.Source.Owner == node || c.Target.Owner == node)
            .ToList();
        _undoStack.Execute(new DeleteNodeCommand(this, node, removed));
        IsModified = true;
        RefreshUndoRedo();
    }

    public void AddConnection(ConnectorViewModel source, ConnectorViewModel target)
    {
        var conn = new ConnectionViewModel(source, target) { UndoStack = _undoStack };
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
            new ConversationNode(newId, false, parent.SpeakerCategory,
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
            new ConnectionViewModel(parent.Output, newNode.Input) { UndoStack = _undoStack }));
        IsModified   = true;
        SelectedNode = newNode;
        RefreshUndoRedo();
    }

    // ── Relay commands for UI binding (context menus, keyboard) ───────────
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void DeleteNodeCmd(NodeViewModel? node)
    {
        if (node is not null) DeleteNode(node);
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void BeginConnectCmd(NodeViewModel? node)
    {
        if (node is not null) BeginConnect(node);
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void AddConnectedNodeCmd(NodeViewModel? parent)
    {
        if (parent is null) return;
        AddConnectedNode(parent, new LayoutPoint(parent.Location.X + 250, parent.Location.Y));
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void DeleteConnectionCmd(ConnectionViewModel? connection)
    {
        if (connection is not null) DeleteConnection(connection);
    }

    // ── Keyboard selection & navigation (spec: 2026-06-12 canvas keyboard) ──
    private NodeViewModel? _lastSelection;

    /// Single selection path for mouse AND keyboard: clears other nodes'
    /// IsSelected (Nodify renders the selection ring from it) and sets
    /// SelectedNode (drives connection highlighting + the detail panel).
    public void SelectNode(NodeViewModel node)
    {
        foreach (var n in Nodes)
            if (!ReferenceEquals(n, node) && n.IsSelected)
                n.IsSelected = false;
        node.IsSelected = true; // OnSelected callback also sets SelectedNode
        SelectedNode = node;    // ...but set explicitly for nodes lacking the callback
    }

    public bool Deselect()
    {
        if (SelectedNode is null) return false;
        SelectedNode.IsSelected = false;
        SelectedNode = null;
        return true;
    }

    /// Keyboard focus arriving on an empty selection resumes at the last
    /// selection; first focus (or a deleted last selection) starts at the root.
    public bool EnsureKeyboardSelection()
    {
        if (SelectedNode is not null) return true;
        if (Nodes.Count == 0) return false;

        var target = _lastSelection is not null && Nodes.Contains(_lastSelection)
            ? _lastSelection
            : Nodes.FirstOrDefault(n => n.NodeId == 0) ?? Nodes[0];
        SelectNode(target);
        return true;
    }

    public bool TrySelectRoot()
    {
        var root = Nodes.FirstOrDefault(n => n.NodeId == 0) ?? Nodes.FirstOrDefault();
        if (root is null) return false;
        SelectNode(root);
        return true;
    }

    public bool TryNavigate(CanvasNavDirection direction)
    {
        if (SelectedNode is null) return false;

        var target = direction switch
        {
            CanvasNavDirection.Parent          => CanvasNavigationService.GetParent(SelectedNode, Nodes, Connections),
            CanvasNavDirection.Child           => CanvasNavigationService.GetChild(SelectedNode, Nodes, Connections),
            CanvasNavDirection.PreviousSibling => CanvasNavigationService.GetSibling(SelectedNode, -1, Nodes, Connections),
            CanvasNavDirection.NextSibling     => CanvasNavigationService.GetSibling(SelectedNode, +1, Nodes, Connections),
            _                                  => null,
        };
        if (target is null) return false;
        SelectNode(target);
        return true;
    }

    public bool TryCycle(bool forward)
    {
        var target = CanvasNavigationService.Cycle(SelectedNode, forward, Nodes);
        if (target is null) return false;
        SelectNode(target);
        return true;
    }

    /// Keyboard nudge has drag-move semantics: a plain Location set, no undo
    /// entry (drag moves are not individually undoable today; layout persists
    /// via GetCurrentLayout at save). Gated on IsEditable so read-only canvases
    /// (diff view) cannot be rearranged from the keyboard.
    public bool NudgeSelected(double dx, double dy)
    {
        if (!IsEditable || SelectedNode is null) return false;
        SelectedNode.Location = new LayoutPoint(SelectedNode.Location.X + dx, SelectedNode.Location.Y + dy);
        return true;
    }

    // ── Keyboard connect mode ────────────────────────────────────────────────
    /// <summary>
    /// Starts connect mode with <paramref name="node"/> as the source. The source
    /// becomes the initial target-candidate too — arrow keys then move the target
    /// candidate away from it (spec decision: entry = SelectNode(source)).
    /// No-op (returns false) if the canvas isn't editable or connect mode is already
    /// active — the user must confirm or cancel the current session first.
    /// </summary>
    public bool BeginConnect(NodeViewModel node)
    {
        if (!IsEditable || IsConnecting) return false;

        SelectNode(node);
        ConnectionSource = node;
        node.IsConnectionSource = true;
        IsConnecting = true;

        ConnectModeChanged?.Invoke(this, new ConnectModeEventArgs(ConnectModeChange.Started, node, null));
        return true;
    }

    /// <summary>Starts connect mode using <see cref="SelectedNode"/> as the source.</summary>
    public bool TryBeginConnect()
    {
        if (SelectedNode is null) return false;
        return BeginConnect(SelectedNode);
    }

    /// <summary>
    /// Confirms the connection from <see cref="ConnectionSource"/> to the current
    /// target candidate (<see cref="SelectedNode"/>), called only while
    /// <see cref="IsConnecting"/>. If the target is the source itself or a node
    /// already connected to the source's output, this is a silent no-op and connect
    /// mode remains active (spec decision 2 — matches
    /// <see cref="PendingConnectionViewModel.Complete"/>'s self/duplicate handling).
    /// Always returns true: the key is consumed either way.
    /// </summary>
    public bool TryConfirmConnection()
    {
        var source = ConnectionSource!;
        var target = SelectedNode;

        var isSelf      = target == source;
        var isDuplicate = target is not null &&
            Connections.Any(c => c.Source == source.Output && c.Target == target.Input);

        if (target is not null && !isSelf && !isDuplicate)
        {
            AddConnection(source.Output, target.Input);
            ExitConnectMode();
            ConnectModeChanged?.Invoke(this, new ConnectModeEventArgs(ConnectModeChange.Connected, source, target));
        }

        return true;
    }

    /// <summary>
    /// Cancels connect mode without creating a connection (selection unchanged).
    /// Called only while <see cref="IsConnecting"/>. Always returns true.
    /// </summary>
    public bool CancelConnect()
    {
        var source = ConnectionSource!;
        ExitConnectMode();
        ConnectModeChanged?.Invoke(this, new ConnectModeEventArgs(ConnectModeChange.Cancelled, source, null));
        return true;
    }

    private void ExitConnectMode()
    {
        if (ConnectionSource is not null)
            ConnectionSource.IsConnectionSource = false;
        ConnectionSource = null;
        IsConnecting     = false;
    }

    // ── Layout helpers ────────────────────────────────────────────────────
    public IReadOnlyDictionary<int, LayoutPoint> GetCurrentLayout() =>
        Nodes.ToDictionary(n => n.NodeId, n => n.Location);

    public void RestoreLayout(IReadOnlyDictionary<int, LayoutPoint> positions)
    {
        foreach (var node in Nodes)
            if (positions.TryGetValue(node.NodeId, out var pos))
                node.Location = pos;
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
                    c.RandomWeight,
                    c.QuestionNodeTextDisplay,
                    c.HasConditions)
                    { Conditions = c.Conditions })
                .ToList();
            return n.ToSnapshot(links);
        }).ToList());
}
