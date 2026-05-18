using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// A single editable script call row (flat — no NOT/And-Or).
public partial class ScriptRowViewModel : ObservableObject
{
    public string FullName     { get; }
    public string DisplayName  { get; }
    public ScriptCategory Category { get; }

    public ObservableCollection<ParameterValueViewModel> Parameters { get; }

    public bool IsLeaf => true;   // all script rows are leaves (no tree structure)

    public ScriptRowViewModel(ScriptCall call, ScriptCatalogueEntry? entry)
    {
        FullName    = call.FullName;
        DisplayName = entry?.DisplayName ?? call.DisplayName;
        Category    = call.Category;

        if (entry is not null)
        {
            Parameters = new(entry.Parameters
                .Select((p, i) => new ParameterValueViewModel
                {
                    Name        = p.Name,
                    Description = p.Description,
                    Type        = p.Type,
                    Options     = p.Options,
                    Value       = i < call.Parameters.Count ? call.Parameters[i] : p.Default,
                }));
        }
        else
        {
            Parameters = new(call.Parameters
                .Select(v => new ParameterValueViewModel { Name = "Parameter", Value = v }));
        }
    }

    public ScriptCall ToCall() =>
        new(FullName, Parameters.Select(p => p.Value).ToList(), Category);
}

/// Simple entry in the script catalogue.
public record ScriptCatalogueEntry(
    string MethodName,
    string DisplayName,
    string Category,
    string FullName,
    IReadOnlyList<ConditionParameter> Parameters);

/// Edits the script lists on a node (Enter / Exit / Update).
public partial class ScriptEditorViewModel : ObservableObject
{
    private readonly Action<IReadOnlyList<ScriptCall>> _commit;

    public string NodeTitle { get; }

    // Rows per category
    public ObservableCollection<ScriptRowViewModel> EnterRows  { get; } = [];
    public ObservableCollection<ScriptRowViewModel> ExitRows   { get; } = [];
    public ObservableCollection<ScriptRowViewModel> UpdateRows { get; } = [];

    public event Action? Confirmed;
    public event Action? Cancelled;

    // ── General constructor ───────────────────────────────────────────────
    public ScriptEditorViewModel(
        string title,
        IReadOnlyList<ScriptCall> initial,
        Action<IReadOnlyList<ScriptCall>> commit)
    {
        NodeTitle = title;
        _commit   = commit;

        foreach (var s in initial)
        {
            var row = new ScriptRowViewModel(s, null);
            RowsFor(s.Category).Add(row);
        }
    }

    // ── Convenience wrapper for nodes ─────────────────────────────────────
    public ScriptEditorViewModel(NodeViewModel node)
        : this($"Node {node.NodeId}", node.Scripts,
               scripts => node.Scripts = scripts) { }

    private ObservableCollection<ScriptRowViewModel> RowsFor(ScriptCategory cat) => cat switch
    {
        ScriptCategory.Enter  => EnterRows,
        ScriptCategory.Exit   => ExitRows,
        ScriptCategory.Update => UpdateRows,
        _                     => EnterRows,
    };

    // ── Add script by FullName ────────────────────────────────────────────

    [ObservableProperty] private string _newEnterFullName  = string.Empty;
    [ObservableProperty] private string _newExitFullName   = string.Empty;
    [ObservableProperty] private string _newUpdateFullName = string.Empty;

    [RelayCommand]
    private void AddEnterScript()  => AddByName(NewEnterFullName,  ScriptCategory.Enter,
        n => NewEnterFullName  = n);
    [RelayCommand]
    private void AddExitScript()   => AddByName(NewExitFullName,   ScriptCategory.Exit,
        n => NewExitFullName   = n);
    [RelayCommand]
    private void AddUpdateScript() => AddByName(NewUpdateFullName, ScriptCategory.Update,
        n => NewUpdateFullName = n);

    private void AddByName(string raw, ScriptCategory category, Action<string> clearField)
    {
        var name = raw.Trim();
        if (string.IsNullOrEmpty(name)) return;
        RowsFor(category).Add(new ScriptRowViewModel(new ScriptCall(name, [], category), null));
        clearField(string.Empty);
    }

    [RelayCommand]
    private void DeleteRow(ScriptRowViewModel? row)
    {
        if (row is null) return;
        RowsFor(row.Category).Remove(row);
    }

    [RelayCommand]
    private void MoveUp(ScriptRowViewModel? row)
    {
        if (row is null) return;
        var coll = RowsFor(row.Category);
        var i    = coll.IndexOf(row);
        if (i > 0) coll.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveDown(ScriptRowViewModel? row)
    {
        if (row is null) return;
        var coll = RowsFor(row.Category);
        var i    = coll.IndexOf(row);
        if (i >= 0 && i < coll.Count - 1) coll.Move(i, i + 1);
    }

    [RelayCommand]
    private void Confirm()
    {
        var all = EnterRows.Concat(ExitRows).Concat(UpdateRows)
                           .Select(r => r.ToCall())
                           .ToList();
        _commit(all);
        Confirmed?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
