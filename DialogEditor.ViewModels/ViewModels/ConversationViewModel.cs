using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Conditions;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Editing;
using DialogEditor.ViewModels.Models;
using DialogEditor.ViewModels.Resources;
using DialogEditor.Core.Logging;
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

    // ── Active game (used by search + VO/lookup helpers) ───────────────────
    /// The loaded game (set by MainWindowViewModel on folder load).
    [ObservableProperty] private string _activeGameId = "";

    /// Highlights the given node IDs as condition/script-search matches and dims the rest.
    /// Shares the unified SearchMatchState with the text search (last search wins).
    public void ApplyConditionHighlight(IReadOnlySet<int> matches)
    {
        foreach (var node in Nodes)
            node.SearchMatchState = matches.Contains(node.NodeId)
                ? SearchMatchState.Match
                : SearchMatchState.Dimmed;
    }

    /// Clears search emphasis/dim (Clear button, or a fresh conversation).
    public void ClearConditionHighlight()
    {
        foreach (var node in Nodes)
            node.SearchMatchState = SearchMatchState.None;
    }

    // ── Assumed game state (View > Game State) ───────────────────────────
    private GameState? _assumedState;

    /// The session's assumed game state. Every canvas link judges itself against it and fades
    /// when it cannot fire (#32). NodeDetail also calls SetVerdictContext on the selected
    /// node's links with this same state; that is harmless, as both set identical context.
    public GameState? AssumedState
    {
        get => _assumedState;
        set
        {
            if (ReferenceEquals(_assumedState, value)) return;
            if (_assumedState is not null) _assumedState.Changed -= OnAssumedStateChanged;
            _assumedState = value;
            if (_assumedState is not null) _assumedState.Changed += OnAssumedStateChanged;
            ApplyVerdictContext();
        }
    }

    private void OnAssumedStateChanged(object? sender, EventArgs e)
    {
        foreach (var c in Connections) c.RefreshVerdict();
        RefreshAllShadowing();
    }

    private void ApplyVerdictContext()
    {
        foreach (var c in Connections) c.SetVerdictContext(_assumedState, ActiveGameId);
        RefreshAllShadowing();
    }

    // ── First-passing-link shadowing (issue 353) ─────────────────────────
    // Load adds every link one at a time; refreshing per add would be quadratic, so it
    // suppresses the per-add refresh and calls RefreshAllShadowing once at the end.
    private bool _suppressShadowing;

    public void RefreshAllShadowing()
    {
        // One pass: GroupBy keeps Connections order within each source's group.
        foreach (var group in Connections.Where(c => c.Source.Owner is not null)
                                         .GroupBy(c => c.Source.Owner!).ToList())
            ApplyShadowing(group.Key, group.ToList());
    }

    /// Recomputes which of the source node's links an earlier link hides. Link order is
    /// the order in <see cref="Connections"/>, which is what BuildSnapshot saves.
    public void RefreshShadowing(NodeViewModel source)
        => ApplyShadowing(source, Connections.Where(c => c.Source.Owner == source).ToList());

    private void ApplyShadowing(NodeViewModel source, IReadOnlyList<ConnectionViewModel> links)
    {
        // With no fact set every verdict is Unknown, but a link with no conditions at all still
        // passes for certain, so the rule applies even before View > Game State is used.
        var state = _assumedState is { HasAnyFact: true } ? _assumedState : new GameState();
        var described = links.Select(c => c.Target.Owner is { } t
            ? LinkShadowing.Describe(c.Conditions,
                new ShadowTarget(t.IsPlayerChoice, t.ScriptKind, t.IsQuestionNode, t.Persistence, t.Conditions),
                state, ActiveGameId)
            : null).ToList();
        var hiddenBy = LinkShadowing.HiddenBy(source.PlayType, source.ScriptKind, described);
        for (int i = 0; i < links.Count; i++)
            links[i].ShadowedByNodeId = hiddenBy[i] is { } h ? links[h].Target.Owner?.NodeId : null;
    }

    // ── Link position numbers (issue 359) ──────────────────────────────────
    // Same triggers as shadowing: the numbers are the links' order in Connections.
    public void RefreshAllLinkPositions()
    {
        foreach (var group in Connections.Where(c => c.Source.Owner is not null)
                                         .GroupBy(c => c.Source.Owner!).ToList())
            ApplyLinkPositions(group.ToList());
    }

    private void RefreshLinkPositions(NodeViewModel source)
        => ApplyLinkPositions(Connections.Where(c => c.Source.Owner == source).ToList());

    private static void ApplyLinkPositions(IReadOnlyList<ConnectionViewModel> links)
    {
        for (int i = 0; i < links.Count; i++) links[i].SetLinkPosition(i + 1, links.Count);
    }

    private void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionViewModel.Conditions) && sender is ConnectionViewModel { Source.Owner: { } src })
            RefreshShadowing(src);
    }

    partial void OnActiveGameIdChanged(string value) => ApplyVerdictContext();

    private readonly HashSet<NodeViewModel> _subscribedNodes = [];
    private readonly HashSet<ConnectionViewModel> _subscribedConnections = [];

    public ConversationViewModel(IDispatcher dispatcher)
    {
        Connections.CollectionChanged += (_, args) =>
        {
            // Move Earlier/Later depend on a link's place among its siblings (issue 359); the commands are shared by every
            // menu item, so one raise refreshes them all after any add, delete, undo or move.
            MoveLinkEarlierCommand.NotifyCanExecuteChanged();
            MoveLinkLaterCommand.NotifyCanExecuteChanged();
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                // Clear() reports no OldItems, so release the tracked set.
                foreach (var c in _subscribedConnections) c.PropertyChanged -= OnConnectionChanged;
                _subscribedConnections.Clear();
                if (!_suppressShadowing) { RefreshAllShadowing(); RefreshAllLinkPositions(); }
                return;
            }
            var affected = new HashSet<NodeViewModel>();
            if (args.OldItems is not null)
                foreach (ConnectionViewModel c in args.OldItems)
                {
                    c.PropertyChanged -= OnConnectionChanged;
                    _subscribedConnections.Remove(c);
                    c.ShadowedByNodeId = null;
                    if (c.Source.Owner is { } s) affected.Add(s);
                }
            if (args.NewItems is not null)
                foreach (ConnectionViewModel c in args.NewItems)
                {
                    c.SetVerdictContext(_assumedState, ActiveGameId);
                    c.PropertyChanged += OnConnectionChanged;
                    _subscribedConnections.Add(c);
                    if (c.Source.Owner is { } s) affected.Add(s);
                }
            if (!_suppressShadowing)
                foreach (var s in affected) { RefreshShadowing(s); RefreshLinkPositions(s); }
        };
        _dispatcher       = dispatcher;
        PendingConnection = new PendingConnectionViewModel(this);
        // Node field edits from the detail pane reach the stack via NodeViewModel.Push
        // without passing through any canvas method, so dirty-flag centrally here.
        _undoStack.CommandExecuted += () => IsModified = true;
        Nodes.CollectionChanged += (_, _) => RefreshStartNode();
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
        // The numbers show only while the link's source is selected (issue 359).
        if (e.PropertyName == nameof(NodeViewModel.IsSelected) && sender is NodeViewModel picked)
            foreach (var c in Connections)
                if (c.Source.Owner == picked) c.NotifySourceSelectionChanged();

        // A node's own conditions gate every link leading to it, so those links re-judge (#32).
        if (e.PropertyName == nameof(NodeViewModel.Conditions) && sender is NodeViewModel changed)
            foreach (var c in Connections)
                if (c.Target.Owner == changed) c.RefreshVerdict();

        // What a link leads to (its conditions, persistence, kind) decides whether it is certain,
        // so every source with a link into the node re-judges which later links it hides.
        if (e.PropertyName is nameof(NodeViewModel.Conditions) or nameof(NodeViewModel.Persistence)
                           or nameof(NodeViewModel.IsPlayerChoice) && sender is NodeViewModel target)
            foreach (var src in Connections.Where(c => c.Target.Owner == target)
                                           .Select(c => c.Source.Owner).OfType<NodeViewModel>().Distinct().ToList())
                RefreshShadowing(src);

        if (e.PropertyName is nameof(NodeViewModel.DefaultText)
                           or nameof(NodeViewModel.FemaleText)
                           or nameof(NodeViewModel.IsPlayerChoice)
                           or nameof(NodeViewModel.SpeakerCategory))
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
            // By node type, as Flow Analytics counts them (GitHub issue 256): "NPC" used to
            // mean every non-player node, Narrator and Script included.
            int player = 0, npc = 0, narrator = 0, script = 0, words = 0, femaleWords = 0;
            foreach (var n in Nodes)
            {
                switch (n.SpeakerCategory)
                {
                    case SpeakerCategory.Player:   player++;   break;
                    case SpeakerCategory.Npc:      npc++;      break;
                    case SpeakerCategory.Narrator: narrator++; break;
                    case SpeakerCategory.Script:   script++;   break;
                }
                words       += WordCount.Of(n.DefaultText);
                femaleWords += WordCount.Of(n.FemaleText);
            }
            return new ConversationStatistics(Nodes.Count, player, npc, narrator, script, words, femaleWords);
        }
    }

    public string StatisticsText
    {
        get
        {
            var s = Statistics;
            return Loc.Format("Statistics_Summary",
                s.NodeCount, s.PlayerCount, s.NpcCount, s.NarratorCount, s.ScriptCount, s.WordCount);
        }
    }

    public string StatisticsTooltip
    {
        get
        {
            var s = Statistics;
            // The explanation of what the totals count stays when the female count is added.
            return s.FemaleWordCount > 0
                ? Loc.Format("Statistics_FemaleDetail", Loc.Get("ToolTip_Statistics"), s.FemaleWordCount)
                : Loc.Get("ToolTip_Statistics");
        }
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
    [NotifyCanExecuteChangedFor(nameof(MoveLinkEarlierCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveLinkLaterCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseStartNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(MakeStartNodeCmdCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    [NotifyPropertyChangedFor(nameof(CanMakeStartNode))]
    private bool _isEditable;

    /// Reading mode (issue 234), set by MainWindowViewModel alongside IsEditable. IsEditable is false
    /// in reading mode, but also with no project open, where the editing menu items stay as disabled
    /// entries; reading mode hides them outright, which needs its own flag.
    [ObservableProperty]
    private bool _isReadingMode;

    // ── Undo/Redo state ───────────────────────────────────────────────────
    public bool    CanUndo         => _undoStack.CanUndo;
    public bool    CanRedo         => _undoStack.CanRedo;
    public string? UndoDescription => _undoStack.UndoDescription;
    public string? RedoDescription => _undoStack.RedoDescription;

    // Also gated on IsEditable (issue 234): a read-only canvas must not change through Ctrl+Z / Ctrl+Y.
    private bool CanRunUndo() => CanUndo && IsEditable;
    private bool CanRunRedo() => CanRedo && IsEditable;

    [RelayCommand(CanExecute = nameof(CanRunUndo))]
    public void Undo()
    {
        _undoStack.Undo();
        RefreshUndoRedo();
        IsModified = _undoStack.CanUndo || Connections.Count > 0 || Nodes.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanRunRedo))]
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
        _pendingRemaps.Clear();
        IsChoosingStartNode = false;
        CanChooseStartNode  = false;
        RefreshUndoRedo();
    }

    public void Load(Conversation conversation, ConversationEditSnapshot? baseSnapshot = null,
                     bool? stringTableMissing = null)
    {
        Clear();
        ConversationName = conversation.Name;

        var nodeMap = new Dictionary<int, NodeViewModel>();
        // The patched-load path rebuilds Strings from the snapshot, which loses the
        // "table missing" state — callers pass it explicitly then (#84).
        var tableMissing = stringTableMissing ?? conversation.Strings.IsMissing;

        foreach (var node in conversation.Nodes)
        {
            var entry = conversation.Strings.Get(node.NodeId);
            var vm    = new NodeViewModel(node, entry, tableMissing);
            vm.OnSelected = n => SelectedNode = n;
            vm.UndoStack  = _undoStack;
            // Wire Owner on connectors so GetNodeId() works
            vm.Input.Owner  = vm;
            vm.Output.Owner = vm;
            nodeMap[node.NodeId] = vm;
            Nodes.Add(vm);
        }

        // Cards are laid out by their estimated height (issue 82), from the text each one shows.
        AutoLayoutService.Apply(conversation.Nodes,
            id => CardMetrics.EstimateHeight(nodeMap.TryGetValue(id, out var n) ? n.DefaultText : null),
            (id, x, y) =>
            {
                if (nodeMap.TryGetValue(id, out var vm))
                    vm.Location = new LayoutPoint(x, y);
            });

        _suppressShadowing = true;
        try
        {
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
        }
        finally { _suppressShadowing = false; }
        RefreshAllShadowing();
        RefreshAllLinkPositions();
        BaseSnapshot = baseSnapshot ?? BuildSnapshot();
        ConversationLoaded?.Invoke(this, EventArgs.Empty);
    }

    /// Raised at the end of Load, once nodes, layout and links are in place. The canvas view
    /// frames the new conversation on it (GitHub issue 246).
    public event EventHandler? ConversationLoaded;

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

    // ── Start node (issue 172) ────────────────────────────────────────────
    // Both games start a conversation at node 0. A conversation the project created can have
    // a chosen node renumbered to 0 as one undoable edit.

    /// False only when the conversation has nodes and none is node 0: the game would have
    /// nowhere to start it.
    [ObservableProperty] private bool _hasStartNode = true;

    /// Whether the canvas shows its minimap (GitHub issue 264). MainWindowViewModel.ShowMinimap
    /// loads and saves it, so a canvas made in a test never touches the settings file.
    [ObservableProperty] private bool _showMinimap = true;

    /// Set by MainWindowViewModel: true for conversations created in this project. Renumbering
    /// a game conversation's node would turn it into a delete plus an add in the patch.
    [ObservableProperty] private bool _canChooseStartNode;

    public event EventHandler<StartNodeChangedEventArgs>? StartNodeAttempted;

    // ID changes not yet folded into the stored patch's translations (NodeIdRemap).
    private readonly List<(int From, int To)> _pendingRemaps = [];

    internal IReadOnlyList<(int From, int To)> PeekPendingRemaps() => _pendingRemaps.ToList();

    internal IReadOnlyList<(int From, int To)> TakePendingRemaps()
    {
        var taken = _pendingRemaps.ToList();
        _pendingRemaps.Clear();
        return taken;
    }

    private void RefreshStartNode() =>
        HasStartNode = Core.Analytics.StartNode.IsPresent(Nodes.Select(n => n.NodeId));

    public StartNodeOutcome MakeStartNode(NodeViewModel node)
    {
        var oldId = node.NodeId;
        var oldVo = string.IsNullOrEmpty(node.ExternalVO)
            ? VoPathResolver.ExpectedRelativePath(node.SpeakerGuid, "", oldId, ConversationName)
            : null;
        StartNodeOutcome Report(StartNodeOutcome outcome, string detail = "")
        {
            StartNodeAttempted?.Invoke(this, new StartNodeChangedEventArgs(outcome, oldId, oldVo, detail));
            return outcome;
        }

        if (!IsEditable || !CanChooseStartNode || !Nodes.Contains(node))
            return Report(StartNodeOutcome.NotAllowed);
        if (HasStartNode)
            return Report(StartNodeOutcome.StartNodeExists);

        var vo = StartNodeVoFiles.For(ProjectPath, node, ConversationName, oldId, Core.Analytics.StartNode.Id);
        if (vo?.ExistingTarget is { } taken)
            return Report(StartNodeOutcome.VoTargetExists, taken);

        try
        {
            _undoStack.Execute(new MakeStartNodeCommand(this, node, oldId, ProjectPath));
        }
        catch (Exception ex)   // StartNodeVoFiles has logged it and put the files back
        {
            AppLog.Warn($"Make start node: node {oldId} unchanged: {ex.Message}");
            return Report(StartNodeOutcome.VoRenameFailed, ex.Message);
        }
        IsModified = true;
        RefreshUndoRedo();
        return Report(StartNodeOutcome.Made);
    }

    // Pick mode: the warning strip's "Choose start node…" arms it; clicking a node, or Enter
    // on the selected one, then makes that node the start node (TryConfirmStartNode).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartNodeWarningText))]
    private bool _isChoosingStartNode;

    public bool ShowStartNodeWarning => !HasStartNode;

    /// For the right-click item and the strip's button: the fix is on offer.
    public bool CanMakeStartNode => IsEditable && CanChooseStartNode && !HasStartNode;

    public string StartNodeWarningText =>
        IsChoosingStartNode ? Loc.Get("Canvas_ChooseStartNode_Prompt")
        : CanChooseStartNode ? Loc.Get("Canvas_NoStartNode_New")
        : Loc.Get("Canvas_NoStartNode_Game");

    [RelayCommand(CanExecute = nameof(CanMakeStartNode))]
    private void ChooseStartNode() => IsChoosingStartNode = true;

    [RelayCommand]
    private void CancelChooseStartNode() => IsChoosingStartNode = false;

    /// Confirms the picked node: a click on a node, or Enter on the selected one. Selection
    /// alone never commits; keyboard focus, navigation and new nodes all move it without the
    /// user choosing anything (as in connect mode). False outside pick mode.
    public bool TryConfirmStartNode(NodeViewModel? node)
    {
        if (!IsChoosingStartNode || node is null) return false;
        IsChoosingStartNode = false;
        MakeStartNode(node);
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanMakeStartNode))]
    private void MakeStartNodeCmd(NodeViewModel? node)
    {
        if (node is not null) MakeStartNode(node);
    }

    partial void OnHasStartNodeChanged(bool value)
    {
        if (value) IsChoosingStartNode = false;
        StartNodeStateChanged();
    }

    partial void OnCanChooseStartNodeChanged(bool value) => StartNodeStateChanged();

    private void StartNodeStateChanged()
    {
        OnPropertyChanged(nameof(ShowStartNodeWarning));
        OnPropertyChanged(nameof(CanMakeStartNode));
        OnPropertyChanged(nameof(StartNodeWarningText));
        ChooseStartNodeCommand.NotifyCanExecuteChanged();
        MakeStartNodeCmdCommand.NotifyCanExecuteChanged();
    }

    internal string? TakeNodeComment(int nodeId) =>
        _nodeComments.Remove(nodeId, out var comment) ? comment : null;

    internal void RestoreNodeComment(int nodeId, string comment) => _nodeComments[nodeId] = comment;

    internal void ApplyRenumber(NodeViewModel node, int from, int to)
    {
        node.Renumber(to);
        if (_nodeComments.Remove(from, out var comment)) _nodeComments[to] = comment;
        _pendingRemaps.Add((from, to));
        RefreshStartNode();
        IsModified = true;
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
        AddConnectedNode(parent, new LayoutPoint(parent.Location.X + CardMetrics.Width + CardMetrics.ColumnGap, parent.Location.Y));
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void DeleteConnectionCmd(ConnectionViewModel? connection)
    {
        if (connection is not null) DeleteConnection(connection);
    }

    // ── Link order (issue 359) ────────────────────────────────────────────
    // A node's link order is the order of its connections in Connections, and the game takes the first
    // link that passes. A move swaps the link with the neighbouring link of the same node; connections of
    // other nodes between them keep their indices.
    public bool CanMoveLinkEarlier(ConnectionViewModel? connection) => IsEditable && SiblingOf(connection, -1) is not null;

    public bool CanMoveLinkLater(ConnectionViewModel? connection) => IsEditable && SiblingOf(connection, +1) is not null;

    private ConnectionViewModel? SiblingOf(ConnectionViewModel? connection, int direction)
    {
        if (connection is null || connection.Source.Owner is not { } source) return null;
        var at = Connections.IndexOf(connection);
        if (at < 0) return null;
        for (int i = at + direction; i >= 0 && i < Connections.Count; i += direction)
            if (Connections[i].Source.Owner == source) return Connections[i];
        return null;
    }

    [RelayCommand(CanExecute = nameof(CanMoveLinkEarlier))]
    private void MoveLinkEarlier(ConnectionViewModel? connection) => MoveLink(connection, -1);

    [RelayCommand(CanExecute = nameof(CanMoveLinkLater))]
    private void MoveLinkLater(ConnectionViewModel? connection) => MoveLink(connection, +1);

    private void MoveLink(ConnectionViewModel? connection, int direction)
    {
        if (SiblingOf(connection, direction) is not { } sibling) return;
        _undoStack.Execute(new MoveLinkCommand(this, connection!, sibling));
        IsModified = true;
        RefreshUndoRedo();
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

        var voRoot = VoPathResolver.VoicesRoot(gameRoot);
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
