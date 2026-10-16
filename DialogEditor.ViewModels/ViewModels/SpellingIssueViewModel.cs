using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One possible misspelling in the Node Details panel, with its quick fixes (issue 9):
/// use the suggested word, or add the word to the user dictionary.
public sealed partial class SpellingIssueViewModel
{
    private readonly Action _apply;
    private readonly Action? _addToDictionary;

    public string  Word       { get; }
    public string? Suggestion { get; }
    public string  Message    { get; }

    public bool CanApplySuggestion => Suggestion is not null;

    /// The quick-fix button's label, e.g. Use "captain".
    public string ApplyLabel => Suggestion is null ? string.Empty : Loc.Format("Spelling_UseSuggestion", Suggestion);
    public bool CanAddToDictionary => _addToDictionary is not null;

    public SpellingIssueViewModel(SpellIssue issue, string message, Action apply, Action? addToDictionary)
    {
        Word       = issue.Word;
        Suggestion = issue.Suggestion;
        Message    = message;
        _apply           = apply;
        _addToDictionary = addToDictionary;
    }

    [RelayCommand(CanExecute = nameof(CanApplySuggestion))]
    private void ApplySuggestion() => _apply();

    [RelayCommand(CanExecute = nameof(CanAddToDictionary))]
    private void AddToDictionary() => _addToDictionary?.Invoke();
}
