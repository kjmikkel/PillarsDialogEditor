using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Search;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One selectable catalogue entry (a condition or a script) in the search picker.
public record SearchEntryItem(
    string DisplayLabel, string ReflectionFullName, IReadOnlyList<ConditionParameter> Parameters)
{
    public override string ToString() => DisplayLabel;
}

/// One parameter of the selected entry, optionally pinned. Empty Value = wildcard.
public partial class PinRowViewModel : ObservableObject
{
    public string Name { get; }
    public IReadOnlyList<string> Options { get; }   // enum/lookup options for the view (may be empty)
    public string? LookupKind { get; }
    [ObservableProperty] private string _value = string.Empty;

    public PinRowViewModel(ConditionParameter p)
    {
        Name       = p.Name;
        Options    = p.Options ?? [];
        LookupKind = p.LookupKind;
    }
}

/// Per-conversation search over conditions AND scripts. Builds a CatalogueMatch from a chosen
/// entry + pinned parameters, finds the matching nodes, and applies Match/Dimmed via callbacks.
public partial class ConditionSearchViewModel : ObservableObject
{
    private readonly Func<ConversationEditSnapshot?> _getSnapshot;
    private readonly Action<IReadOnlySet<int>>       _applyHighlight;
    private readonly Action                          _clearHighlight;

    public ObservableCollection<SearchEntryItem> Entries { get; } = [];
    public ObservableCollection<PinRowViewModel> PinRows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private SearchEntryItem? _selectedEntry;

    [ObservableProperty] private string _matchCountText = string.Empty;

    public ConditionSearchViewModel(
        string gameId,
        Func<ConversationEditSnapshot?> getSnapshot,
        Action<IReadOnlySet<int>> applyHighlight,
        Action clearHighlight)
    {
        _getSnapshot    = getSnapshot;
        _applyHighlight = applyHighlight;
        _clearHighlight = clearHighlight;

        var items = new List<SearchEntryItem>();
        foreach (var c in ConditionCatalogue.Instance.ForGame(gameId))
            items.Add(new SearchEntryItem(Loc.Format("CondSearch_EntryCondition", c.DisplayName),
                c.ReflectionFullName, c.Parameters));
        foreach (var s in ScriptCatalogue.Instance.ForGame(gameId))
            items.Add(new SearchEntryItem(Loc.Format("CondSearch_EntryScript", s.DisplayName),
                s.ReflectionFullName, s.Parameters));

        foreach (var e in items.OrderBy(e => e.DisplayLabel, StringComparer.CurrentCultureIgnoreCase))
            Entries.Add(e);
    }

    partial void OnSelectedEntryChanged(SearchEntryItem? value)
    {
        PinRows.Clear();
        if (value is null) return;
        foreach (var p in value.Parameters) PinRows.Add(new PinRowViewModel(p));
    }

    private bool CanSearch() => SelectedEntry is not null;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private void Search()
    {
        var snap = _getSnapshot();
        if (snap is null || SelectedEntry is null) return;

        var pins = PinRows
            .Select(r => string.IsNullOrEmpty(r.Value) ? ParameterPin.Wildcard : ParameterPin.Pin(r.Value))
            .ToList();
        var query   = new CatalogueMatch(SelectedEntry.ReflectionFullName, pins);
        var matches = NodeConditionSearchService.FindMatches(snap, query);

        _applyHighlight(matches);
        MatchCountText = Loc.Format("CondSearch_MatchCount", matches.Count);
    }

    [RelayCommand]
    private void Clear()
    {
        _clearHighlight();
        MatchCountText = string.Empty;
    }
}
