using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One entry in the character picker: a speaker with a resolved display name and
/// its line count. ToString drives ComboBox display and text search.
public record SpeakerPickerItem(string Guid, string DisplayName, int Count)
{
    public override string ToString() => $"{DisplayName} ({Count})";
}

/// <summary>
/// Drives the Speaker Line Browser. Runs the whole-game <see cref="SpeakerLineScanner"/>
/// off the UI thread (cancellable), derives the character picker from the returned rows,
/// and filters those rows in memory when the selected speaker or the "only my lines"
/// toggle changes — so switching characters never re-reads the game folder. Refresh
/// re-scans. The scan function is injectable so tests can bypass IO.
/// Spec: docs/superpowers/specs/2026-07-13-speaker-line-browser-design.md
/// </summary>
public partial class SpeakerLineBrowserViewModel : ObservableObject
{
    private readonly DialogProject _project;
    private readonly IGameDataProvider _provider;
    private readonly string _primaryLanguage;
    private readonly Func<(string? Name, ConversationEditSnapshot? Snapshot)> _openAccessor;
    private readonly string? _initialSpeakerGuid;
    private readonly Func<string?, ConversationEditSnapshot?, CancellationToken, IReadOnlyList<SpeakerLineRow>> _scan;

    private IReadOnlyList<SpeakerLineRow> _allRows = [];
    private CancellationTokenSource? _cts;

    public SpeakerLineBrowserViewModel(
        DialogProject project,
        IGameDataProvider provider,
        string primaryLanguage,
        Func<(string? Name, ConversationEditSnapshot? Snapshot)> openConversationAccessor,
        string? initialSpeakerGuid = null,
        Func<string?, ConversationEditSnapshot?, CancellationToken, IReadOnlyList<SpeakerLineRow>>? scanner = null)
    {
        _project            = project;
        _provider           = provider;
        _primaryLanguage    = primaryLanguage;
        _openAccessor       = openConversationAccessor;
        _initialSpeakerGuid = initialSpeakerGuid;
        _scan = scanner ?? ((name, snap, ct) =>
            SpeakerLineScanner.Scan(_project, _provider, _primaryLanguage, name, snap, ct));
    }

    public ObservableCollection<SpeakerPickerItem> Speakers { get; } = [];

    public IReadOnlyList<SpeakerLineRow> Rows { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private SpeakerPickerItem? _selectedSpeaker;
    [ObservableProperty] private bool _onlyMyLines;

    partial void OnSelectedSpeakerChanged(SpeakerPickerItem? value) => ApplyFilter();
    partial void OnOnlyMyLinesChanged(bool value) => ApplyFilter();

    /// Raised with (conversationName, nodeId) when the user activates a row.
    public event Action<string, int>? RequestNavigate;
    public void NavigateTo(SpeakerLineRow row) => RequestNavigate?.Invoke(row.ConversationName, row.NodeId);

    public async Task ScanAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;
        StatusText = Loc.Get("SpeakerLines_Scanning");
        var (name, snap) = _openAccessor();

        try
        {
            var rows = await Task.Run(() => _scan(name, snap, token), token);
            _allRows = rows;

            var speakers = rows
                .GroupBy(r => r.SpeakerGuid, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SpeakerPickerItem(
                    g.Key, SpeakerNameService.Resolve(g.Key) ?? g.Key, g.Count()))
                .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Speakers.Clear();
            foreach (var s in speakers) Speakers.Add(s);

            SelectedSpeaker =
                (_initialSpeakerGuid is not null
                    ? speakers.FirstOrDefault(s =>
                          string.Equals(s.Guid, _initialSpeakerGuid, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? speakers.FirstOrDefault();

            ApplyFilter();   // covers the case where SelectedSpeaker did not change value
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.Get("SpeakerLines_Cancelled");   // deliberate cancel — swallowed
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task Refresh() => ScanAsync();

    private bool CanCancelScan() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan() => _cts?.Cancel();

    private void ApplyFilter()
    {
        var guid = SelectedSpeaker?.Guid;
        IEnumerable<SpeakerLineRow> q = guid is null
            ? []
            : _allRows.Where(r => string.Equals(r.SpeakerGuid, guid, StringComparison.OrdinalIgnoreCase));
        if (OnlyMyLines) q = q.Where(r => r.Origin != LineOrigin.Vanilla);

        Rows = q.ToList();
        OnPropertyChanged(nameof(Rows));
        StatusText = Rows.Count == 0
            ? Loc.Get("SpeakerLines_NoLines")
            : Loc.FormatCount("SpeakerLines_LineCount", Rows.Count);
    }
}
