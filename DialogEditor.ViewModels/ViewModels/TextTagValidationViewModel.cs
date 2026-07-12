using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One display row of the Validate Text window (tag or spelling issue).
public sealed partial class TextTagRowViewModel
{
    private readonly Action<string>? _addWord;
    private readonly Action _refresh;
    private readonly string? _word;

    public string ConversationName { get; }
    public string NodeLabel        { get; }
    public string LanguageLabel    { get; }
    public string TypeLabel        { get; }
    public string Message          { get; }

    /// Spelling rows with a wired add-word callback offer "Add to dictionary".
    public bool CanAddToDictionary => _word is not null && _addWord is not null;

    public TextTagRowViewModel(TextTagIssueRow row, Action<string>? addWord, Action refresh)
    {
        ConversationName = row.ConversationName;
        NodeLabel        = Loc.Format("VoValidation_NodeRow", row.NodeId);
        LanguageLabel    = row.Language.Length == 0
            ? Loc.Get("TextTagValidation_Default") : row.Language;
        TypeLabel        = row.Type == TextIssueType.Spelling
            ? Loc.Get("TextIssueType_Spelling") : Loc.Get("TextIssueType_Tag");
        Message          = row.Message;
        _word            = row.Word;
        _addWord         = addWord;
        _refresh         = refresh;
    }

    [RelayCommand]
    private void AddToDictionary()
    {
        if (_word is null || _addWord is null) return;
        _addWord(_word);
        _refresh();
    }
}

/// Project-wide text validation results (Test ▸ Validate Text…). The scan
/// delegate reads the CURRENT saved project on each invocation, so Refresh picks up
/// saves made while the window is open. The scan is pure in-memory string work —
/// no IO — hence synchronous.
public partial class TextTagValidationViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TextTagIssueRow>> _scan;
    private readonly Action<string>? _addWord;

    public ObservableCollection<TextTagRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool   _hasIssues;

    public TextTagValidationViewModel(
        Func<IReadOnlyList<TextTagIssueRow>> scan,
        Action<string>? addWord = null)
    {
        _scan    = scan;
        _addWord = addWord;
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var rows = _scan();
        Rows.Clear();
        foreach (var r in rows) Rows.Add(new TextTagRowViewModel(r, _addWord, Refresh));
        HasIssues = rows.Count > 0;
        var convCount = rows.Select(r => r.ConversationName).Distinct().Count();
        SummaryText = rows.Count == 0
            ? Loc.Get("TextTagValidation_NoIssues")
            : Loc.Format("TextTagValidation_Summary",
                Loc.FormatCount("TextTagValidation_Issues", rows.Count),
                Loc.FormatCount("TextTagValidation_Convs", convCount));
    }
}
