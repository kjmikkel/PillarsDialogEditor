using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// <summary>
/// Represents a single node whose VO file could not be found on disk.
/// </summary>
public record VoValidationIssue(int NodeId, string TextPreview, bool IsMissing);

/// <summary>
/// Async scan ViewModel that checks every VO-enabled node in a conversation against
/// the game's audio folder and surfaces missing .wem files.
///
/// Design notes:
/// - The scan runs on Task.Run to keep the UI responsive.
/// - Results are collected in a local list during the scan, then applied to the
///   ObservableCollection on the caller's thread after Task.Run completes.
///   This avoids a race between UIThread.Post fire-and-forget calls and the
///   awaiter continuing — important both in production (where RunAsync is called
///   from the UI thread and results would be batched anyway) and in unit tests
///   (which run on threadpool threads without a Dispatcher pump).
/// - OperationCanceledException is swallowed silently per project convention.
/// - RunAgainCommand calls RunAsync() via "fire and discard" (_= RunAsync()) because
///   RelayCommand does not natively support async lambdas.
/// </summary>
public partial class VoValidationViewModel : ObservableObject
{
    private readonly IReadOnlyList<NodeEditSnapshot> _nodes;
    private readonly string _conversationName;
    private readonly string _gameRoot;
    private readonly string _activeGameId;

    // Cancelled and replaced at the start of each RunAsync call.
    private CancellationTokenSource _cts = new();

    [ObservableProperty] private bool   _isRunning;
    [ObservableProperty] private string _summaryText = string.Empty;

    public ObservableCollection<VoValidationIssue> Results { get; } = [];

    public IRelayCommand CancelCommand   { get; }
    public IRelayCommand RunAgainCommand { get; }

    public VoValidationViewModel(
        IReadOnlyList<NodeEditSnapshot> nodes,
        string conversationName,
        string gameRoot,
        string activeGameId)
    {
        _nodes            = nodes;
        _conversationName = conversationName;
        _gameRoot         = gameRoot;
        _activeGameId     = activeGameId;

        CancelCommand   = new RelayCommand(() => _cts.Cancel(), () => IsRunning);
        // RelayCommand does not support async lambdas; call RunAsync() via fire-and-discard.
        RunAgainCommand = new RelayCommand(() => _ = RunAsync(), () => !IsRunning);
    }

    partial void OnIsRunningChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        RunAgainCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Runs the VO presence scan. Safe to call multiple times; each call cancels
    /// any in-progress scan before starting a new one.
    /// </summary>
    public async Task RunAsync()
    {
        // Cancel any previous scan and start a fresh token.
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Results.Clear();
        IsRunning   = true;
        SummaryText = Loc.Get("VoValidation_Running");

        var checked_  = 0;
        var missing   = 0;
        var cancelled = false;
        var batch     = new List<VoValidationIssue>();

        try
        {
            await Task.Run(() =>
            {
                foreach (var node in _nodes)
                {
                    token.ThrowIfCancellationRequested();

                    var result = VoPathResolver.Check(
                        node.SpeakerGuid, node.HasVO, node.ExternalVO, node.NodeId,
                        _conversationName, _gameRoot, _activeGameId);

                    // null  → feature not applicable for this game ID
                    // NotApplicable → node carries no VO information
                    if (result is null || result.Status == VoPresence.NotApplicable)
                        continue;

                    checked_++;
                    if (result.Status == VoPresence.Missing)
                    {
                        missing++;
                        batch.Add(new VoValidationIssue(node.NodeId, BuildPreview(node.DefaultText), IsMissing: true));
                    }
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            // Deliberate cancellation — swallow silently per project convention.
            cancelled = true;
        }
        catch (Exception ex)
        {
            // Unexpected failure (e.g. IOException from VoPathResolver.Check).
            // Log it and treat the run as cancelled so the UI recovers cleanly.
            AppLog.Error("VO validation scan failed unexpectedly", ex);
            cancelled = true;
        }
        finally
        {
            // Apply results on the caller's thread (UI thread in production; test thread in tests).
            foreach (var issue in batch)
                Results.Add(issue);

            IsRunning   = false;
            SummaryText = cancelled
                ? Loc.Format("VoValidation_Cancelled", checked_, missing)
                : Loc.Format("VoValidation_Summary",   checked_, missing);
        }
    }

    private static string BuildPreview(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 60 ? trimmed : trimmed[..60] + "…";
    }
}
