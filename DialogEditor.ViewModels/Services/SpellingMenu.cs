namespace DialogEditor.ViewModels.Services;

/// What the right-click menu on a misspelt word offers (issue 313): the word and its exact
/// span in the text, up to <see cref="MaxSuggestions"/> suggestions, and the add-to-dictionary
/// actions (null when that dictionary is not available, e.g. no project open).
public sealed record SpellingMenu(
    string Word,
    int Start,
    int Length,
    IReadOnlyList<string> Suggestions,
    Action? AddToDictionary,
    Action? AddToProjectDictionary)
{
    public const int MaxSuggestions = 5;
}
