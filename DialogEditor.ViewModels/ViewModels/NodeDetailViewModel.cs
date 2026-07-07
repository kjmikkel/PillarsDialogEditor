using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Audio;
using DialogEditor.Core.Models;
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class NodeDetailViewModel : ObservableObject
{
    private NodeViewModel? _node;
    public  NodeViewModel? Node => _node;

    private readonly TokenValidationService _tokenValidator = new();

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
            OnPropertyChanged(nameof(NotesSummary));
            if (_node is not null)
                Canvas?.SetNodeComment(_node.NodeId, value);
        }
    }

    [ObservableProperty] private bool _hasContent;

    // ── Editable proxy properties ─────────────────────────────────────────
    public string DefaultText
    {
        get => _node?.DefaultText ?? string.Empty;
        set { if (_node != null) { _node.DefaultText = value; OnPropertyChanged(nameof(TokenWarnings)); } }
    }

    public string FemaleText
    {
        get => _node?.FemaleText ?? string.Empty;
        set { if (_node != null) { _node.FemaleText = value; OnPropertyChanged(nameof(TokenWarnings)); } }
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

    /// Set by MainWindow.axaml.cs — asks the user what to do when importing over
    /// an ExternalVO alias (the target file is shared with other nodes).
    public Func<VoAliasImportPrompt, Task<VoAliasImportChoice>>? ConfirmAliasedImport { get; set; }

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

        // Guard: the alias target is shared audio — importing overwrites it for
        // every node that aliases it (audit 2026-07-03: up to 11 nodes share one
        // file in shipped data). Confirm, or clear the alias and give this node
        // its own recording.
        if (HasVoAlias && ConfirmAliasedImport is not null)
        {
            var choice = await ConfirmAliasedImport(new VoAliasImportPrompt(
                _node!.ExternalVO, VoAliasSharedCount ?? 0));
            if (choice == VoAliasImportChoice.Cancel) return;
            if (choice == VoAliasImportChoice.ClearAliasImportOwn)
            {
                _node.ExternalVO = string.Empty;   // undoable
                _node.HasVO = true;
                // Re-resolve inline so the destination below uses the own path.
                _voCheck = VoPathResolver.Check(
                    _node.SpeakerGuid, true, _node.ExternalVO, _node.HasFemaleText, _node.NodeId,
                    Canvas?.ConversationName ?? "", GameRoot, ActiveGameId);
            }
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

    // Button faces carry a localised variant letter (M/F) because the two play
    // buttons sit side by side and are otherwise identical (2026-07-02 spec).
    public string PlayPrimaryLabel => (_currentlyPlaying == Playing.Primary ? "■ " : "▶ ")
                                      + Loc.Get("VoPlay_MaleLetter");
    public string PlayFemLabel     => (_currentlyPlaying == Playing.Female  ? "■ " : "▶ ")
                                      + Loc.Get("VoPlay_FemaleLetter");
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
        OnPropertyChanged(nameof(PlayPrimaryLabel));
        OnPropertyChanged(nameof(PlayFemLabel));
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

    /// Token/markup validation warnings for the selected node's Default (and, when
    /// present, Female) text — likely token typos and unbalanced markup. Free-text
    /// stage directions are never flagged. Recomputed on selection change and on
    /// Default/Female edits.
    public IReadOnlyList<string> TokenWarnings
    {
        get
        {
            if (_node is null) return [];
            var messages = new List<string>();
            AppendTokenWarnings(_node.DefaultText, messages);
            if (_node.HasFemaleText)
                AppendTokenWarnings(_node.FemaleText, messages);
            return messages;
        }
    }

    private void AppendTokenWarnings(string? text, List<string> into)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var issue in _tokenValidator.Validate(text, ActiveGameId))
        {
            var msg = issue.Kind switch
            {
                TokenIssueKind.UnbalancedMarkup =>
                    Loc.Format("Validation_UnbalancedMarkup", issue.Fragment),
                _ when issue.Suggestion is not null =>
                    Loc.Format("Validation_UnknownToken_Suggest", issue.Fragment, issue.Suggestion),
                _ => Loc.Format("Validation_UnknownToken", issue.Fragment),
            };
            into.Add(msg);
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

    // ── Pane header + GUID toggles (2026-07-02 pane rework) ─────────────

    /// Bold identity line at the top of the pane: "#42 · NPC · Talk[ · Edér]".
    /// Replaces the old read-only PropertyGroups block (which held only the ID).
    public string NodeHeaderSummary
    {
        get
        {
            if (_node is null) return string.Empty;
            var sep = Loc.Get("NodeDetail_HeaderSeparator");
            var s = $"#{_node.NodeId}{sep}{SpeakerCategoryString}{sep}{NodeTypeString}";
            var speaker = SpeakerNameService.FindByGuid(_node.SpeakerGuid)?.Name;
            return speaker is null ? s : s + sep + speaker;
        }
    }

    // Raw GUID boxes double every picker field, so they hide behind a {} toggle
    // when friendly speaker data exists. Without speaker data (PoE1) they are the
    // only editing surface and stay visible unconditionally.
    private bool _showSpeakerGuidBox;
    public bool ShowSpeakerGuidBox
    {
        get => _showSpeakerGuidBox;
        set
        {
            if (SetProperty(ref _showSpeakerGuidBox, value))
                OnPropertyChanged(nameof(IsSpeakerGuidBoxVisible));
        }
    }

    private bool _showListenerGuidBox;
    public bool ShowListenerGuidBox
    {
        get => _showListenerGuidBox;
        set
        {
            if (SetProperty(ref _showListenerGuidBox, value))
                OnPropertyChanged(nameof(IsListenerGuidBoxVisible));
        }
    }

    public bool IsSpeakerGuidBoxVisible  => !HasSpeakerData || ShowSpeakerGuidBox;
    public bool IsListenerGuidBoxVisible => !HasSpeakerData || ShowListenerGuidBox;

    // ── Expander summaries (collapsed headers still answer most glances) ──

    /// e.g. "NPC · Edér → Player" — speaker/listener names only when resolvable.
    public string IdentitySummary
    {
        get
        {
            if (_node is null) return string.Empty;
            var sep      = Loc.Get("NodeDetail_HeaderSeparator");
            var speaker  = SpeakerNameService.FindByGuid(_node.SpeakerGuid)?.Name;
            var listener = SpeakerNameService.FindByGuid(_node.ListenerGuid)?.Name;
            if (speaker is null) return SpeakerCategoryString;
            var pair = listener is null ? speaker : $"{speaker} → {listener}";
            return SpeakerCategoryString + sep + pair;
        }
    }

    /// e.g. "Conversation · persists: None".
    public string DisplaySummary => _node is null
        ? string.Empty
        : $"{DisplayType}{Loc.Get("NodeDetail_HeaderSeparator")}{Loc.Get("NodeDetail_PersistsPrefix")} {Persistence}";

    /// e.g. "✓ found · M+F" / "✗ missing" / "—" (VO not applicable).
    public string VoiceSummary => _voCheck switch
    {
        null or { Status: VoPresence.NotApplicable }            => Loc.Get("NodeDetail_NoneShort"),
        { Status: VoPresence.Found, FemaleVariantFound: true }  => Loc.Get("NodeDetail_VoFoundWithFem"),
        { Status: VoPresence.Found }                            => Loc.Get("NodeDetail_VoFound"),
        _                                                       => Loc.Get("NodeDetail_VoMissing"),
    };

    /// e.g. "2 conditions · 1 script" (words are localised fragments).
    public string LogicSummary => _node is null
        ? string.Empty
        : $"{_node.Conditions.Count} {Loc.Get("NodeDetail_ConditionsWord")}"
          + Loc.Get("NodeDetail_HeaderSeparator")
          + $"{_node.Scripts.Count} {Loc.Get("NodeDetail_ScriptsWord")}";

    /// e.g. "1 note(s)" counting non-empty comment + translator note, "—" when none.
    public string NotesSummary
    {
        get
        {
            if (_node is null) return string.Empty;
            var n = (string.IsNullOrWhiteSpace(Comments) ? 0 : 1)
                  + (string.IsNullOrWhiteSpace(TranslatorNote) ? 0 : 1);
            return n == 0
                ? Loc.Get("NodeDetail_NoneShort")
                : Loc.FormatCount("NodeDetail_NotesCount", n);
        }
    }

    // ── Session-wide expander state ──────────────────────────────────────
    // Static so a VO pass keeps Voice open across node selections; resets on
    // app restart (deliberately NOT persisted to AppSettings — YAGNI per spec).
    private static bool _identityExpanded, _displayExpanded, _voiceExpanded,
                        _logicExpanded, _notesExpanded;

    public bool IsIdentityExpanded
    {
        get => _identityExpanded;
        set { _identityExpanded = value; OnPropertyChanged(); }
    }
    public bool IsDisplayExpanded
    {
        get => _displayExpanded;
        set { _displayExpanded = value; OnPropertyChanged(); }
    }
    public bool IsVoiceExpanded
    {
        get => _voiceExpanded;
        set { _voiceExpanded = value; OnPropertyChanged(); }
    }
    public bool IsLogicExpanded
    {
        get => _logicExpanded;
        set { _logicExpanded = value; OnPropertyChanged(); }
    }
    public bool IsNotesExpanded
    {
        get => _notesExpanded;
        set { _notesExpanded = value; OnPropertyChanged(); }
    }

    /// Test hook: static state leaks across serially-run tests otherwise.
    internal static void ResetExpanderStateForTests()
        => _identityExpanded = _displayExpanded = _voiceExpanded
         = _logicExpanded = _notesExpanded = false;

    // ── ExternalVO alias surface (2026-07-03 alias UX) ───────────────────
    // ExternalVO redirects this node's VO to ANOTHER line's recording — shipped
    // PoE2 data aliases 1,000 nodes this way, often across conversation files.
    // The pane therefore explains the alias instead of exposing a raw textbox;
    // edits go through the node picker so the value is always derivable.

    /// PoE2-gated: _voCheck is null on PoE1/no game root, hiding the alias UI there.
    public bool HasVoAlias =>
        _voCheck is not null && !string.IsNullOrEmpty(_node?.ExternalVO);

    public string VoAliasRawPath => _node?.ExternalVO ?? string.Empty;

    /// "Plays the recording of <conv> node <id>" — or the raw path when unparseable.
    public string VoAliasDescription
    {
        get
        {
            if (!HasVoAlias) return string.Empty;
            var t = VoAliasParse.TryParse(_node!.ExternalVO);
            return t is null
                ? Loc.Format("NodeDetail_AliasRaw", _node.ExternalVO)
                : Loc.Format("NodeDetail_AliasDescription", t.Conversation, t.NodeId);
        }
    }

    /// Set by MainWindowViewModel — current in-memory (conversation, nodeId, alias)
    /// triples from the open project; these shadow the disk index so mid-session
    /// edits are reflected before F5 writes them to the game folder.
    public Func<IReadOnlyList<VoAliasUse>>? ProjectAliasOverlay { get; set; }

    /// Other nodes sharing this alias (self excluded); null while the background
    /// index scan has not finished.
    public int? VoAliasSharedCount
    {
        get
        {
            if (!HasVoAlias || !VoAliasIndexService.IsReady || _node is null) return null;
            var alias   = _node.ExternalVO;
            var selfConv = (Canvas?.ConversationName ?? string.Empty).ToLowerInvariant();
            var overlay = ProjectAliasOverlay?.Invoke() ?? [];
            var shadowed = overlay
                .Select(u => (Conv: u.Conversation.ToLowerInvariant(), u.NodeId))
                .ToHashSet();

            var effective = VoAliasIndexService.GetReferences(alias)
                .Select(r => (Conv: r.Conversation.ToLowerInvariant(), r.NodeId))
                .Where(r => !shadowed.Contains(r))
                .Concat(overlay
                    .Where(u => string.Equals(u.AliasPath, alias, StringComparison.OrdinalIgnoreCase))
                    .Select(u => (Conv: u.Conversation.ToLowerInvariant(), u.NodeId)))
                .Distinct()
                .Count(r => !(r.Conv == selfConv && r.NodeId == _node.NodeId));
            return effective;
        }
    }

    public string VoAliasSharedText => VoAliasSharedCount switch
    {
        null => string.Empty,
        0    => Loc.Get("NodeDetail_AliasNotShared"),
        int n => Loc.FormatCount("NodeDetail_AliasSharedCount", n),
    };

    /// "Reuse another line's VO…" visibility: PoE2 node loaded, no alias yet.
    public bool CanStartVoAliasPick => _voCheck is not null && !HasVoAlias;

    /// Set by MainWindow.axaml.cs — opens the picker (current alias in, chosen
    /// alias out, null = cancelled).
    public Func<string?, Task<string?>>? ShowAliasPicker { get; set; }

    [RelayCommand]
    private async Task PickVoAlias()
    {
        if (_node is null || ShowAliasPicker is null) return;
        var result = await ShowAliasPicker(HasVoAlias ? _node.ExternalVO : null);
        if (result is not null)
            _node.ExternalVO = result;   // undoable via NodeViewModel.Push
    }

    [RelayCommand]
    private void ClearVoAlias()
    {
        if (_node is not null)
            _node.ExternalVO = string.Empty;   // undoable via NodeViewModel.Push
    }

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
        if (e.PropertyName == nameof(NodeViewModel.Conditions))
        {
            OnPropertyChanged(nameof(ConditionSummary));
            OnPropertyChanged(nameof(LogicSummary));
        }
        if (e.PropertyName == nameof(NodeViewModel.Scripts))
        {
            OnPropertyChanged(nameof(ScriptSummary));
            OnPropertyChanged(nameof(LogicSummary));
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
        OnPropertyChanged(nameof(TokenWarnings));

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
        OnPropertyChanged(nameof(NodeHeaderSummary));
        OnPropertyChanged(nameof(IdentitySummary));
        OnPropertyChanged(nameof(DisplaySummary));
        OnPropertyChanged(nameof(VoiceSummary));
        OnPropertyChanged(nameof(LogicSummary));
        OnPropertyChanged(nameof(NotesSummary));

        OnPropertyChanged(nameof(HasVoAlias));
        OnPropertyChanged(nameof(VoAliasRawPath));
        OnPropertyChanged(nameof(VoAliasDescription));
        OnPropertyChanged(nameof(VoAliasSharedCount));
        OnPropertyChanged(nameof(VoAliasSharedText));
        OnPropertyChanged(nameof(CanStartVoAliasPick));
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

/// One (conversation, nodeId) entry that currently sets ExternalVO to AliasPath —
/// used by NodeDetailViewModel.ProjectAliasOverlay to shadow the on-disk VoAliasIndexService
/// with the in-memory state of the currently-open project (mid-session edits not yet on disk).
public record VoAliasUse(string Conversation, int NodeId, string AliasPath);

/// Outcome of the aliased-import confirmation (2026-07-03 import-guard spec):
/// importing over an ExternalVO alias overwrites shared audio for every other
/// node that aliases the same file, so the user must be asked before it happens.
public enum VoAliasImportChoice { Cancel, OverwriteShared, ClearAliasImportOwn }

/// What the confirmation dialog shows: the shared target and its blast radius.
public record VoAliasImportPrompt(string TargetPath, int SharedWithOthers);
