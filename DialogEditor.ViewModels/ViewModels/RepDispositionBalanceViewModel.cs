using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;
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

    [ObservableProperty] private BalanceSource _source = BalanceSource.ProjectChanges;
    [ObservableProperty] private BalanceScope  _scope  = BalanceScope.Current;
    [ObservableProperty] private string        _statusText = string.Empty;
    [ObservableProperty] private bool          _isBusy;

    public ObservableCollection<BalanceRowViewModel> DispositionRows { get; } = [];
    public ObservableCollection<BalanceRowViewModel> ReputationRows  { get; } = [];

    public RepDispositionBalanceViewModel(
        DialogProject project, IGameDataProvider provider,
        Func<(string? Name, ConversationEditSnapshot? Snapshot)> getOpenConversation)
    {
        _project  = project;
        _provider = provider;
        _getOpen  = getOpenConversation;
    }

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
