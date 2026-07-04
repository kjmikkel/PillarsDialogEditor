using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

/// One match found by the search — identifies which node and which field.
public record FindResult(NodeViewModel Node, string FieldName, string DisplayText);

public partial class FindReplaceViewModel : ObservableObject
{
    private readonly ConversationViewModel _canvas;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceAllCommand))]
    private string _searchText = string.Empty;

    [ObservableProperty] private string _replaceText   = string.Empty;
    [ObservableProperty] private bool   _caseSensitive;
    [ObservableProperty] private string _statusText    = string.Empty;

    public IReadOnlyList<FindResult> Results { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentResult))]
    private int _currentIndex = -1;

    public FindResult? CurrentResult
        => CurrentIndex >= 0 && CurrentIndex < Results.Count
            ? Results[CurrentIndex]
            : null;

    public FindReplaceViewModel(ConversationViewModel canvas)
        => _canvas = canvas;

    // ── Find ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanFind))]
    private void Find()
    {
        var comparison = CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var results = new List<FindResult>();
        foreach (var node in _canvas.Nodes)
        {
            if (node.DefaultText.Contains(SearchText, comparison))
                results.Add(new FindResult(node, "DefaultText", node.DefaultText));
            if (!string.IsNullOrEmpty(node.FemaleText) &&
                node.FemaleText.Contains(SearchText, comparison))
                results.Add(new FindResult(node, "FemaleText", node.FemaleText));
        }

        Results       = results;
        CurrentIndex  = results.Count > 0 ? 0 : -1;
        StatusText = results.Count > 0
            ? Loc.FormatCount("FindReplace_Matches", results.Count)
            : Loc.Get("FindReplace_NoMatches");

        OnPropertyChanged(nameof(Results));
        SelectCurrent();
        ReplaceAllCommand.NotifyCanExecuteChanged();
        FindNextCommand.NotifyCanExecuteChanged();
        FindPrevCommand.NotifyCanExecuteChanged();
        ReplaceCommand.NotifyCanExecuteChanged();
    }

    private bool CanFind() => !string.IsNullOrEmpty(SearchText);

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void FindNext()
    {
        if (Results.Count == 0) return;
        CurrentIndex = (CurrentIndex + 1) % Results.Count;
        StatusText   = Loc.Format("FindReplace_Navigation", CurrentIndex + 1, Results.Count);
        SelectCurrent();
    }

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void FindPrev()
    {
        if (Results.Count == 0) return;
        CurrentIndex = (CurrentIndex - 1 + Results.Count) % Results.Count;
        StatusText   = Loc.Format("FindReplace_Navigation", CurrentIndex + 1, Results.Count);
        SelectCurrent();
    }

    private bool HasResults() => Results.Count > 0;

    private void SelectCurrent()
    {
        if (CurrentResult?.Node is { } node)
            _canvas.SelectedNode = node;
    }

    // ── Replace ───────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasCurrentResult))]
    private void Replace()
    {
        if (CurrentResult is not { } result) return;
        ApplyReplace(result);
        Find(); // re-scan after replacement
    }

    private bool HasCurrentResult() => CurrentResult is not null;

    [RelayCommand(CanExecute = nameof(CanFind))]
    private void ReplaceAll()
    {
        // Run search first so Results is populated
        Find();
        foreach (var result in Results.ToList())
            ApplyReplace(result);

        var count = Results.Count;
        Results      = [];
        CurrentIndex = -1;
        StatusText = count > 0
            ? Loc.FormatCount("FindReplace_Replaced", count)
            : Loc.Get("FindReplace_NothingReplaced");

        OnPropertyChanged(nameof(Results));
        ReplaceAllCommand.NotifyCanExecuteChanged();
        FindNextCommand.NotifyCanExecuteChanged();
        FindPrevCommand.NotifyCanExecuteChanged();
        ReplaceCommand.NotifyCanExecuteChanged();
    }

    private void ApplyReplace(FindResult result)
    {
        var comparison = CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (result.FieldName == "DefaultText")
            result.Node.DefaultText = ReplaceAll(result.Node.DefaultText, SearchText, ReplaceText, comparison);
        else if (result.FieldName == "FemaleText")
            result.Node.FemaleText  = ReplaceAll(result.Node.FemaleText,  SearchText, ReplaceText, comparison);
    }

    private static string ReplaceAll(string source, string search, string replacement,
        StringComparison comparison)
        => StringReplace.ReplaceAll(source, search, replacement, comparison);
}
