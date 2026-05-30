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
        node.SelectionChanged += OnNodeSelectionChanged;
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

    private void OnNodeSelectionChanged()
    {
        if (_suppressRollDown) return;
        OnPropertyChanged(nameof(IsAllSelected));
        SelectionChanged?.Invoke();
    }
}
