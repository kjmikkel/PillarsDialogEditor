using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

public partial class ConnectionViewModel : ObservableObject
{
    /// All valid QTD values in display order; "" is the engine default. The rest are the
    /// game's own enum names, which PoE1 writes verbatim (issue 139).
    public static IReadOnlyList<string> QTDOptions { get; } =
        ["", .. NodeEnumNames.QuestionNodeTextDisplays];

    public UndoRedoStack? UndoStack { get; set; }

    public ConnectorViewModel Source { get; }
    public ConnectorViewModel Target { get; }

    private string _questionNodeTextDisplay;
    private int    _randomWeight;
    private IReadOnlyList<ConditionNode> _conditions = [];

    public string QuestionNodeTextDisplay
    {
        get => _questionNodeTextDisplay;
        set => Push(_questionNodeTextDisplay, value, "Undo_EditLinkDisplay",
            v => { _questionNodeTextDisplay = v; OnPropertyChanged(nameof(QuestionNodeTextDisplay));
                   OnPropertyChanged(nameof(IsAlways)); OnPropertyChanged(nameof(IsNever)); });
    }

    /// An int, as both games store it (issue 129).
    public int RandomWeight
    {
        get => _randomWeight;
        set => Push(_randomWeight, value, "Undo_EditLinkWeight",
            v => { _randomWeight = v; OnPropertyChanged(nameof(RandomWeight)); });
    }

    public IReadOnlyList<ConditionNode> Conditions
    {
        get => _conditions;
        set => Push(_conditions, value, "Undo_EditLinkConditions",
            v => { _conditions = v; OnPropertyChanged(nameof(Conditions));
                   OnPropertyChanged(nameof(HasConditions));
                   OnPropertyChanged(nameof(ConditionCount));
                   OnPropertyChanged(nameof(ConditionCountLabel));
                   OnPropertyChanged(nameof(ReadingConditionsToolTip));
                   RefreshVerdict(); });
    }

    // ── Verdict under the assumed game state (View > Game State) ──────────
    private DialogEditor.Core.Conditions.GameState? _assumedState;
    private string _verdictGameId = string.Empty;

    /// Points the verdict at the session's assumed state. Owned by Node Details, which also
    /// calls <see cref="RefreshVerdict"/> when the state changes; the link itself holds no
    /// subscription, so a discarded canvas link cannot leak. (The canvas does subscribe to the
    /// link's PropertyChanged, for shadowing, and unsubscribes when the link leaves Connections.)
    public void SetVerdictContext(DialogEditor.Core.Conditions.GameState? state, string gameId)
    {
        _assumedState = state;
        _verdictGameId = gameId;
        RefreshVerdict();
    }

    public void RefreshVerdict()
    {
        OnPropertyChanged(nameof(ShowConditionVerdict));
        OnPropertyChanged(nameof(ConditionVerdict));
        OnPropertyChanged(nameof(ConditionVerdictText));
        OnPropertyChanged(nameof(ConditionVerdictPasses));
        OnPropertyChanged(nameof(ConditionVerdictFails));
        OnPropertyChanged(nameof(CannotFire));
        OnPropertyChanged(nameof(CannotFireToolTip));
        OnPropertyChanged(nameof(IsFaded));
        OnPropertyChanged(nameof(FadedToolTip));
    }

    /// True when the assumed game state rules this link out: its own conditions, or those on
    /// the line it leads to, definitely fail (#32). Drives the faded canvas rendering.
    public bool CannotFire => _assumedState is not null
        && DialogEditor.Core.Conditions.LinkFiring.CannotFire(
            _conditions, Target.Owner?.Conditions ?? [], _assumedState, _verdictGameId);

    public string? CannotFireToolTip => CannotFire ? Loc.Get("ToolTip_LinkCannotFire") : null;

    private int? _shadowedByNodeId;

    /// Target node id of the earlier link that always passes and so hides this one (issue 353);
    /// null when nothing hides it. Set by the canvas, which sees all of a node's links.
    public int? ShadowedByNodeId
    {
        get => _shadowedByNodeId;
        set
        {
            if (_shadowedByNodeId == value) return;
            _shadowedByNodeId = value;
            OnPropertyChanged(nameof(ShadowedByNodeId));
            OnPropertyChanged(nameof(IsShadowed));
            OnPropertyChanged(nameof(IsFaded));
            OnPropertyChanged(nameof(FadedToolTip));
        }
    }

    public bool IsShadowed => _shadowedByNodeId is not null;

    /// Drives the faded canvas rendering: ruled out by the game state or hidden by an earlier link.
    public bool IsFaded => CannotFire || IsShadowed;

    /// The game-state reason wins when both apply.
    public string? FadedToolTip => CannotFireToolTip
        ?? (IsShadowed ? Loc.Format("ToolTip_LinkShadowed", _shadowedByNodeId) : null);

    // ── Link order on the selected node (issue 359) ───────────────────────
    private int _linkPosition = 1;
    private int _linkCount = 1;

    /// 1-based place among the source node's links, which is the order the game tries them in.
    /// Set by the canvas, which sees all of a node's links.
    public int LinkPosition => _linkPosition;

    /// How many links the source node has.
    public int LinkCount => _linkCount;

    internal void SetLinkPosition(int position, int count)
    {
        if (_linkPosition == position && _linkCount == count) return;
        _linkPosition = position;
        _linkCount = count;
        OnPropertyChanged(nameof(LinkPosition));
        OnPropertyChanged(nameof(LinkCount));
        OnPropertyChanged(nameof(LinkPositionHelpText));
        OnPropertyChanged(nameof(LinkAutomationHelpText));
        NotifySourceSelectionChanged();
    }

    /// The canvas calls this when the source node's selection flips; the link holds no
    /// subscription of its own, so a discarded link cannot leak.
    internal void NotifySourceSelectionChanged() => OnPropertyChanged(nameof(ShowLinkPosition));

    /// The number is only worth showing on the selected node, and only when there is an order.
    public bool ShowLinkPosition => _linkCount >= 2 && Source.Owner is { IsSelected: true };

    public string LinkPositionHelpText => Loc.Format("Automation_LinkPosition", _linkPosition, _linkCount);

    /// Help text for the canvas line: the position, but only when the node has an order to speak of.
    public string? LinkAutomationHelpText => _linkCount >= 2 ? LinkPositionHelpText : null;

    /// Shown only once the writer has set a fact and this link has conditions.
    public bool ShowConditionVerdict => ConditionVerdictFormat.Applies(_assumedState, _conditions);

    public DialogEditor.Core.Conditions.Truth ConditionVerdict
        => ConditionVerdictFormat.Evaluate(_assumedState, _conditions, _verdictGameId);

    public string ConditionVerdictText => ConditionVerdictFormat.Text(ConditionVerdict);

    public bool ConditionVerdictPasses => ConditionVerdict == DialogEditor.Core.Conditions.Truth.True;
    public bool ConditionVerdictFails => ConditionVerdict == DialogEditor.Core.Conditions.Truth.False;

    public bool IsAlways      => _questionNodeTextDisplay == "ShowAlways";
    public bool IsNever       => _questionNodeTextDisplay == "ShowNever";
    public bool HasConditions => _conditions.Count > 0;
    public int  ConditionCount => _conditions.Count;

    /// Button face for the link-conditions editor: localised glyph + live count.
    public string ConditionCountLabel => $"{Loc.Get("Link_ConditionGlyph")} {ConditionCount}";

    /// Tooltip for the read-only conditions indicator shown in reading mode (issue 234).
    public string ReadingConditionsToolTip => Loc.FormatCount("ToolTip_LinkConditionsReading", ConditionCount);

    [ObservableProperty] private bool _isHighlighted;

    public ConnectionViewModel(
        ConnectorViewModel source,
        ConnectorViewModel target,
        string questionNodeTextDisplay = "",
        int    randomWeight            = 1,
        IReadOnlyList<ConditionNode>? conditions = null)
    {
        Source                    = source;
        Target                    = target;
        _questionNodeTextDisplay  = questionNodeTextDisplay;
        _randomWeight             = randomWeight;
        _conditions               = conditions ?? [];
    }

    // descriptionKey, not description: the undo history is re-read on every peek, so
    // the lookup is deferred to keep it correct across a live language change.
    private void Push<T>(T current, T value, string descriptionKey, Action<T> apply)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        if (UndoStack is null) { apply(value); return; }
        UndoStack.Execute(new SetPropertyCommand<T>(
            () => Loc.Get(descriptionKey), apply, current, value));
    }
}
