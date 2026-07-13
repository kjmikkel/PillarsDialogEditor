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

    private readonly Func<bool, IReadOnlyList<StaleDataRow>>? _staleScan;
    private readonly Action<IReadOnlyList<StaleDataRow>>? _prune;
    private readonly string _primaryLanguage;

    public ObservableCollection<TextTagRowViewModel> Rows { get; } = [];
    public ObservableCollection<StaleDataRowViewModel> StaleRows { get; } = [];

    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool   _hasIssues;

    [ObservableProperty] private string _staleSummaryText = string.Empty;
    [ObservableProperty] private bool   _hasStaleData;
    [ObservableProperty] private bool   _isStaleCleanUpArmed;

    public bool CanCheckGameFiles { get; }
    [ObservableProperty] private bool _checkGameFiles;

    public RelayCommand CleanUpStaleCommand        { get; }
    public RelayCommand ConfirmCleanUpStaleCommand { get; }
    public RelayCommand CancelCleanUpStaleCommand  { get; }

    public string StaleCleanUpConfirmText =>
        Loc.FormatCount("StaleData_CleanUpConfirm", ConfirmedCount);

    private int ConfirmedCount =>
        StaleRows.Count(r => !r.IsLikely);

    public TextTagValidationViewModel(
        Func<IReadOnlyList<TextTagIssueRow>> scan,
        Action<string>? addWord = null,
        Func<bool, IReadOnlyList<StaleDataRow>>? staleScan = null,
        Action<IReadOnlyList<StaleDataRow>>? prune = null,
        bool canCheckGameFiles = false,
        string primaryLanguage = "")
    {
        _scan             = scan;
        _addWord          = addWord;
        _staleScan        = staleScan;
        _prune            = prune;
        CanCheckGameFiles = canCheckGameFiles;
        _primaryLanguage  = primaryLanguage;

        CleanUpStaleCommand        = new RelayCommand(() => IsStaleCleanUpArmed = true,
                                                      () => HasStaleData && ConfirmedCount > 0 && !IsStaleCleanUpArmed);
        ConfirmCleanUpStaleCommand = new RelayCommand(ExecuteStaleCleanUp, () => IsStaleCleanUpArmed);
        CancelCleanUpStaleCommand  = new RelayCommand(() => IsStaleCleanUpArmed = false, () => IsStaleCleanUpArmed);

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

        RefreshStale();
    }

    private void RefreshStale()
    {
        IsStaleCleanUpArmed = false;
        StaleRows.Clear();
        if (_staleScan is not null)
        {
            foreach (var r in _staleScan(CheckGameFiles && CanCheckGameFiles))
                StaleRows.Add(new StaleDataRowViewModel(r, _primaryLanguage, RemoveOne));
        }
        HasStaleData = StaleRows.Count > 0;
        StaleSummaryText = StaleRows.Count == 0
            ? Loc.Get("StaleData_NoIssues")
            : Loc.FormatCount("StaleData_Summary", StaleRows.Count);
        OnPropertyChanged(nameof(StaleCleanUpConfirmText));
        RaiseStaleCommandStates();
    }

    partial void OnCheckGameFilesChanged(bool value) => RefreshStale();
    partial void OnHasStaleDataChanged(bool value) => RaiseStaleCommandStates();
    partial void OnIsStaleCleanUpArmedChanged(bool value) => RaiseStaleCommandStates();

    private void RaiseStaleCommandStates()
    {
        CleanUpStaleCommand.NotifyCanExecuteChanged();
        ConfirmCleanUpStaleCommand.NotifyCanExecuteChanged();
        CancelCleanUpStaleCommand.NotifyCanExecuteChanged();
    }

    private void ExecuteStaleCleanUp()
    {
        var confirmed = StaleRows.Where(r => !r.IsLikely).Select(r => r.Row).ToList();
        IsStaleCleanUpArmed = false;
        if (confirmed.Count == 0) return;
        _prune?.Invoke(confirmed);
        Refresh();
    }

    private void RemoveOne(StaleDataRow row)
    {
        _prune?.Invoke([row]);
        Refresh();
    }
}
