using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;
using DialogEditor.Core.Logging;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One value row for a balance table, with the localised flag label and share text ready to bind.
public sealed class BalanceRowViewModel
{
    public string      DisplayValue { get; }
    public int         Count        { get; }
    public string      ShareText    { get; }
    public string      FlagLabel    { get; }
    public BalanceFlag Flag         { get; }
    public bool        IsUnresolved { get; }

    /// What a screen reader says for the row (issue 325); without it the list item fell back to
    /// this class's type name.
    public string AutomationName => FlagLabel.Length == 0
        ? Loc.Format("RepBalance_RowAutomationName", DisplayValue, Count, ShareText)
        : Loc.Format("RepBalance_RowAutomationNameFlagged", DisplayValue, Count, ShareText, FlagLabel);

    public BalanceRowViewModel(BalanceRow row)
    {
        DisplayValue = row.DisplayValue;
        Count        = row.Count;
        Flag         = row.Flag;
        IsUnresolved = row.IsUnresolved;
        ShareText    = row.Count == 0 ? "—" : Loc.Format("RepBalance_Share", row.ShareVsExpected);
        FlagLabel    = row.Flag switch
        {
            BalanceFlag.Over    => Loc.Get("RepBalance_Flag_Over"),
            BalanceFlag.Under   => Loc.Get("RepBalance_Flag_Under"),
            BalanceFlag.Ignored => Loc.Get("RepBalance_Flag_Ignored"),
            _                   => string.Empty,   // Normal rows carry no flag label
        };
    }
}

/// Read-only, project-wide reputation/disposition check-balance report. Presents two tables
/// (dispositions, reputations) over a chosen Source×Scope. The heavy full-corpus sweep runs
/// off the UI thread and is cancellable; the cheap scopes run synchronously.
public partial class RepDispositionBalanceViewModel : ObservableObject
{
    private readonly DialogProject     _project;
    private readonly IGameDataProvider _provider;
    private readonly Func<(string? Name, ConversationEditSnapshot? Snapshot)> _getOpen;
    private CancellationTokenSource?   _cts;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(EmptyHint))] private BalanceSource _source = BalanceSource.ProjectChanges;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EmptyHint))] private BalanceScope  _scope  = BalanceScope.Current;
    [ObservableProperty] private string        _statusText = string.Empty;
    [ObservableProperty] private bool          _isBusy;

    public ObservableCollection<BalanceRowViewModel> DispositionRows { get; } = [];
    public ObservableCollection<BalanceRowViewModel> ReputationRows  { get; } = [];

    // Blank until the first analysis finishes: while it runs, the status line says so.
    private bool _analysed;

    /// Why the Dispositions table is empty, or "" (GitHub issue 254).
    public string DispositionEmptyMessage =>
        _analysed && DispositionRows.Count == 0 ? Loc.Get("EmptyState_RepBalance_NoDispositions") : "";

    /// Why the Reputations table is empty, or "" (GitHub issue 254).
    public string ReputationEmptyMessage =>
        _analysed && ReputationRows.Count == 0 ? Loc.Get("EmptyState_RepBalance_NoReputations") : "";

    /// How to look further when a table is empty. "" when the search is already as wide as
    /// it goes (every conversation, on disk plus changes), as there is nowhere further to look.
    public string EmptyHint
    {
        get
        {
            if (DispositionEmptyMessage.Length == 0 && ReputationEmptyMessage.Length == 0) return "";
            if (Scope == BalanceScope.All && Source == BalanceSource.OnDiskPlusChanges) return "";
            // No project: the Source picker is hidden (issue 234), so only Scope can be widened.
            return Loc.Get(HasProject ? "EmptyState_RepBalance_Hint" : "EmptyState_RepBalance_Hint_NoProject");
        }
    }

    public RepDispositionBalanceViewModel(
        DialogProject project, IGameDataProvider provider,
        Func<(string? Name, ConversationEditSnapshot? Snapshot)> getOpenConversation,
        bool hasProject = true)
    {
        _project   = project;
        _provider  = provider;
        _getOpen   = getOpenConversation;
        HasProject = hasProject;
        // Without a project there is nothing to compare "changes" against; with the empty
        // stand-in project, OnDiskPlusChanges is simply the game on disk (issue 234).
        if (!hasProject) _source = BalanceSource.OnDiskPlusChanges;
    }

    /// False for a Reader with only a game folder open: the window hides the source picker,
    /// since every source other than the game on disk would be empty.
    public bool HasProject { get; }

    /// Scope tooltip: "the selected source" names a picker that is hidden without a project.
    public string ScopeTooltip => Loc.Get(HasProject ? "RepBalance_Tip_Scope" : "RepBalance_Tip_Scope_NoProject");

    /// Analyze tooltip, for the same reason as ScopeTooltip.
    public string AnalyzeTooltip => Loc.Get(HasProject ? "RepBalance_Tip_Refresh" : "RepBalance_Tip_Refresh_NoProject");

    [RelayCommand]
    private async Task Refresh()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var (openName, openSnap) = _getOpen();
        var source = Source;
        var scope  = Scope;
        var gameId = _provider.GameId;

        try
        {
            IsBusy = true;
            StatusText = Loc.Get("RepBalance_Analyzing");

            RepDispositionReport report =
                scope == BalanceScope.All && source == BalanceSource.OnDiskPlusChanges
                    ? await Task.Run(() =>
                        {
                            var convs = RepDispositionGatherService.Gather(
                                source, scope, _project, _provider, openName, openSnap, ct);
                            return RepDispositionTallyService.Analyze(convs, gameId, ConditionCatalogue.Instance);
                        }, ct)
                    : RepDispositionTallyService.Analyze(
                        RepDispositionGatherService.Gather(
                            source, scope, _project, _provider, openName, openSnap, ct),
                        gameId, ConditionCatalogue.Instance);

            DispositionRows.Clear();
            foreach (var r in report.DispositionRows) DispositionRows.Add(new BalanceRowViewModel(r));
            ReputationRows.Clear();
            foreach (var r in report.ReputationRows) ReputationRows.Add(new BalanceRowViewModel(r));

            _analysed = true;
            OnPropertyChanged(nameof(DispositionEmptyMessage));
            OnPropertyChanged(nameof(ReputationEmptyMessage));
            OnPropertyChanged(nameof(EmptyHint));

            StatusText = Loc.Format("RepBalance_Summary",
                report.ConversationsAnalyzed, report.DispositionTotal, report.ReputationTotal);
        }
        catch (OperationCanceledException) { /* deliberate cancel — swallow silently */ }
        catch (Exception ex)
        {
            AppLog.Error($"Rep/disposition balance failed: {ex}");
            StatusText = Loc.Get("RepBalance_Error");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
