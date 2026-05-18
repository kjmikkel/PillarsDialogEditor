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

/// Edits the script lists on a node (Enter / Exit / Update).
public partial class ScriptEditorViewModel : ObservableObject
{
    private readonly Action<IReadOnlyList<ScriptCall>> _commit;

    public string NodeTitle { get; }

    // Catalogue entries available for the add picker (all entries for now; can filter by gameId later)
    public IReadOnlyList<ScriptCatalogueEntry> AvailableScripts { get; }

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
        Action<IReadOnlyList<ScriptCall>> commit,
        string gameId = "")
    {
        NodeTitle = title;
        _commit   = commit;

        AvailableScripts = string.IsNullOrEmpty(gameId)
            ? ScriptCatalogue.Instance.All
            : ScriptCatalogue.Instance.ForGame(gameId);

        foreach (var s in initial)
        {
            // FindByFullName disambiguates same-named scripts with different signatures
            // (e.g. PoE1 StartQuest(String) vs PoE2 StartQuest(Guid))
            var entry = ScriptCatalogue.Instance.FindByFullName(s.FullName);
            RowsFor(s.Category).Add(new ScriptRowViewModel(s, entry));
        }
    }

    // ── Convenience wrapper for nodes ─────────────────────────────────────
    public ScriptEditorViewModel(NodeViewModel node, string gameId = "")
        : this($"Node {node.NodeId}", node.Scripts,
               scripts => node.Scripts = scripts, gameId) { }

    private ObservableCollection<ScriptRowViewModel> RowsFor(ScriptCategory cat) => cat switch
    {
        ScriptCategory.Enter  => EnterRows,
        ScriptCategory.Exit   => ExitRows,
        ScriptCategory.Update => UpdateRows,
        _                     => EnterRows,
    };

    // ── Picker state: one selected entry + text per section ───────────────

    [ObservableProperty] private ScriptCatalogueEntry? _selectedEnterEntry;
    [ObservableProperty] private ScriptCatalogueEntry? _selectedExitEntry;
    [ObservableProperty] private ScriptCatalogueEntry? _selectedUpdateEntry;

    // Text is also bound so manual FullName entry works when no catalogue entry is selected
    [ObservableProperty] private string _newEnterText  = string.Empty;
    [ObservableProperty] private string _newExitText   = string.Empty;
    [ObservableProperty] private string _newUpdateText = string.Empty;

    // ── Add commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private void AddEnterScript()
    {
        AddScript(SelectedEnterEntry, NewEnterText, ScriptCategory.Enter);
        SelectedEnterEntry = null;
        NewEnterText = string.Empty;
    }

    [RelayCommand]
    private void AddExitScript()
    {
        AddScript(SelectedExitEntry, NewExitText, ScriptCategory.Exit);
        SelectedExitEntry = null;
        NewExitText = string.Empty;
    }

    [RelayCommand]
    private void AddUpdateScript()
    {
        AddScript(SelectedUpdateEntry, NewUpdateText, ScriptCategory.Update);
        SelectedUpdateEntry = null;
        NewUpdateText = string.Empty;
    }

    private void AddScript(ScriptCatalogueEntry? entry, string rawText, ScriptCategory category)
    {
        if (entry is not null)
        {
            // Catalogue entry: pre-populate parameters with defaults
            var defaults = entry.Parameters.Select(p => p.Default).ToList();
            var call     = new ScriptCall(entry.ReflectionFullName, defaults, category);
            RowsFor(category).Add(new ScriptRowViewModel(call, entry));
        }
        else
        {
            var name = rawText.Trim();
            if (string.IsNullOrEmpty(name)) return;
            RowsFor(category).Add(new ScriptRowViewModel(new ScriptCall(name, [], category), null));
        }
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
