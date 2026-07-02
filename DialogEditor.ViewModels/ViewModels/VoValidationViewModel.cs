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
public record VoValidationIssue(int NodeId, string TextPreview, bool IsMissing)
{
    /// Localised "Node {0}" label used in the VoValidationWindow results list.
    /// Populated via Loc.Format so the format string comes from Strings.axaml
    /// rather than being hard-coded in XAML (CLAUDE.md localisation rule).
    public string NodeLabel => Loc.Format("VoValidation_NodeRow", NodeId);
}

/// A .wem in _vo/ that no VO-enabled node references (candidate for cleanup).
public record VoOrphanIssue(string FullPath, string RelativePath);

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
    private readonly string? _projectPath;

    // Cancelled and replaced at the start of each RunAsync call.
    private CancellationTokenSource _cts = new();
    private bool _ranAtLeastOnce;

    [ObservableProperty] private bool   _isRunning;
    [ObservableProperty] private string _summaryText = string.Empty;

    public ObservableCollection<VoValidationIssue> Results { get; } = [];

    /// True once at least one missing-VO issue exists in Results.
    /// Drives the empty-state ("all found") visibility in the window.
    public bool HasResults => Results.Count > 0;

    /// True when the scan has finished at least once and found zero issues — drives
    /// the "all VO files found" empty-state message in VoValidationWindow.
    public bool ShowAllFound => _ranAtLeastOnce && !IsRunning && !HasResults;

    public IRelayCommand CancelCommand   { get; }
    public IRelayCommand RunAgainCommand { get; }

    /// Project-wide orphan scan, injected by MainWindowViewModel (wraps
    /// VoOrphanScanner.FindOrphans with the live project/provider/canvas).
    /// Null (e.g. no project open) disables the orphan section.
    public Func<CancellationToken, IReadOnlyList<string>>? OrphanScanner { get; set; }

    /// The _vo/ folder; used to compute display-relative paths and prune
    /// empty prefix directories after cleanup.
    public string? VoRootPath { get; set; }

    public ObservableCollection<VoOrphanIssue> OrphanResults { get; } = [];
    public bool HasOrphans => OrphanResults.Count > 0;

    [ObservableProperty] private bool _isCleanUpArmed;

    /// Localised "Delete {0} file(s)…" confirmation line for the armed state.
    public string CleanUpConfirmText => Loc.Format("VoValidation_CleanUpConfirm", OrphanResults.Count);

    public IRelayCommand CleanUpCommand        { get; }
    public IRelayCommand ConfirmCleanUpCommand { get; }
    public IRelayCommand CancelCleanUpCommand  { get; }

    public VoValidationViewModel(
        IReadOnlyList<NodeEditSnapshot> nodes,
        string conversationName,
        string gameRoot,
        string activeGameId,
        string? projectPath = null)
    {
        _nodes            = nodes;
        _conversationName = conversationName;
        _gameRoot         = gameRoot;
        _activeGameId     = activeGameId;
        _projectPath      = projectPath;

        CancelCommand   = new RelayCommand(() => _cts.Cancel(), () => IsRunning);
        // RelayCommand does not support async lambdas; call RunAsync() via fire-and-discard.
        RunAgainCommand = new RelayCommand(() => _ = RunAsync(), () => !IsRunning);

        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));

        CleanUpCommand        = new RelayCommand(() => IsCleanUpArmed = true,  () => HasOrphans && !IsCleanUpArmed);
        ConfirmCleanUpCommand = new RelayCommand(ExecuteCleanUp,               () => IsCleanUpArmed);
        CancelCleanUpCommand  = new RelayCommand(() => IsCleanUpArmed = false, () => IsCleanUpArmed);

        OrphanResults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasOrphans));
            OnPropertyChanged(nameof(CleanUpConfirmText));
            CleanUpCommand.NotifyCanExecuteChanged();
        };
    }

    partial void OnIsRunningChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        RunAgainCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowAllFound));
    }

    partial void OnIsCleanUpArmedChanged(bool value)
    {
        CleanUpCommand.NotifyCanExecuteChanged();
        ConfirmCleanUpCommand.NotifyCanExecuteChanged();
        CancelCleanUpCommand.NotifyCanExecuteChanged();
    }

    private void ExecuteCleanUp()
    {
        var files  = OrphanResults.Select(o => o.FullPath).ToList();
        var failed = 0;
        foreach (var f in files)
        {
            try { File.Delete(f); }
            catch (Exception ex) { failed++; AppLog.Warn($"VO cleanup: could not delete '{f}': {ex.Message}"); }
        }
        PruneEmptyVoDirectories();
        IsCleanUpArmed = false;
        OrphanResults.Clear();
        SummaryText = failed == 0
            ? Loc.Format("VoValidation_CleanedUp", files.Count)
            : Loc.Format("VoValidation_CleanUpPartial", files.Count - failed, failed);
        _ = RunAsync();   // refresh both sections against reality
    }

    private void PruneEmptyVoDirectories()
    {
        if (VoRootPath is null || !Directory.Exists(VoRootPath)) return;
        foreach (var dir in Directory.GetDirectories(VoRootPath, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(d => d.Length))   // deepest first
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex) { AppLog.Warn($"VO cleanup: could not remove empty dir '{dir}': {ex.Message}"); }
        }
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

        var checked_    = 0;
        var missing     = 0;
        var cancelled   = false;
        var batch       = new List<VoValidationIssue>();
        var orphanBatch = new List<VoOrphanIssue>();

        try
        {
            await Task.Run(() =>
            {
                foreach (var node in _nodes)
                {
                    token.ThrowIfCancellationRequested();

                    var result = VoPathResolver.Check(
                        node.SpeakerGuid, node.HasVO, node.ExternalVO, node.FemaleText.Length > 0,
                        node.NodeId, _conversationName, _gameRoot, _activeGameId);

                    // null  → feature not applicable for this game ID
                    // NotApplicable → node carries no VO information
                    if (result is null || result.Status == VoPresence.NotApplicable)
                        continue;

                    // A file staged in the project's _vo/ folder counts as present —
                    // F6 removes the game copy, but F5 will re-sync it (B-006).
                    result = VoPathResolver.WithLocalVoFallback(result, _projectPath, _gameRoot,
                        node.FemaleText.Length > 0);

                    checked_++;
                    if (result.Status == VoPresence.Missing)
                    {
                        missing++;
                        batch.Add(new VoValidationIssue(node.NodeId, BuildPreview(node.DefaultText), IsMissing: true));
                    }
                }

                if (OrphanScanner is not null)
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var path in OrphanScanner(token))
                        orphanBatch.Add(new VoOrphanIssue(path,
                            VoRootPath is not null ? Path.GetRelativePath(VoRootPath, path)
                                                   : Path.GetFileName(path)));
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

            OrphanResults.Clear();
            foreach (var issue in orphanBatch)
                OrphanResults.Add(issue);
            IsCleanUpArmed = false;

            _ranAtLeastOnce = true;
            IsRunning   = false;
            SummaryText = cancelled
                ? Loc.Format("VoValidation_Cancelled", checked_, missing)
                : Loc.Format("VoValidation_Summary",   checked_, missing);
            OnPropertyChanged(nameof(ShowAllFound));
        }
    }

    private static string BuildPreview(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 60 ? trimmed : trimmed[..60] + "…";
    }
}
