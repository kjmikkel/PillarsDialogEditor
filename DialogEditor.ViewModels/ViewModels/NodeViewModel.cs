using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class NodeViewModel : ObservableObject
{
    // ── Read-only identity ────────────────────────────────────────────────
    public int NodeId { get; }

    // ── Undo stack (wired after construction by ConversationViewModel) ────
    internal UndoRedoStack? UndoStack { get; set; }

    // ── Editable backing fields ───────────────────────────────────────────
    private bool            _isPlayerChoice;
    private SpeakerCategory _speakerCategory;
    private string          _speakerGuid    = string.Empty;
    private string          _listenerGuid   = string.Empty;
    private string          _defaultText    = string.Empty;
    private string          _femaleText     = string.Empty;
    private string          _displayType    = string.Empty;
    private string          _persistence    = string.Empty;
    private string          _actorDirection = string.Empty;
    private string          _comments       = string.Empty;
    private string          _externalVO     = string.Empty;
    private bool            _hasVO;
    private bool            _hideSpeaker;

    // ── Editable properties ───────────────────────────────────────────────
    public bool IsPlayerChoice
    {
        get => _isPlayerChoice;
        set => Push(_isPlayerChoice, value, "Edit node type",
            v => { _isPlayerChoice = v; OnPropertyChanged(nameof(IsPlayerChoice)); OnPropertyChanged(nameof(Title)); });
    }

    public SpeakerCategory SpeakerCategory
    {
        get => _speakerCategory;
        set => Push(_speakerCategory, value, "Edit speaker category",
            v => { _speakerCategory = v; OnPropertyChanged(nameof(SpeakerCategory)); });
    }

    public string SpeakerGuid
    {
        get => _speakerGuid;
        set => Push(_speakerGuid, value, "Edit speaker GUID",
            v => { _speakerGuid = v; OnPropertyChanged(nameof(SpeakerGuid)); OnPropertyChanged(nameof(SpeakerName)); OnPropertyChanged(nameof(Title)); });
    }

    public string ListenerGuid
    {
        get => _listenerGuid;
        set => Push(_listenerGuid, value, "Edit listener GUID",
            v => { _listenerGuid = v; OnPropertyChanged(nameof(ListenerGuid)); OnPropertyChanged(nameof(ListenerName)); });
    }

    public string DefaultText
    {
        get => _defaultText;
        set => Push(_defaultText, value, "Edit dialog text",
            v => { _defaultText = v; OnPropertyChanged(nameof(DefaultText)); OnPropertyChanged(nameof(TextPreview)); });
    }

    public string FemaleText
    {
        get => _femaleText;
        set => Push(_femaleText, value, "Edit female text",
            v => { _femaleText = v; OnPropertyChanged(nameof(FemaleText)); OnPropertyChanged(nameof(HasFemaleText)); OnPropertyChanged(nameof(FooterText)); });
    }

    public string DisplayType
    {
        get => _displayType;
        set => Push(_displayType, value, "Edit display type",
            v => { _displayType = v; OnPropertyChanged(nameof(DisplayType)); });
    }

    public string Persistence
    {
        get => _persistence;
        set => Push(_persistence, value, "Edit persistence",
            v => { _persistence = v; OnPropertyChanged(nameof(Persistence)); });
    }

    public string ActorDirection
    {
        get => _actorDirection;
        set => Push(_actorDirection, value, "Edit actor direction",
            v => { _actorDirection = v; OnPropertyChanged(nameof(ActorDirection)); });
    }

    public string Comments
    {
        get => _comments;
        set => Push(_comments, value, "Edit comments",
            v => { _comments = v; OnPropertyChanged(nameof(Comments)); });
    }

    public string ExternalVO
    {
        get => _externalVO;
        set => Push(_externalVO, value, "Edit external VO",
            v => { _externalVO = v; OnPropertyChanged(nameof(ExternalVO)); });
    }

    public bool HasVO
    {
        get => _hasVO;
        set => Push(_hasVO, value, "Edit HasVO",
            v => { _hasVO = v; OnPropertyChanged(nameof(HasVO)); });
    }

    public bool HideSpeaker
    {
        get => _hideSpeaker;
        set => Push(_hideSpeaker, value, "Edit HideSpeaker",
            v => { _hideSpeaker = v; OnPropertyChanged(nameof(HideSpeaker)); });
    }

    // ── Computed display properties ───────────────────────────────────────
    public string SpeakerName =>
        SpeakerNameService.Resolve(_speakerGuid) ?? _speakerCategory switch
        {
            SpeakerCategory.Player   => Loc.Get("Speaker_Player"),
            SpeakerCategory.Narrator => Loc.Get("Speaker_Narrator"),
            SpeakerCategory.Script   => Loc.Get("Speaker_Script"),
            _                        => Loc.Get("Speaker_Unknown")
        };

    public string ListenerName =>
        SpeakerNameService.Resolve(_listenerGuid) ?? string.Empty;

    public string Title =>
        Loc.Format("Node_Title", NodeId, SpeakerName,
            _isPlayerChoice ? Loc.Get("Node_PlayerChoiceSuffix") : string.Empty);

    public string TextPreview =>
        _defaultText.Length > 80 ? _defaultText[..80] + "…" : _defaultText;

    public bool HasFemaleText => !string.IsNullOrEmpty(_femaleText);

    public string FooterText
    {
        get
        {
            var count = ConditionStrings.Count;
            var condPart = count > 0
                ? Loc.Format(count == 1 ? "Node_ConditionSingular" : "Node_ConditionPlural", count)
                : Loc.Get("Node_NoConditions");
            return HasFemaleText ? condPart + Loc.Get("Node_FemaleTextSuffix") : condPart;
        }
    }

    // ── Read-only (conditions/scripts not editable in Phase 1) ───────────
    public IReadOnlyList<string> ConditionStrings   { get; }
    public string               ConditionExpression { get; }
    public IReadOnlyList<string> Scripts            { get; }
    public IReadOnlyList<NodeLink> Links            { get; }

    // ── Nodify connector anchors ──────────────────────────────────────────
    public ConnectorViewModel Input   { get; } = new();
    public ConnectorViewModel Output  { get; } = new();
    public IReadOnlyList<ConnectorViewModel> Inputs  { get; }
    public IReadOnlyList<ConnectorViewModel> Outputs { get; }

    // ── Canvas state ──────────────────────────────────────────────────────
    [ObservableProperty] private LayoutPoint _location;
    [ObservableProperty] private bool        _isSelected;
    [ObservableProperty] private bool        _isSearchMatch = true;

    internal Action<NodeViewModel>? OnSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) OnSelected?.Invoke(this);
    }

    // ── Constructor ───────────────────────────────────────────────────────
    public NodeViewModel(ConversationNode node, StringEntry? entry)
    {
        NodeId           = node.NodeId;
        _isPlayerChoice  = node.IsPlayerChoice;
        _speakerCategory = node.SpeakerCategory;
        _speakerGuid     = node.SpeakerGuid;
        _listenerGuid    = node.ListenerGuid;
        _displayType     = node.DisplayType;
        _persistence     = node.Persistence;
        _actorDirection  = node.ActorDirection;
        _comments        = node.Comments;
        _externalVO      = node.ExternalVO;
        _hasVO           = node.HasVO;
        _hideSpeaker     = node.HideSpeaker;
        _defaultText     = entry?.DefaultText ?? Loc.Get("Node_TextUnavailable");
        _femaleText      = entry?.FemaleText  ?? string.Empty;

        ConditionStrings    = node.ConditionStrings;
        ConditionExpression = node.ConditionExpression;
        Scripts             = node.Scripts;
        Links               = node.Links;

        Inputs  = [Input];
        Outputs = [Output];
    }

    // ── Command-generating setter helper ──────────────────────────────────
    private void Push<T>(T current, T value, string description, Action<T> apply)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        if (UndoStack is null) { apply(value); return; }
        UndoStack.Execute(new SetPropertyCommand<T>(description, apply, current, value));
    }

    // ── Snapshot helper (links provided by ConversationViewModel) ─────────
    public NodeEditSnapshot ToSnapshot(IReadOnlyList<LinkEditSnapshot> links) =>
        new(NodeId, _isPlayerChoice, _speakerCategory,
            _speakerGuid, _listenerGuid,
            _defaultText, _femaleText,
            _displayType, _persistence,
            _actorDirection, _comments, _externalVO,
            _hasVO, _hideSpeaker, links);
}
