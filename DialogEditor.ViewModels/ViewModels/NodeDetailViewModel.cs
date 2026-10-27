using DialogEditor.Core.Analytics;
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
using DialogEditor.Core.Logging;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class NodeDetailViewModel : ObservableObject
{
    private NodeViewModel? _node;
    public  NodeViewModel? Node => _node;

    private readonly TokenValidationService _tokenValidator = new();

    /// Set by MainWindowViewModel when a game folder is opened.
    public string ActiveGameId
    {
        get => _activeGameId;
        set
        {
            if (_activeGameId == value) return;
            _activeGameId = value;
            OnPropertyChanged(nameof(SupportsLinkConditions));
            OnPropertyChanged(nameof(CanEditLinkConditions));
            OnPropertyChanged(nameof(LinkConditionsToolTip));
            OnPropertyChanged(nameof(ScriptWarnings));
        }
    }
    private string _activeGameId = string.Empty;

    /// Only PoE2's FlowChartLink has Conditionals (issue 141). PoE1's game model has none, so
    /// a condition put on a PoE1 link would look saved here yet never fire in game; the shipped
    /// PoE1 data gates a choice by putting the condition on the target node instead.
    public bool SupportsLinkConditions =>
        string.Equals(_activeGameId, "poe2", StringComparison.OrdinalIgnoreCase);

    /// The link card's conditions button: the game must support them and the pane must be editable.
    public bool CanEditLinkConditions => SupportsLinkConditions && IsEditable;

    /// The link-conditions button's tooltip. For PoE1 it says where the condition goes.
    public string LinkConditionsToolTip =>
        Loc.Get(SupportsLinkConditions ? "ToolTip_LinkConditions_None" : "ToolTip_LinkConditions_Poe1");

    /// The provider's text language ("en" etc.) — the language Default/Female text
    /// is written in. Set by MainWindowViewModel alongside ActiveGameId; drives
    /// which spelling dictionary checks this node's text.
    public string ActiveLanguage
    {
        get => _activeLanguage;
        set { _activeLanguage = value; OnPropertyChanged(nameof(SpellSpanFinder)); OnPropertyChanged(nameof(SpellingMenuFinder)); }
    }
    private string _activeLanguage = string.Empty;

    /// Spell checker over the three-layer dictionary store. Wired at app startup
    /// (MainWindow) from SpellDictionaryStore.Default; null in unit tests, which
    /// silently disables spelling warnings.
    public SpellCheckService? SpellChecker
    {
        get => _spellChecker;
        set { _spellChecker = value; OnPropertyChanged(nameof(SpellSpanFinder)); OnPropertyChanged(nameof(SpellingMenuFinder)); }
    }
    private SpellCheckService? _spellChecker;

    /// Finds the misspelt spans in a given text, for the inline squiggles in the Default/Female
    /// text boxes (issue 9). Null without a checker. A NEW delegate each time the dictionary or
    /// language changes, so the binding tells the view to re-check. The squiggles are visual
    /// only; SpellingIssues stays the accessible channel.
    public Func<string, IReadOnlyList<SpellSpan>>? SpellSpanFinder
    {
        get
        {
            var checker = SpellChecker;
            if (checker is null) return null;
            var lang = ActiveLanguage;
            return text => checker.FindMisspellings(text ?? "", lang);
        }
    }

    /// What to offer when the writer right-clicks at a text position (issue 313): the misspelt
    /// word there (caret touching either end counts), its suggestions and the add-to-dictionary
    /// actions. Null without a checker; the delegate returns null off a misspelt word. A NEW
    /// delegate whenever SpellSpanFinder is, so a binding picks up the current dictionary/language.
    public Func<string, int, SpellingMenu?>? SpellingMenuFinder
    {
        get
        {
            var checker = SpellChecker;
            if (checker is null) return null;
            var lang = ActiveLanguage;
            return (text, index) =>
            {
                text ??= "";
                foreach (var span in checker.FindMisspellings(text, lang))
                {
                    if (index < span.Start || index > span.Start + span.Length) continue;
                    var word = text.Substring(span.Start, span.Length);
                    var add = AddWordToDictionary;
                    var addProject = AddWordToProjectDictionary;
                    return new SpellingMenu(word, span.Start, span.Length,
                        checker.Suggestions(word, lang, SpellingMenu.MaxSuggestions),
                        add is null ? null : () => { add(word); RefreshSpelling(); },
                        addProject is null ? null : () => { addProject(word); RefreshSpelling(); });
                }
                return null;
            };
        }
    }

    private void RefreshSpelling()
    {
        OnPropertyChanged(nameof(SpellingIssues));
        OnPropertyChanged(nameof(SpellSpanFinder));
        OnPropertyChanged(nameof(SpellingMenuFinder));
    }

    /// Set by MainWindowViewModel when a game folder is opened.
    public string GameRoot { get; set; } = string.Empty;

    /// Set by MainWindowViewModel when a conversation is loaded.
    public ConversationViewModel? Canvas
    {
        get => _canvas;
        set
        {
            if (ReferenceEquals(_canvas, value)) return;
            if (_canvas is not null) _canvas.PropertyChanged -= OnCanvasPropertyChanged;
            _canvas = value;
            if (_canvas is not null) _canvas.PropertyChanged += OnCanvasPropertyChanged;
            OnEditabilityChanged();
        }
    }
    private ConversationViewModel? _canvas;

    // ── Editability (issue 240) ───────────────────────────────────────────
    /// The canvas owns editability: MainWindowViewModel.SetProject sets it, and with only a game
    /// folder open ("Viewing only") it is false, so edits here would dirty a conversation there
    /// is no project to save to. Without a canvas nothing restricts editing.
    public bool IsEditable => _canvas?.IsEditable ?? true;
    public bool IsReadOnly => !IsEditable;

    /// True in Reading mode (issue 234); set by MainWindowViewModel.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowImportVoMenuItem))]
    [NotifyPropertyChangedFor(nameof(ShowNoProjectHint))]
    [NotifyPropertyChangedFor(nameof(FemaleTextPlaceholder))]
    private bool _isReadingMode;

    /// The "open or create a project" line under the header: only in Editing mode, since in
    /// Reading mode File > New Project is hidden and the mode badge already says why nothing
    /// takes edits.
    public bool ShowNoProjectHint => IsReadOnly && !IsReadingMode;

    /// Watermark of the female-text box; the editing placeholder advises leaving it blank, which
    /// is meaningless where nothing can be typed.
    public string FemaleTextPlaceholder =>
        Loc.Get(IsReadingMode ? "Placeholder_FemaleText_Reading" : "Placeholder_FemaleText");

    /// The node context menu's "Import voice-over" item (and its separator): a node with VO status, but
    /// never in reading mode, where it would write to the project. A property here because a menu item
    /// cannot combine two bindings (a MultiBinding in the popup never took effect).
    public bool ShowImportVoMenuItem => HasVoStatus && !IsReadingMode;

    private void OnCanvasPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConversationViewModel.IsEditable))
            OnEditabilityChanged();
    }

    private void OnEditabilityChanged()
    {
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(ShowNoProjectHint));
        OnPropertyChanged(nameof(CanEditLinkConditions));
        AddLinkCommand.NotifyCanExecuteChanged();
        DeleteLinkCommand.NotifyCanExecuteChanged();
        PickVoAliasCommand.NotifyCanExecuteChanged();
        ClearVoAliasCommand.NotifyCanExecuteChanged();
    }

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
        set { if (_node != null) { _node.DefaultText = value; OnPropertyChanged(nameof(TokenWarnings)); OnPropertyChanged(nameof(SpellingIssues)); } }
    }

    public string FemaleText
    {
        get => _node?.FemaleText ?? string.Empty;
        set { if (_node != null) { _node.FemaleText = value; OnPropertyChanged(nameof(TokenWarnings)); OnPropertyChanged(nameof(SpellingIssues)); } }
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

    // A value outside the dropdown's fixed items (e.g. "Unknown(7)") leaves the ComboBox
    // with nothing selected, and it writes null back through its TwoWay binding. Ignoring
    // null keeps that value intact (issue 132); the user can only ever pick a real name.
    public string DisplayType
    {
        get => _node?.DisplayType ?? string.Empty;
        set { if (_node != null && value is not null) _node.DisplayType = value; }
    }

    public string Persistence
    {
        get => _node?.Persistence ?? string.Empty;
        set { if (_node != null && value is not null) _node.Persistence = value; }
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

        var voRoot = VoPathResolver.VoicesRoot(GameRoot);
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
            ReportStatus?.Invoke(Loc.Format("Status_VoImportFailed", result.ErrorMessage ?? Loc.Get("Status_UnknownError")));
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

    /// Scripts on a Deadfire script node that the game never runs (issue 167). The rule
    /// is shared with flow analysis (ScriptNodeRules); recomputed on selection, link,
    /// script and game changes.
    public IReadOnlyList<string> ScriptWarnings =>
        _node is null ? []
        : ScriptNodeRules.NeverRun(_activeGameId, _node.SpeakerCategory, _node.ScriptKind,
                                   Links.Count > 0, _node.Scripts)
            .Select(kind => Loc.Get(kind == FlowIssueKind.ScriptExitNeverRuns
                ? "NodeDetail_ScriptWarning_ExitNeverRuns"
                : "NodeDetail_ScriptWarning_UpdateNeverRuns"))
            .ToList();

    /// Text validation warnings for the selected node's Default (and, when present,
    /// Female) text — likely token typos and unbalanced markup. Free-text stage
    /// directions are never flagged. Spelling has its own rows (SpellingIssues), so each
    /// can carry its quick fixes (issue 9). Recomputed on selection change and on
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

    /// Possible misspellings in the Default (and, when present, Female) text, when a spell
    /// checker is wired and a dictionary is installed for the active language. Each row
    /// offers the suggested word and "Add to dictionary" (issue 9), so a writer fixes or
    /// accepts a word without leaving the detail panel.
    public IReadOnlyList<SpellingIssueViewModel> SpellingIssues
    {
        get
        {
            if (_node is null || SpellChecker is null) return [];
            var rows = new List<SpellingIssueViewModel>();
            AppendSpellingIssues(_node.DefaultText, female: false, rows);
            if (_node.HasFemaleText)
                AppendSpellingIssues(_node.FemaleText, female: true, rows);
            return rows;
        }
    }

    /// Adds a word to the user dictionary. Wired at app startup (MainWindow) to the same
    /// store the Validate Text window uses; null in unit tests, which hides the button.
    public Action<string>? AddWordToDictionary { get; set; }

    private Action<string>? _addWordToProjectDictionary;

    /// Adds a word to the open project's spelling list (issue 9). Null when no project is open,
    /// which hides the button; changing it refreshes the spelling rows so the button follows.
    public Action<string>? AddWordToProjectDictionary
    {
        get => _addWordToProjectDictionary;
        set
        {
            if (ReferenceEquals(_addWordToProjectDictionary, value)) return;
            _addWordToProjectDictionary = value;
            OnPropertyChanged(nameof(SpellingIssues));
            OnPropertyChanged(nameof(SpellingMenuFinder));
        }
    }

    private void AppendSpellingIssues(string? text, bool female, List<SpellingIssueViewModel> into)
    {
        if (SpellChecker is null || string.IsNullOrEmpty(text)) return;
        foreach (var issue in SpellChecker.Check(text, ActiveLanguage))
        {
            var message = issue.Suggestion is not null
                ? Loc.Format("Spelling_Misspelled_Suggest", issue.Word, issue.Suggestion)
                : Loc.Format("Spelling_Misspelled", issue.Word);
            into.Add(new SpellingIssueViewModel(issue, message,
                apply: () => ApplySpellingSuggestion(issue, female),
                addToDictionary: AddWordToDictionary is null ? null : () =>
                {
                    AddWordToDictionary(issue.Word);
                    RefreshSpelling();
                },
                addToProjectDictionary: AddWordToProjectDictionary is null ? null : () =>
                {
                    AddWordToProjectDictionary(issue.Word);
                    RefreshSpelling();
                }));
        }
    }

    /// Replaces every whole-word occurrence of the misspelling in that text, through the normal
    /// undoable text edit. The checker reports each word once, so one click fixes them all;
    /// longer words that merely contain it ("captians") are left alone.
    private void ApplySpellingSuggestion(SpellIssue issue, bool female)
    {
        if (_node is null || issue.Suggestion is null) return;
        var text = female ? _node.FemaleText : _node.DefaultText;
        var wholeWord = new System.Text.RegularExpressions.Regex(
            $@"(?<![\w'’-]){System.Text.RegularExpressions.Regex.Escape(issue.Word)}(?![\w'’-])");
        var fixedText = wholeWord.Replace(text, issue.Suggestion.Replace("$", "$$"));
        if (fixedText == text) return;   // the text changed since the check; the next recompute shows it
        if (female) { FemaleText = fixedText; OnPropertyChanged(nameof(FemaleText)); }
        else        { DefaultText = fixedText; OnPropertyChanged(nameof(DefaultText)); }
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
            _node.SetCategory(category);
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
        set
        {
            if (_node is null) return;
            // The game type follows the category (issue 160). "NPC line" is what the dropdown
            // already shows for every non-player node, so it changes only a player choice:
            // narrator, script, bank and trigger nodes keep their category and type (issue 113).
            if (value == Loc.Get("Option_PlayerChoice"))   _node.SetCategory(SpeakerCategory.Player);
            else if (_node.SpeakerCategory == SpeakerCategory.Player) _node.SetCategory(SpeakerCategory.Npc);
        }
    }

    public static IReadOnlyList<string> NodeTypeOptions
        => [Loc.Get("Option_NpcLine"), Loc.Get("Option_PlayerChoice")];

    /// The stored names, never localised labels — the view localises each item (issue 132).
    /// Fixed, never per node: replacing a ComboBox's items under a selection makes it
    /// re-select the previous node's value and write it into the new node, and leaves the
    /// SelectedItem binding stuck on it.
    public static IReadOnlyList<string> DisplayTypeOptions => NodeEnumNames.DisplayTypes;

    public static IReadOnlyList<string> PersistenceOptions => NodeEnumNames.Persistences;

    /// The node's value when it is not one of the options ("Unknown(7)", or "" when the
    /// property is absent), else null. The dropdown shows it as placeholder text, so such a
    /// value is visible without being selectable — and so without being overwritten.
    public string? DisplayTypeUnlisted => Unlisted(DisplayTypeOptions, DisplayType);

    public string? PersistenceUnlisted => Unlisted(PersistenceOptions, Persistence);

    private string? Unlisted(IReadOnlyList<string> options, string value) =>
        _node is null || options.Contains(value) ? null : value;

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

    // An unknown speaker leaves the picker blank, so its full GUID shows without the toggle (GitHub issue 257).
    public bool IsSpeakerGuidBoxVisible  => !HasSpeakerData || ShowSpeakerGuidBox || IsUnknownGuid(_node?.SpeakerGuid);
    public bool IsListenerGuidBoxVisible => !HasSpeakerData || ShowListenerGuidBox || IsUnknownGuid(_node?.ListenerGuid);

    private static bool IsUnknownGuid(string? guid) =>
        !string.IsNullOrWhiteSpace(guid) && !SpeakerNameService.IsKnown(guid);

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

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private async Task PickVoAlias()
    {
        if (_node is null || ShowAliasPicker is null) return;
        var result = await ShowAliasPicker(HasVoAlias ? _node.ExternalVO : null);
        if (result is not null)
            _node.ExternalVO = result;   // undoable via NodeViewModel.Push
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
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

    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void AddLink()
    {
        if (_node is null || !int.TryParse(AddLinkTargetId.Trim(), out var targetId)) return;
        AddLinkRequested?.Invoke(_node.NodeId, targetId);
        AddLinkTargetId = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(IsEditable))]
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
        OnPropertyChanged(nameof(DisplayTypeUnlisted));
        OnPropertyChanged(nameof(PersistenceUnlisted));
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
        OnPropertyChanged(nameof(IsSpeakerGuidBoxVisible));
        OnPropertyChanged(nameof(IsListenerGuidBoxVisible));
        OnPropertyChanged(nameof(BarkWarnings));
        OnPropertyChanged(nameof(TokenWarnings));
        RefreshSpelling();
        OnPropertyChanged(nameof(ScriptWarnings));

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
        OnPropertyChanged(nameof(ShowImportVoMenuItem));
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
        OnPropertyChanged(nameof(ScriptWarnings));
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
