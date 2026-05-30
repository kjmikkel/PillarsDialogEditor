using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.ViewModels;

/// One changed dialogue line in the selection tree. Kind drives the +/~/- glyph
/// and colour; IsSelected drives whether it is brought in.
public partial class NodeChangeViewModel : ObservableObject
{
    public int        NodeId { get; }
    public DiffStatus Kind   { get; }

    [ObservableProperty] private bool _isSelected;

    public event Action? SelectionChanged;

    public NodeChangeViewModel(int nodeId, DiffStatus kind)
    {
        NodeId = nodeId;
        Kind   = kind;
    }

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();
}
