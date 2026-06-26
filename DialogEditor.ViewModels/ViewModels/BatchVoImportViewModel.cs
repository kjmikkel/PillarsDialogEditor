using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public enum BatchRowStatus { Pending, Importing, Done, Error }

public partial class BatchVoRowViewModel : ObservableObject
{
    // ── Init-only ────────────────────────────────────────────────────────
    public string     ConversationName { get; }
    public int        NodeId           { get; }
    public string     TextPreview      { get; }
    public VoPresence VoStatus         { get; }
    public string     DestPrimaryPath  { get; }
    public string     DestFemPath      { get; }

    // ── Observable ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrimarySource))]
    [NotifyPropertyChangedFor(nameof(PrimaryFileLabel))]
    [NotifyPropertyChangedFor(nameof(PrimaryPlayGlyph))]
    private string? _primarySourcePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FemFileLabel))]
    [NotifyPropertyChangedFor(nameof(FemPlayGlyph))]
    private string? _femSourcePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowStatusGlyph))]
    private BatchRowStatus _rowStatus = BatchRowStatus.Pending;

    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryPlayGlyph))]
    private bool _isPlayingPrimary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FemPlayGlyph))]
    private bool _isPlayingFem;

    // ── Computed ─────────────────────────────────────────────────────────
    public bool   HasPrimarySource => PrimarySourcePath is not null;
    public string PrimaryFileLabel => PrimarySourcePath is not null
        ? Path.GetFileName(PrimarySourcePath) : "—";
    public string FemFileLabel     => FemSourcePath is not null
        ? Path.GetFileName(FemSourcePath) : "—";
    public string VoStatusGlyph    => VoStatus == VoPresence.Found ? "✓" : "✗";
    public string RowStatusGlyph   => RowStatus switch
    {
        BatchRowStatus.Done      => "✓",
        BatchRowStatus.Error     => "✗",
        BatchRowStatus.Importing => "…",
        _                        => ""
    };
    public string PrimaryPlayGlyph => IsPlayingPrimary ? "■" : "▶";
    public string FemPlayGlyph     => IsPlayingFem     ? "■" : "▶";

    public BatchVoRowViewModel(
        string conversationName, int nodeId, string textPreview,
        VoPresence voStatus, string destPrimaryPath, string destFemPath)
    {
        ConversationName = conversationName;
        NodeId           = nodeId;
        TextPreview      = textPreview;
        VoStatus         = voStatus;
        DestPrimaryPath  = destPrimaryPath;
        DestFemPath      = destFemPath;
    }
}

public partial class BatchVoImportViewModel : ObservableObject
{
    private readonly IVoImporter _importer;
    private CancellationTokenSource _cts = new();

    public IReadOnlyList<BatchVoRowViewModel>        AllRows     { get; }
    public ObservableCollection<BatchVoRowViewModel> VisibleRows { get; } = [];

    /// When true the Conversation column is hidden — all rows share the same conversation.
    public bool IsSingleConversation { get; }

    [ObservableProperty] private bool       _showOnlyMissing = true;
    [ObservableProperty] private WemQuality _quality         = WemQuality.Medium;
    [ObservableProperty] private bool       _isImporting;
    [ObservableProperty] private string     _progressText    = string.Empty;

    public BatchVoImportViewModel(IReadOnlyList<BatchVoRowViewModel> rows, IVoImporter importer,
        bool isSingleConversation = true)
    {
        AllRows              = rows;
        _importer            = importer;
        IsSingleConversation = isSingleConversation;
        RefreshVisibleRows();
    }

    partial void OnShowOnlyMissingChanged(bool value) => RefreshVisibleRows();

    private void RefreshVisibleRows()
    {
        VisibleRows.Clear();
        foreach (var row in AllRows)
            if (!ShowOnlyMissing || row.VoStatus != VoPresence.Found)
                VisibleRows.Add(row);
    }

    /// Called from dialog code-behind after Browse/Clear so CanExecute re-evaluates.
    public void OnRowChanged() => ImportCommand.NotifyCanExecuteChanged();

    public void Cancel()
    {
        if (IsImporting) _cts.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        _cts = new CancellationTokenSource();
        IsImporting = true;
        ImportCommand.NotifyCanExecuteChanged();

        var toImport = AllRows.Where(r => r.HasPrimarySource).ToList();
        var done     = 0;
        ProgressText = Loc.Format("BatchVoImport_Progress", done, toImport.Count);

        try
        {
            foreach (var row in toImport)
            {
                _cts.Token.ThrowIfCancellationRequested();
                row.RowStatus = BatchRowStatus.Importing;
                try
                {
                    var result = await _importer.ImportAsync(
                        new VoImportRequest(
                            row.DestPrimaryPath, row.PrimarySourcePath!,
                            row.DestFemPath,     row.FemSourcePath,
                            Quality),
                        _cts.Token);

                    if (result.Success)
                        row.RowStatus = BatchRowStatus.Done;
                    else
                    {
                        row.RowStatus    = BatchRowStatus.Error;
                        row.ErrorMessage = result.ErrorMessage;
                        AppLog.Error($"Batch VO import failed for node {row.NodeId}: {result.ErrorMessage}");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLog.Error($"Batch VO import exception for node {row.NodeId}", ex);
                    row.RowStatus    = BatchRowStatus.Error;
                    row.ErrorMessage = ex.Message;
                }

                done++;
                ProgressText = Loc.Format("BatchVoImport_Progress", done, toImport.Count);
            }
        }
        catch (OperationCanceledException) { /* deliberate cancellation — swallow silently */ }
        finally
        {
            IsImporting = false;
            ImportCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanImport() => !IsImporting && AllRows.Any(r => r.HasPrimarySource);
}
