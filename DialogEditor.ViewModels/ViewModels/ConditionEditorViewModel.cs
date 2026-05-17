using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class ConditionEditorViewModel : ObservableObject
{
    private readonly NodeViewModel                    _node;
    private readonly IReadOnlyList<ConditionNode>     _original;

    public string NodeTitle { get; }

    public ObservableCollection<ConditionRowViewModel> Rows { get; } = [];

    public IReadOnlyList<ConditionEntry> AvailableConditions
        => ConditionCatalogue.Instance.All;

    [ObservableProperty] private ConditionEntry? _selectedNewCondition;

    public event Action? Confirmed;
    public event Action? Cancelled;

    public ConditionEditorViewModel(NodeViewModel node)
    {
        _node    = node;
        _original = node.Conditions.ToList();
        NodeTitle = $"Node {node.NodeId}";

        foreach (var c in node.Conditions)
        {
            if (c is ConditionLeaf leaf)
                Rows.Add(BuildRow(leaf));
        }
    }

    private ConditionRowViewModel BuildRow(ConditionLeaf leaf)
    {
        // Try to match by method name (strip return type and params from FullName)
        var methodName = leaf.FullName.Contains(' ')
            ? leaf.FullName[(leaf.FullName.IndexOf(' ') + 1)..].Split('(')[0]
            : leaf.FullName;
        var entry = ConditionCatalogue.Instance.Find(methodName);
        return new ConditionRowViewModel(leaf, entry);
    }

    [RelayCommand]
    private void AddSelectedCondition()
    {
        if (SelectedNewCondition is not { } entry) return;
        var defaults = entry.Parameters.Select(p => p.Default).ToList();
        var leaf     = new ConditionLeaf(entry.MethodName, defaults, false, "And");
        Rows.Add(BuildRow(leaf));
        SelectedNewCondition = null;
    }

    [RelayCommand]
    private void DeleteRow(ConditionRowViewModel? row)
    {
        if (row is not null) Rows.Remove(row);
    }

    [RelayCommand]
    private void MoveUp(ConditionRowViewModel? row)
    {
        var i = row is null ? -1 : Rows.IndexOf(row);
        if (i > 0) Rows.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveDown(ConditionRowViewModel? row)
    {
        var i = row is null ? -1 : Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1) Rows.Move(i, i + 1);
    }

    [RelayCommand]
    private void Confirm()
    {
        _node.Conditions = Rows.Select(r => (ConditionNode)r.ToLeaf()).ToList();
        Confirmed?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        // Discard — leave _node.Conditions unchanged (still has _original values)
        Cancelled?.Invoke();
    }
}
