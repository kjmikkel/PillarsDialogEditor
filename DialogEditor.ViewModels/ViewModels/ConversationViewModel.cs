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

    /// Set by MainWindowViewModel so the node context menu can reach the import/vo commands.
    public NodeDetailViewModel? Detail { get; set; }

    private string? _projectPath;
    public string? ProjectPath
    {
        get => _projectPath;
        set { _projectPath = value; BatchImportVoCommand.NotifyCanExecuteChanged(); }
    }

    public Func<Task>? ShowBatchVoImport { get; set; }

    public bool CanBatchImportVo =>
        ProjectPath is not null && ShowBatchVoImport is not null &&
        Nodes.Any(n => n.HasVO || !string.IsNullOrEmpty(n.ExternalVO));

    [RelayCommand(CanExecute = nameof(CanBatchImportVo))]
    private async Task BatchImportVo()
    {
        if (ShowBatchVoImport is not null)
            await ShowBatchVoImport();
    }

    private readonly HashSet<NodeViewModel> _subscribedNodes = [];

    public ConversationViewModel(IDispatcher dispatcher)
    {
        _dispatcher       = dispatcher;
        PendingConnection = new PendingConnectionViewModel(this);
        // Node field edits from the detail pane reach the stack via NodeViewModel.Push
        // without passing through any canvas method, so dirty-flag centrally here.
        _undoStack.CommandExecuted += () => IsModified = true;
        LocaleService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocaleService.Revision))
                OnPropertyChanged(string.Empty);
        };
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
            BatchImportVoCommand.NotifyCanExecuteChanged();
        };
    }

    public ObservableCollection<NodeViewModel>       Nodes       { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];
    public ObservableCollection<AnnotationViewModel> Annotations { get; } = [];

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
                node.SearchMatchState = SearchMatchState.None;
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
                results[i].node.SearchMatchState =
                    results[i].match ? SearchMatchState.None : SearchMatchState.Dimmed;
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

    /// <param name="baseSnapshot">
    /// Optional explicit diff baseline. When the canvas displays a *patched* state
    /// (project edits applied on top of the vanilla game file), the baseline must be
    /// the vanilla snapshot — not the displayed state — so SaveProject's re-diff
    /// reproduces the full patch instead of only this session's delta. Null keeps
    /// the old behaviour: baseline = the loaded state itself.
    /// </param>
    /// Resets the canvas to the empty no-conversation state: no nodes, clean
    /// undo/dirty state, no baseline. Used by Close Project — a closed project's
    /// patched content must not stay visible — and by Load() before populating.
    public void Clear()
    {
        ConversationName = "";
        _searchCts?.Cancel();
        _undoStack.Clear();
        IsModified  = false;
        Nodes.Clear();
        Connections.Clear();
        Annotations.Clear();
        SelectedNode = null;
        SearchQuery  = string.Empty;
        BaseSnapshot = null;
        RefreshUndoRedo();
    }

    public void Load(Conversation conversation, ConversationEditSnapshot? baseSnapshot = null)
    {
        Clear();
        ConversationName = conversation.Name;

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
        BaseSnapshot = baseSnapshot ?? BuildSnapshot();
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
        // A node must never carry two links to the same target: DiffEngine keys a
        // node's links by ToNodeId, so a duplicate makes every save of the
        // conversation throw (B-009). The drag/keyboard paths pre-check, but this
        // funnel guards every caller (e.g. the detail panel's add-link).
        if (Connections.Any(c => c.Source == source && c.Target == target))
            return;

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

    /// Next free node ID, skipping IDs that still own a _vo/ voice-over file —
    /// reusing such an ID would silently attach the deleted node's audio to the
    /// new node (file names are <conversation>_<id:0000>.wem).
    public int NextNodeId() =>
        NodeIdAllocator.Next(Nodes.Select(n => n.NodeId), IsNodeIdReservedByVo);

    private bool IsNodeIdReservedByVo(int id)
    {
        if (ProjectPath is null || string.IsNullOrEmpty(ConversationName)) return false;
        var voDir = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo");
        if (!Directory.Exists(voDir)) return false;
        var fileName = $"{ConversationName.ToLowerInvariant()}_{id:0000}.wem";
        try
        {
            return Directory.EnumerateFiles(voDir, fileName, SearchOption.AllDirectories).Any();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"VO reservation check failed for id {id}: {ex.Message}");
            return false;
        }
    }

    public void AddConnectedNode(NodeViewModel parent, LayoutPoint position)
    {
        var newId  = NextNodeId();
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

    /// Raised with a node's speaker GUID when the user asks to browse that
    /// character's lines. MainWindowViewModel subscribes and opens the browser
    /// (it owns the project/provider and the dirty guard). Read-only, so unlike
    /// the editing commands above it isn't gated on IsEditable.
    public event Action<string>? RequestBrowseSpeakerLines;

    [RelayCommand]
    private void BrowseSpeakerLinesForNode(NodeViewModel? node)
    {
        if (node is not null && !string.IsNullOrWhiteSpace(node.SpeakerGuid))
            RequestBrowseSpeakerLines?.Invoke(node.SpeakerGuid);
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

    // ── Annotation helpers ────────────────────────────────────────────────
    public void AddAnnotation(AnnotationViewModel annotation)
    {
        annotation.UndoStack = _undoStack;
        _undoStack.Execute(new AddAnnotationCommand(this, annotation));
        IsModified = true;
    }

    public void DeleteAnnotation(AnnotationViewModel annotation)
    {
        _undoStack.Execute(new DeleteAnnotationCommand(this, annotation));
        IsModified = true;
    }

    public IReadOnlyList<Core.Editing.AnnotationSnapshot> GetCurrentAnnotations() =>
        [.. Annotations.Select(a => a.ToSnapshot())];

    public void RestoreAnnotations(IReadOnlyList<Core.Editing.AnnotationSnapshot> snapshots)
    {
        Annotations.Clear();
        foreach (var s in snapshots)
        {
            var vm = AnnotationViewModel.FromSnapshot(s);
            vm.UndoStack = _undoStack;
            Annotations.Add(vm);
        }
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

    public IReadOnlyList<BatchVoRowViewModel> BuildBatchVoRows(string gameRoot, string activeGameId)
    {
        if (ProjectPath is null) return [];

        var voRoot = Path.Combine(gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        var voDir  = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo");

        var rows = new List<BatchVoRowViewModel>();
        foreach (var node in Nodes)
        {
            var check = VoPathResolver.Check(
                node.SpeakerGuid, node.HasVO, node.ExternalVO, node.HasFemaleText,
                node.NodeId, ConversationName, gameRoot, activeGameId);

            if (check is null || check.Status == VoPresence.NotApplicable) continue;
            if (check.PrimaryWemPath is null) continue;

            var rel         = Path.GetRelativePath(voRoot, check.PrimaryWemPath);
            var destPrimary = Path.Combine(voDir, rel);
            var destFem     = Path.Combine(voDir, rel[..^4] + "_fem.wem");

            var raw     = node.DefaultText.Trim();
            var preview = raw.Length == 0  ? Loc.Format("BatchVoImport_NodeFallback", node.NodeId)
                        : raw.Length <= 60 ? raw
                                           : raw[..60] + "…";

            rows.Add(new BatchVoRowViewModel(
                ConversationName, node.NodeId, preview, check.Status, destPrimary, destFem,
                isAliased: !string.IsNullOrEmpty(node.ExternalVO)));
        }

        return rows.OrderBy(r => r.NodeId).ToList();
    }
}
