using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Patch.Diff;

namespace DialogEditor.ViewModels;

/// Expandable group of changed lines for one conversation, with a tri-state
/// "select all" checkbox (true / false / null = indeterminate).
public partial class ConversationChangeViewModel : ObservableObject
{
    public string Name { get; }
    public ObservableCollection<NodeChangeViewModel> Nodes { get; } = [];

    [ObservableProperty] private bool _isExpanded;

    private bool _suppressRollDown;

    private IReadOnlyDictionary<int, IReadOnlyList<int>> _outgoing =
        new Dictionary<int, IReadOnlyList<int>>();
    private IReadOnlySet<int> _addedIds = new HashSet<int>();

    /// When true, ticking a node also ticks the added nodes it links to (transitively).
    public bool AutoPullEnabled { get; set; }

    /// Supplies the conversation's outgoing-link map (source node id → target node ids)
    /// and the set of added node ids eligible to be auto-pulled.
    public void SetDependencies(
        IReadOnlyDictionary<int, IReadOnlyList<int>> outgoing, IReadOnlySet<int> addedIds)
    {
        _outgoing = outgoing;
        _addedIds = addedIds;
    }

    public event Action? SelectionChanged;

    public ConversationChangeViewModel(ConversationChange change)
    {
        Name = change.Name;
        foreach (var id in change.Added)    Add(id, DiffStatus.Added);
        foreach (var id in change.Modified) Add(id, DiffStatus.Changed);
        foreach (var id in change.Removed)  Add(id, DiffStatus.Removed);
    }

    private void Add(int id, DiffStatus kind)
    {
        var node = new NodeChangeViewModel(id, kind);
        node.SelectionChanged += () => OnNodeSelectionChanged(node);
        Nodes.Add(node);
    }

    public IReadOnlyList<int> SelectedNodeIds =>
        Nodes.Where(n => n.IsSelected).Select(n => n.NodeId).ToList();

    // null = indeterminate (some but not all selected).
    public bool? IsAllSelected
    {
        get
        {
            var selected = Nodes.Count(n => n.IsSelected);
            if (selected == 0)           return false;
            if (selected == Nodes.Count) return true;
            return null;
        }
        set
        {
            if (value is null) return;
            _suppressRollDown = true;
            foreach (var n in Nodes) n.IsSelected = value.Value;
            _suppressRollDown = false;
            OnPropertyChanged(nameof(IsAllSelected));
            SelectionChanged?.Invoke();
        }
    }

    private void OnNodeSelectionChanged(NodeChangeViewModel node)
    {
        if (_suppressRollDown) return;

        if (node.IsSelected && AutoPullEnabled)
            PullDependencies(node.NodeId);

        OnPropertyChanged(nameof(IsAllSelected));
        SelectionChanged?.Invoke();
    }

    private void PullDependencies(int startNodeId)
    {
        var toSelect = DependencyClosure.Expand(startNodeId, _outgoing, _addedIds);
        if (toSelect.Count == 0) return;

        _suppressRollDown = true;
        foreach (var n in Nodes)
            if (toSelect.Contains(n.NodeId))
                n.IsSelected = true;
        _suppressRollDown = false;
    }
}
