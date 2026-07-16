using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One row in the Duplicate-lines pane: an exact group or a near pair.
public sealed partial class DuplicateRowViewModel : ObservableObject
{
    private readonly Action _navigate;
    private readonly Action _ignore;

    public string TierLabel { get; }
    public string Text      { get; }
    public string Locations { get; }

    public DuplicateRowViewModel(string tierLabel, string text, string locations,
        Action navigate, Action ignore)
    {
        TierLabel = tierLabel;
        Text      = text;
        Locations = locations;
        _navigate = navigate;
        _ignore   = ignore;
    }

    [RelayCommand] private void Navigate() => _navigate();
    [RelayCommand] private void Ignore()   => _ignore();
}

/// One row in the Ignored-duplicates pane.
public sealed partial class IgnoredDuplicateRowViewModel : ObservableObject
{
    private readonly Action _restore;

    public string TierLabel   { get; }
    public string DisplayText { get; }

    public IgnoredDuplicateRowViewModel(string tierLabel, string displayText, Action restore)
    {
        TierLabel   = tierLabel;
        DisplayText = displayText;
        _restore    = restore;
    }

    [RelayCommand] private void Restore() => _restore();
}

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

    private readonly Func<DuplicateLineReport>? _dupScan;
    private readonly Func<IReadOnlyList<IgnoredDuplicate>>? _ignoredList;
    private readonly Action<IgnoredDuplicate>? _ignore;
    private readonly Action<IgnoredDuplicate>? _unignore;
    private readonly Action<string, int>? _navigate;

    public ObservableCollection<TextTagRowViewModel> Rows { get; } = [];
    public ObservableCollection<StaleDataRowViewModel> StaleRows { get; } = [];
    public ObservableCollection<DuplicateRowViewModel> DuplicateRows { get; } = [];
    public ObservableCollection<IgnoredDuplicateRowViewModel> IgnoredDuplicateRows { get; } = [];

    [ObservableProperty] private bool   _hasDuplicates;
    [ObservableProperty] private string _duplicateSummaryText = string.Empty;
    [ObservableProperty] private bool   _hasIgnoredDuplicates;
    [ObservableProperty] private string _ignoredSummaryText = string.Empty;

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
        string primaryLanguage = "",
        Func<DuplicateLineReport>? dupScan = null,
        Func<IReadOnlyList<IgnoredDuplicate>>? ignoredList = null,
        Action<IgnoredDuplicate>? ignore = null,
        Action<IgnoredDuplicate>? unignore = null,
        Action<string, int>? navigate = null)
    {
        _scan             = scan;
        _addWord          = addWord;
        _staleScan        = staleScan;
        _prune            = prune;
        CanCheckGameFiles = canCheckGameFiles;
        _primaryLanguage  = primaryLanguage;
        _dupScan          = dupScan;
        _ignoredList      = ignoredList;
        _ignore           = ignore;
        _unignore         = unignore;
        _navigate         = navigate;

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
        RefreshDuplicates();
    }

    private void RefreshDuplicates()
    {
        DuplicateRows.Clear();
        if (_dupScan is not null)
        {
            var report = _dupScan();

            foreach (var g in report.Exact)
            {
                var entry     = new IgnoredDuplicate(DuplicateKind.Exact, [g.Key], g.SampleText);
                var primary   = g.Members[0];
                var locations = string.Join(", ", g.Members.Select(
                    m => Loc.Format("Duplicate_Location", m.ConversationName, m.NodeId)));
                DuplicateRows.Add(new DuplicateRowViewModel(
                    Loc.Get("Duplicate_Tier_Exact"), g.SampleText, locations,
                    () => _navigate?.Invoke(primary.ConversationName, primary.NodeId),
                    () => { _ignore?.Invoke(entry); Refresh(); }));
            }

            foreach (var p in report.Near)
            {
                var display   = Loc.Format("Duplicate_NearDisplay", p.A.Text, p.B.Text);
                var entry     = new IgnoredDuplicate(DuplicateKind.Near, p.Key, display);
                var locations = Loc.Format("Duplicate_Location", p.A.ConversationName, p.A.NodeId)
                              + ", " + Loc.Format("Duplicate_Location", p.B.ConversationName, p.B.NodeId);
                DuplicateRows.Add(new DuplicateRowViewModel(
                    Loc.Format("Duplicate_Tier_Near", p.SimilarityPercent), display, locations,
                    () => _navigate?.Invoke(p.A.ConversationName, p.A.NodeId),
                    () => { _ignore?.Invoke(entry); Refresh(); }));
            }
        }
        HasDuplicates        = DuplicateRows.Count > 0;
        DuplicateSummaryText = DuplicateRows.Count == 0
            ? Loc.Get("Duplicate_NoIssues")
            : Loc.FormatCount("Duplicate_Summary", DuplicateRows.Count);

        IgnoredDuplicateRows.Clear();
        if (_ignoredList is not null)
        {
            foreach (var e in _ignoredList())
            {
                var tier = e.Kind == DuplicateKind.Exact
                    ? Loc.Get("Duplicate_Tier_Exact")
                    : Loc.Get("Duplicate_Tier_NearShort");
                IgnoredDuplicateRows.Add(new IgnoredDuplicateRowViewModel(
                    tier, e.DisplayText, () => { _unignore?.Invoke(e); Refresh(); }));
            }
        }
        HasIgnoredDuplicates = IgnoredDuplicateRows.Count > 0;
        IgnoredSummaryText   = IgnoredDuplicateRows.Count == 0
            ? Loc.Get("Duplicate_Ignored_NoIssues")
            : Loc.FormatCount("Duplicate_Ignored_Summary", IgnoredDuplicateRows.Count);
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
