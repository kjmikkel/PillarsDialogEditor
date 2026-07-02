using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Models;
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels.Models;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class NodeDetailViewModel : ObservableObject
{
    private NodeViewModel? _node;
    public  NodeViewModel? Node => _node;

    /// Set by MainWindowViewModel when a game folder is opened.
    public string ActiveGameId { get; set; } = string.Empty;

    /// Set by MainWindowViewModel when a game folder is opened.
    public string GameRoot { get; set; } = string.Empty;

    /// Set by MainWindowViewModel when a conversation is loaded.
    public ConversationViewModel? Canvas { get; set; }

    // ── Git attribution (read-only "last edited by") ──────────────────────
    /// Set by MainWindowViewModel: looks up (conversationName, nodeId) → last-touching commit.
    public Func<string, int, NodeBlame?>? AttributionLookup { get; set; }
    private NodeBlame? _attribution;

    public bool HasAttribution => _attribution is not null;

    /// Composed "author · date · short-sha" line for the loaded node (empty when none).
    public string LastEditedSummary => _attribution is null
        ? string.Empty
        : $"{_attribution.LastCommit.Author} · "
          + $"{_attribution.LastCommit.Date.ToString("d", CultureInfo.CurrentCulture)} · "
          + _attribution.LastCommit.ShortSha;

    public string LastEditedTooltip => _attribution is null
        ? string.Empty
        : Loc.Format("NodeDetail_LastEditedTooltip", _attribution.LastCommit.Subject, _attribution.LastCommit.Sha);

    // ── Translator note (backed by ConversationViewModel.NodeComments) ────
    private string _translatorNote = string.Empty;

    public string TranslatorNote
    {
        get => _translatorNote;
        set
        {
            if (_translatorNote == value) return;
            _translatorNote = value;
            OnPropertyChanged();
            if (_node is not null)
                Canvas?.SetNodeComment(_node.NodeId, value);
        }
    }

    [ObservableProperty] private bool _hasContent;

    // ── Editable proxy properties ─────────────────────────────────────────
    public string DefaultText
    {
        get => _node?.DefaultText ?? string.Empty;
        set { if (_node != null) _node.DefaultText = value; }
    }

    public string FemaleText
    {
        get => _node?.FemaleText ?? string.Empty;
        set { if (_node != null) _node.FemaleText = value; }
    }

    public bool IsPlayerChoice
    {
        get => _node?.IsPlayerChoice ?? false;
        set { if (_node != null) _node.IsPlayerChoice = value; }
    }

    public string SpeakerGuid
    {
        get => _node?.SpeakerGuid ?? string.Empty;
        set { if (_node != null) _node.SpeakerGuid = value; }
    }

    public string ListenerGuid
    {
        get => _node?.ListenerGuid ?? string.Empty;
        set { if (_node != null) _node.ListenerGuid = value; }
    }

    public string DisplayType
    {
        get => _node?.DisplayType ?? string.Empty;
        set { if (_node != null) _node.DisplayType = value; }
    }

    public string Persistence
    {
        get => _node?.Persistence ?? string.Empty;
        set { if (_node != null) _node.Persistence = value; }
    }

    public string ActorDirection
    {
        get => _node?.ActorDirection ?? string.Empty;
        set { if (_node != null) _node.ActorDirection = value; }
    }

    public string Comments
    {
        get => _node?.Comments ?? string.Empty;
        set { if (_node != null) _node.Comments = value; }
    }

    public string ExternalVO
    {
        get => _node?.ExternalVO ?? string.Empty;
        set { if (_node != null) _node.ExternalVO = value; }
    }

    public bool HasVO
    {
        get => _node?.HasVO ?? false;
        set { if (_node != null) _node.HasVO = value; }
    }

    public bool HideSpeaker
    {
        get => _node?.HideSpeaker ?? false;
        set { if (_node != null) _node.HideSpeaker = value; }
    }

    // ── VO file status (PoE2 only) ────────────────────────────────────
    private VoCheckResult? _voCheck;

    // ── Audio playback ────────────────────────────────────────────────────
    private enum Playing { None, Primary, Female }
    private Playing _currentlyPlaying = Playing.None;
    private IVoAudioPlayer _player = NullVoAudioPlayer.Instance;

    public IVoAudioPlayer Player
    {
        get => _player;
        set
        {
            _player.PlaybackStopped -= OnPlaybackStopped;
            _player = value;
            _player.PlaybackStopped += OnPlaybackStopped;
            OnPropertyChanged(nameof(CanPlayAudio));
        }
    }

    // ── VO import ────────────────────────────────────────────────────────
    private IVoImporter _importer = NullVoImporter.Instance;

    public IVoImporter Importer
    {
        get => _importer;
        set
        {
            _importer = value;
            ImportVoCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _projectPath;

    /// Set by MainWindowViewModel whenever a project is opened, created, or saved-as.
    /// Null when no project has been saved to disk yet.
    public string? ProjectPath
    {
        get => _projectPath;
        set
        {
            _projectPath = value;
            ImportVoCommand.NotifyCanExecuteChanged();
        }
    }

    /// Set by MainWindow.axaml.cs — shows VoImportDialog and returns user selections or null if cancelled.
    public Func<VoImportPaths, Task<VoImportDialogResult?>>? ShowImportDialog { get; set; }

    /// Set by MainWindowViewModel — receives one-line status messages to display in the status bar.
    public Action<string>? ReportStatus { get; set; }

    /// True when the VO import button should be visible: PoE2 with a game root and a loaded node.
    /// Independent of HasVO/ExternalVO so the button appears on fresh nodes too.
    public bool IsVoImportVisible => _voCheck is not null;

    /// True when a VO import can be initiated: PoE2 node loaded (project save is checked at runtime).
    public bool CanImportVo => _voCheck is not null;

    /// Tooltip for the import button.
    public string ImportVoTooltip => Loc.Get("ToolTip_VoImport");

    [RelayCommand(CanExecute = nameof(CanImportVo))]
    private async Task ImportVo()
    {
        if (ShowImportDialog is null) return;

        // Project must be saved so we know where the _vo/ folder lives.
        if (ProjectPath is null)
        {
            ReportStatus?.Invoke(Loc.Get("VoImport_UnsavedProject"));
            return;
        }

        // Clicking import on a fresh node (HasVO=false, no ExternalVO) implies the user
        // wants to add VO — set HasVO automatically so the path can be resolved.
        if (_node is not null && !_node.HasVO && string.IsNullOrEmpty(_node.ExternalVO))
        {
            _node.HasVO = true;
            // Re-evaluate _voCheck inline so PrimaryWemPath is available immediately
            // (NotifyAllProxies will also run via OnNodePropertyChanged, which is fine).
            _voCheck = VoPathResolver.Check(
                _node.SpeakerGuid, true, _node.ExternalVO, _node.HasFemaleText, _node.NodeId,
                Canvas?.ConversationName ?? "", GameRoot, ActiveGameId);
        }

        AppLog.Info($"ImportVo: status={_voCheck?.Status}, path={_voCheck?.PrimaryWemPath ?? "(null)"}, speaker={_node?.SpeakerGuid ?? "(null)"}");

        if (_voCheck?.PrimaryWemPath is null)
        {
            AppLog.Warn("ImportVo: PrimaryWemPath is null — speaker GUID not in ChatterPrefixService or empty");
            ReportStatus?.Invoke(Loc.Get("Status_VoImport_NoSpeaker"));
            return;
        }

        var voRoot = Path.Combine(GameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        var voDir       = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo");
        var rel         = Path.GetRelativePath(voRoot, _voCheck.PrimaryWemPath);
        var destPrimary = Path.Combine(voDir, rel);
        var destFem     = HasFemaleText
            ? Path.Combine(voDir, rel[..^4] + "_fem.wem")
            : null;

        var selection = await ShowImportDialog(new VoImportPaths(destPrimary, destFem));
        if (selection is null) return;

        var result = await Importer.ImportAsync(
            new VoImportRequest(destPrimary, selection.PrimarySourcePath,
                                destFem, selection.FemSourcePath,
                                selection.Quality), default);

        if (result.Success)
        {
            ReportStatus?.Invoke(Loc.Format("Status_VoImportSuccess",
                Path.GetFileName(selection.PrimarySourcePath)));
        }
        else
        {
            AppLog.Error($"VO import failed: {result.ErrorMessage}");
            ReportStatus?.Invoke(Loc.Format("Status_VoImportFailed", result.ErrorMessage ?? "unknown error"));
        }

        NotifyAllProxies(); // always refresh so UI reflects current reality
    }

    public bool IsPlayingPrimary => _currentlyPlaying == Playing.Primary;
    public bool IsPlayingFem     => _currentlyPlaying == Playing.Female;
    public bool CanPlayAudio     => _player.IsAvailable && VoStatusIsFound;
    public bool CanPlayFem       => CanPlayAudio && (_voCheck?.FemaleVariantFound ?? false);

    public string PlayPrimaryGlyph   => _currentlyPlaying == Playing.Primary ? "■" : "▶";
    public string PlayFemGlyph       => _currentlyPlaying == Playing.Female  ? "■" : "▶";
    public string PlayPrimaryTooltip => _currentlyPlaying == Playing.Primary
        ? Loc.Get("ToolTip_StopVO") : Loc.Get("ToolTip_PlayVO");
    public string PlayFemTooltip => _currentlyPlaying == Playing.Female
        ? Loc.Get("ToolTip_StopFemVO") : Loc.Get("ToolTip_PlayFemVO");

    [RelayCommand]
    private void PlayPrimary()
    {
        if (_currentlyPlaying == Playing.Primary)
        {
            _player.Stop();
            SetPlaying(Playing.None);
        }
        else
        {
            if (_voCheck?.PrimaryWemPath is null || !_player.IsAvailable) return;
            // LocalPrimaryWemPath is only set when the game copy is absent and the
            // project's _vo/ staging copy exists — play the file that is really there.
            _player.Stop();
            _player.Play(_voCheck.LocalPrimaryWemPath ?? _voCheck.PrimaryWemPath);
            SetPlaying(Playing.Primary);
        }
    }

    [RelayCommand]
    private void PlayFem()
    {
        if (_currentlyPlaying == Playing.Female)
        {
            _player.Stop();
            SetPlaying(Playing.None);
        }
        else
        {
            if (_voCheck?.FemWemPath is null || !_player.IsAvailable) return;
            _player.Stop();
            _player.Play(_voCheck.FemWemPath);
            SetPlaying(Playing.Female);
        }
    }

    private void SetPlaying(Playing p)
    {
        _currentlyPlaying = p;
        OnPropertyChanged(nameof(IsPlayingPrimary));
        OnPropertyChanged(nameof(IsPlayingFem));
        OnPropertyChanged(nameof(PlayPrimaryGlyph));
        OnPropertyChanged(nameof(PlayFemGlyph));
        OnPropertyChanged(nameof(PlayPrimaryTooltip));
        OnPropertyChanged(nameof(PlayFemTooltip));
    }

    // Called when a track ends naturally — Stop() does NOT trigger this.
    private void OnPlaybackStopped() => SetPlaying(Playing.None);

    public bool HasVoStatus     => _voCheck is { Status: not VoPresence.NotApplicable };
    public bool VoStatusIsFound => _voCheck?.Status == VoPresence.Found;

    public string VoStatusGlyph => _voCheck?.Status == VoPresence.Found ? "✓" : "✗";

    public string VoStatusText => _voCheck switch
    {
        { Status: VoPresence.Found, FemaleVariantFound: true }  => Loc.Get("VoStatus_FoundWithFem"),
        { Status: VoPresence.Found, FemaleVariantFound: false } => Loc.Get("VoStatus_Found"),
        _ => Loc.Get("VoStatus_Missing"),
    };

    public IReadOnlyList<string> BarkWarnings
    {
        get
        {
            var warnings = new List<string>(_node?.BarkWarnings ?? []);
            if (_node?.IsBark == true
                && Links.Any(l => l.Target.Owner?.IsPlayerChoice == true))
            {
                warnings.Add(Loc.Get("Bark_Warning_PlayerChoiceChild"));
            }
            return warnings;
        }
    }

    // ── SpeakerCategory proxy (enum ↔ string for ComboBox binding) ───────
    public string SpeakerCategoryString
    {
        get => _node?.SpeakerCategory switch
        {
            SpeakerCategory.Player   => Loc.Get("Speaker_Player"),
            SpeakerCategory.Narrator => Loc.Get("Speaker_Narrator"),
            SpeakerCategory.Script   => Loc.Get("Speaker_Script"),
            _                        => Loc.Get("Speaker_Npc"),
        };
        set
        {
            if (_node is null) return;
            var category =
                value == Loc.Get("Speaker_Player")   ? SpeakerCategory.Player   :
                value == Loc.Get("Speaker_Narrator") ? SpeakerCategory.Narrator :
                value == Loc.Get("Speaker_Script")   ? SpeakerCategory.Script   :
                                                        SpeakerCategory.Npc;
            _node.SpeakerCategory  = category;
            _node.IsPlayerChoice   = category == SpeakerCategory.Player;
        }
    }

    public static IReadOnlyList<string> SpeakerCategoryOptions =>
    [
        Loc.Get("Speaker_Npc"),
        Loc.Get("Speaker_Player"),
        Loc.Get("Speaker_Narrator"),
        Loc.Get("Speaker_Script"),
    ];

    // ── NodeType proxy (bool ↔ string for ComboBox binding) ──────────────
    public string NodeTypeString
    {
        get => IsPlayerChoice ? Loc.Get("Option_PlayerChoice") : Loc.Get("Option_NpcLine");
        set { if (_node != null) _node.IsPlayerChoice = value == Loc.Get("Option_PlayerChoice"); }
    }

    public static IReadOnlyList<string> NodeTypeOptions
        => [Loc.Get("Option_NpcLine"), Loc.Get("Option_PlayerChoice")];

    public static IReadOnlyList<string> DisplayTypeOptions
        => [Loc.Get("Option_DisplayConversation"), Loc.Get("Option_DisplayBark")];

    public static IReadOnlyList<string> PersistenceOptions
        => [Loc.Get("Option_PersistenceNone"), Loc.Get("Option_PersistenceOnceEver")];

    // ── Read-only display ─────────────────────────────────────────────────
    public string FemaleTextDisplay =>
        (_node?.HasFemaleText ?? false) ? _node!.FemaleText : Loc.Get("NodeDetail_SameAsDefault");

    public bool HasFemaleText => _node?.HasFemaleText ?? false;

    [ObservableProperty] private IReadOnlyList<PropertyGroup>      _propertyGroups  = [];
    [ObservableProperty] private IReadOnlyList<ConnectionViewModel> _links           = [];
    [ObservableProperty] private string                             _addLinkTargetId = string.Empty;

    // Raised when the user requests to add/delete a link — ConversationViewModel handles it
    public event Action<int, int>? AddLinkRequested;    // (fromNodeId, toNodeId)
    public event Action<int>?      DeleteLinkRequested; // (toNodeId)

    [RelayCommand]
    private void AddLink()
    {
        if (_node is null || !int.TryParse(AddLinkTargetId.Trim(), out var targetId)) return;
        AddLinkRequested?.Invoke(_node.NodeId, targetId);
        AddLinkTargetId = string.Empty;
    }

    [RelayCommand]
    private void DeleteLink(ConnectionViewModel? conn)
    {
        if (conn?.Target.Owner is { } target)
            DeleteLinkRequested?.Invoke(target.NodeId);
    }

    // ── Load / Clear ──────────────────────────────────────────────────────
    public void Load(NodeViewModel? node)
    {
        if (_node is not null)
            _node.PropertyChanged -= OnNodePropertyChanged;

        _node = node;

        _attribution = node is not null && Canvas?.ConversationName is string conv
            ? AttributionLookup?.Invoke(conv, node.NodeId)
            : null;
        OnPropertyChanged(nameof(HasAttribution));
        OnPropertyChanged(nameof(LastEditedSummary));
        OnPropertyChanged(nameof(LastEditedTooltip));

        if (node is null) { HasContent = false; ConditionRows.Clear(); return; }

        node.PropertyChanged += OnNodePropertyChanged;
        RefreshReadOnlyGroups(node);
        RebuildConditionRows(node);
        HasContent = true;
        _translatorNote = Canvas?.GetNodeComment(node.NodeId) ?? string.Empty;
        OnPropertyChanged(nameof(TranslatorNote));
        NotifyAllProxies();
    }

    public void Clear() => Load(null);

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyAllProxies();
        if (_node is not null)
            RefreshReadOnlyGroups(_node);
        if (e.PropertyName == nameof(NodeViewModel.Conditions))
            OnPropertyChanged(nameof(ConditionSummary));
        if (e.PropertyName == nameof(NodeViewModel.Scripts))
        {
            if (_node is not null) RefreshReadOnlyGroups(_node);
            OnPropertyChanged(nameof(ScriptSummary));
        }
    }

    private void NotifyAllProxies()
    {
        // Stop without firing PlaybackStopped; reset state explicitly.
        _player.Stop();
        SetPlaying(Playing.None);

        OnPropertyChanged(nameof(DefaultText));
        OnPropertyChanged(nameof(FemaleText));
        OnPropertyChanged(nameof(SpeakerCategoryString));
        OnPropertyChanged(nameof(FemaleTextDisplay));
        OnPropertyChanged(nameof(HasFemaleText));
        OnPropertyChanged(nameof(IsPlayerChoice));
        OnPropertyChanged(nameof(NodeTypeString));
        OnPropertyChanged(nameof(SpeakerGuid));
        OnPropertyChanged(nameof(ListenerGuid));
        OnPropertyChanged(nameof(DisplayType));
        OnPropertyChanged(nameof(Persistence));
        OnPropertyChanged(nameof(ActorDirection));
        OnPropertyChanged(nameof(Comments));
        OnPropertyChanged(nameof(ExternalVO));
        OnPropertyChanged(nameof(HasVO));
        OnPropertyChanged(nameof(HideSpeaker));
        OnPropertyChanged(nameof(TranslatorNote));

        // Keep AutoCompleteBox selections in sync whenever any proxy changes
        _selectedSpeakerEntry  = SpeakerNameService.FindByGuid(_node?.SpeakerGuid);
        _selectedListenerEntry = SpeakerNameService.FindByGuid(_node?.ListenerGuid);
        OnPropertyChanged(nameof(SelectedSpeakerEntry));
        OnPropertyChanged(nameof(SelectedListenerEntry));
        OnPropertyChanged(nameof(BarkWarnings));

        _voCheck = _node is null ? null
            : VoPathResolver.Check(
                _node.SpeakerGuid, _node.HasVO, _node.ExternalVO, _node.HasFemaleText, _node.NodeId,
                Canvas?.ConversationName ?? "", GameRoot, ActiveGameId);

        // If the game file is absent but a _vo/ local copy exists, treat it as Found.
        // This lets the status row flip to ✓ immediately after import without waiting for F5.
        if (_voCheck is not null && !string.IsNullOrEmpty(GameRoot))
            _voCheck = VoPathResolver.WithLocalVoFallback(_voCheck, ProjectPath, GameRoot,
                _node?.HasFemaleText ?? false);

        OnPropertyChanged(nameof(IsVoImportVisible));
        OnPropertyChanged(nameof(CanImportVo));
        OnPropertyChanged(nameof(HasVoStatus));
        OnPropertyChanged(nameof(VoStatusGlyph));
        OnPropertyChanged(nameof(VoStatusText));
        OnPropertyChanged(nameof(VoStatusIsFound));
        OnPropertyChanged(nameof(CanPlayAudio));
        OnPropertyChanged(nameof(CanPlayFem));
        ImportVoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ImportVoTooltip));
    }

    public void Refresh() => NotifyAllProxies();

    // ── Speaker / Listener name picker ───────────────────────────────────

    /// True when the loaded game has speaker name data (PoE2); hides picker for PoE1.
    public bool HasSpeakerData => SpeakerNameService.HasNames;

    /// All known speakers from the loaded game, sorted by name.
    public IReadOnlyList<SpeakerEntry> AvailableSpeakers => SpeakerNameService.All;

    private SpeakerEntry? _selectedSpeakerEntry;
    private SpeakerEntry? _selectedListenerEntry;

    public SpeakerEntry? SelectedSpeakerEntry
    {
        get => _selectedSpeakerEntry;
        set
        {
            if (SetProperty(ref _selectedSpeakerEntry, value) && value is not null)
                SpeakerGuid = value.Guid;
        }
    }

    public SpeakerEntry? SelectedListenerEntry
    {
        get => _selectedListenerEntry;
        set
        {
            if (SetProperty(ref _selectedListenerEntry, value) && value is not null)
                ListenerGuid = value.Guid;
        }
    }

    private void RefreshReadOnlyGroups(NodeViewModel node)
    {
        var none = Loc.Get("NodeDetail_None");
        PropertyGroups =
        [
            new PropertyGroup(Loc.Get("Label_GroupIdentity"),
            [
                new PropertyRow(Loc.Get("PropertyRow_NodeId"), node.NodeId.ToString()),
            ]),
            // Scripts and Conditions each have their own editable panel sections.
        ];
    }

    public void RefreshLinks(IEnumerable<ConnectionViewModel> connections)
    {
        Links = connections.ToList();
        OnPropertyChanged(nameof(BarkWarnings));
    }

    public void NotifyConditionSummary()
        => OnPropertyChanged(nameof(ConditionSummary));

    public void NotifyScriptSummary()
        => OnPropertyChanged(nameof(ScriptSummary));

    public string ScriptSummary
    {
        get
        {
            if (_node is null || _node.Scripts.Count == 0)
                return Loc.Get("NodeDetail_None");
            return string.Join(", ", _node.Scripts.Select(s => s.DisplayName));
        }
    }

    // ── Condition editing ─────────────────────────────────────────────────

    public ObservableCollection<ConditionRowViewModel> ConditionRows { get; } = [];

    /// Brief summary shown in the detail panel (replaces the old inline editor).
    public string ConditionSummary
    {
        get
        {
            if (_node is null || _node.Conditions.Count == 0)
                return Loc.Get("NodeDetail_None");
            var names = _node.Conditions
                .OfType<ConditionLeaf>()
                .Select(l =>
                {
                    // FindByFullName(fullName, gameId) resolves both game variants and same-signature entries
                    var entry = ConditionCatalogue.Instance.FindByFullName(l.FullName, ActiveGameId)
                             ?? ConditionCatalogue.Instance.Find(
                                    l.FullName.Contains(' ')
                                        ? l.FullName[(l.FullName.IndexOf(' ') + 1)..].Split('(')[0]
                                        : l.FullName);
                    return entry?.DisplayName ?? (l.FullName.Contains(' ')
                        ? l.FullName[(l.FullName.IndexOf(' ') + 1)..].Split('(')[0]
                        : l.FullName);
                });
            return string.Join(", ", names);
        }
    }


    private void RebuildConditionRows(NodeViewModel node)
    {
        ConditionRows.Clear();
        foreach (var c in node.Conditions)
        {
            if (c is ConditionLeaf leaf)
            {
                // FindByFullName(fullName, gameId) picks the correct game-specific variant
                var entry = ConditionCatalogue.Instance.FindByFullName(leaf.FullName, ActiveGameId)
                         ?? ConditionCatalogue.Instance.Find(
                                leaf.FullName.Contains(' ')
                                    ? leaf.FullName[(leaf.FullName.IndexOf(' ') + 1)..]
                                        .Split('(')[0]
                                    : leaf.FullName);
                var row = new ConditionRowViewModel(leaf, entry);
                SubscribeRow(row);
                ConditionRows.Add(row);
            }
            else if (c is ConditionBranch branch)
            {
                // Branch rows are read-only pass-throughs — no subscription needed
                ConditionRows.Add(new ConditionRowViewModel(branch));
            }
        }
    }

    private void SubscribeRow(ConditionRowViewModel row)
    {
        row.PropertyChanged += OnRowPropertyChanged;
        foreach (var p in row.Parameters)
            p.PropertyChanged += OnRowPropertyChanged;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => CommitConditions();

    public void AddCondition(ConditionEntry entry)
    {
        if (_node is null) return;
        var defaults = entry.Parameters.Select(p => p.Default).ToList();
        var leaf     = new ConditionLeaf(entry.ReflectionFullName, defaults, false, "And");
        var row      = new ConditionRowViewModel(leaf, entry);
        SubscribeRow(row);
        ConditionRows.Add(row);
        CommitConditions();
    }

    public void DeleteConditionRow(ConditionRowViewModel row)
    {
        if (ConditionRows.Remove(row))
            CommitConditions();
    }

    public void MoveConditionUp(ConditionRowViewModel row)
    {
        var i = ConditionRows.IndexOf(row);
        if (i > 0) { ConditionRows.Move(i, i - 1); CommitConditions(); }
    }

    public void MoveConditionDown(ConditionRowViewModel row)
    {
        var i = ConditionRows.IndexOf(row);
        if (i >= 0 && i < ConditionRows.Count - 1) { ConditionRows.Move(i, i + 1); CommitConditions(); }
    }

    /// Builds the structured condition list from current rows and pushes it to NodeViewModel.
    public void CommitConditions()
    {
        if (_node is null) return;
        _node.Conditions = ConditionRows.Select(r => r.ToNode()).ToList();
    }
}
