using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class ConditionEditorViewModel : ObservableObject
{
    private readonly Action<IReadOnlyList<ConditionNode>> _commit;
    private readonly string _gameId;

    public string NodeTitle { get; }

    public ObservableCollection<ConditionRowViewModel> Rows { get; } = [];

    public IReadOnlyList<ConditionEntry> AvailableConditions
        => string.IsNullOrEmpty(_gameId)
            ? ConditionCatalogue.Instance.All
            : ConditionCatalogue.Instance.ForGame(_gameId);

    [ObservableProperty] private ConditionEntry? _selectedNewCondition;

    public event Action? Confirmed;
    public event Action? Cancelled;

    // ── General constructor — works for nodes, links, or anything that
    //   owns a list of conditions ───────────────────────────────────────
    public ConditionEditorViewModel(
        string title,
        IReadOnlyList<ConditionNode> initial,
        Action<IReadOnlyList<ConditionNode>> commit,
        string gameId = "")
    {
        NodeTitle = title;
        _commit   = commit;
        _gameId   = gameId;

        foreach (var c in initial)
        {
            if (c is ConditionLeaf leaf)
                Rows.Add(BuildRow(leaf));
            else if (c is ConditionBranch branch)
                Rows.Add(new ConditionRowViewModel(branch));
        }
    }

    // ── Convenience wrapper for node conditions (existing callers unchanged) ─
    public ConditionEditorViewModel(NodeViewModel node, string gameId = "")
        : this($"Node {node.NodeId}",
               node.Conditions,
               conditions => node.Conditions = conditions,
               gameId) { }

    private ConditionRowViewModel BuildRow(ConditionLeaf leaf)
    {
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
        var leaf     = new ConditionLeaf(entry.ReflectionFullName, defaults, false, "And");
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
        _commit(Rows.Select(r => r.ToNode()).ToList());
        Confirmed?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
