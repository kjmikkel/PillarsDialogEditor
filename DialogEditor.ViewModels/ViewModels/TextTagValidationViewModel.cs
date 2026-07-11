using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One display row of the Validate Text Tags window.
public sealed class TextTagRowViewModel
{
    public string ConversationName { get; }
    public string NodeLabel        { get; }
    public string LanguageLabel    { get; }
    public string Message          { get; }

    public TextTagRowViewModel(TextTagIssueRow row)
    {
        ConversationName = row.ConversationName;
        NodeLabel        = Loc.Format("VoValidation_NodeRow", row.NodeId);
        LanguageLabel    = row.Language.Length == 0
            ? Loc.Get("TextTagValidation_Default") : row.Language;
        Message          = row.Message;
    }
}

/// Project-wide text-tag validation results (Test ▸ Validate Text Tags…). The scan
/// delegate reads the CURRENT saved project on each invocation, so Refresh picks up
/// saves made while the window is open. The scan is pure in-memory string work —
/// no IO — hence synchronous.
public partial class TextTagValidationViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TextTagIssueRow>> _scan;

    public ObservableCollection<TextTagRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool   _hasIssues;

    public TextTagValidationViewModel(Func<IReadOnlyList<TextTagIssueRow>> scan)
    {
        _scan = scan;
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var rows = _scan();
        Rows.Clear();
        foreach (var r in rows) Rows.Add(new TextTagRowViewModel(r));
        HasIssues = rows.Count > 0;
        var convCount = rows.Select(r => r.ConversationName).Distinct().Count();
        SummaryText = rows.Count == 0
            ? Loc.Get("TextTagValidation_NoIssues")
            : Loc.Format("TextTagValidation_Summary",
                Loc.FormatCount("TextTagValidation_Issues", rows.Count),
                Loc.FormatCount("TextTagValidation_Convs", convCount));
    }
}
