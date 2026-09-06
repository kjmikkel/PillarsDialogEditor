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

    private readonly Func<DuplicateScanOptions, DuplicateLineReport>? _dupScan;
    private readonly Action<DuplicateScanOptions>? _persistDuplicateOptions;
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

    // ── Near-duplicate threshold (issue #14) ─────────────────────────────────
    // The bar is a dial the writer discovers the right value for by watching the
    // report change, so it lives inline beside the Duplicate-lines header rather
    // than in Settings. The initial value and persistence come from the caller, so
    // this VM stays free of settings-file state.

    /// The presets offered inline. A fixed list rather than free entry: the scanner's
    /// length-blocking optimisation divides by this value, so a near-zero setting would
    /// degrade the scan to a full O(n^2) sweep.
    public IReadOnlyList<double> NearThresholdOptions { get; } = [0.75, 0.80, 0.85, 0.90, 0.95];

    [ObservableProperty] private double _nearThreshold = DuplicateLineScanner.DefaultNearThreshold;

    // Scope toggles (issue #14). Both default OFF so the historical report — primary
    // language, Default text only — is what the writer gets unasked. Female text and
    // other languages were always present in patch.Translations; the scan simply
    // discarded them.
    [ObservableProperty] private bool _includeFemaleText;
    [ObservableProperty] private bool _includeOtherLanguages;

    /// The three scan options as one value. They travel together into the scan delegate
    /// and back out through the persist callback, so the constructor takes one record
    /// rather than three initial values and three callbacks.
    public DuplicateScanOptions DuplicateOptions =>
        new(NearThreshold, IncludeFemaleText, IncludeOtherLanguages);

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
        Func<DuplicateScanOptions, DuplicateLineReport>? dupScan = null,
        Func<IReadOnlyList<IgnoredDuplicate>>? ignoredList = null,
        Action<IgnoredDuplicate>? ignore = null,
        Action<IgnoredDuplicate>? unignore = null,
        Action<string, int>? navigate = null,
        DuplicateScanOptions? duplicateOptions = null,
        Action<DuplicateScanOptions>? persistDuplicateOptions = null)
    {
        _scan             = scan;
        _addWord          = addWord;
        _staleScan        = staleScan;
        _prune            = prune;
        CanCheckGameFiles = canCheckGameFiles;
        _primaryLanguage  = primaryLanguage;
        _dupScan          = dupScan;
        // Assign the backing fields directly, and BEFORE the Refresh() below: the
        // property setters would persist values we were just handed, and the
        // constructor's scan reads all three.
        var initial              = duplicateOptions ?? new DuplicateScanOptions();
        _nearThreshold           = initial.NearThreshold;
        _includeFemaleText       = initial.IncludeFemaleText;
        _includeOtherLanguages   = initial.IncludeOtherLanguages;
        _persistDuplicateOptions = persistDuplicateOptions;
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
            var report = _dupScan(DuplicateOptions);

            foreach (var g in report.Exact)
            {
                var entry     = new IgnoredDuplicate(DuplicateKind.Exact, [g.Key], g.SampleText);
                var primary   = g.Members[0];
                var locations = string.Join(", ", g.Members.Select(Describe));
                DuplicateRows.Add(new DuplicateRowViewModel(
                    Loc.Get("Duplicate_Tier_Exact"), g.SampleText, locations,
                    () => _navigate?.Invoke(primary.ConversationName, primary.NodeId),
                    () => { _ignore?.Invoke(entry); Refresh(); }));
            }

            foreach (var p in report.Near)
            {
                var display   = Loc.Format("Duplicate_NearDisplay", p.A.Text, p.B.Text);
                var entry     = new IgnoredDuplicate(DuplicateKind.Near, p.Key, display);
                var locations = Describe(p.A) + ", " + Describe(p.B);
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

    /// Where a duplicate was found. A primary-language Default line renders exactly as
    /// it always did, so widening the scope leaves existing rows untouched; anything else
    /// gains a source marker, because "these two lines match" is unactionable when the
    /// reader cannot tell which field or language matched. Language follows the
    /// convention used by the tag rows above: "" means primary.
    private static string Describe(LineRef r)
    {
        var where = Loc.Format("Duplicate_Location", r.ConversationName, r.NodeId);
        var source = (r.Language.Length > 0, r.IsFemale) switch
        {
            (false, false) => "",
            (false, true)  => Loc.Get("Duplicate_Source_Female"),
            (true,  false) => r.Language,
            (true,  true)  => Loc.Format("Duplicate_Source_LanguageFemale", r.Language),
        };
        return source.Length == 0 ? where : Loc.Format("Duplicate_Source", where, source);
    }

    partial void OnCheckGameFilesChanged(bool value) => RefreshStale();

    // Each of the three re-scans immediately and persists the whole option set —
    // the report is the only way to judge whether a scope change was the right call.
    partial void OnNearThresholdChanged(double value)         => OnDuplicateOptionChanged();
    partial void OnIncludeFemaleTextChanged(bool value)       => OnDuplicateOptionChanged();
    partial void OnIncludeOtherLanguagesChanged(bool value)   => OnDuplicateOptionChanged();

    private void OnDuplicateOptionChanged()
    {
        _persistDuplicateOptions?.Invoke(DuplicateOptions);
        RefreshDuplicates();
    }
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
