using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.ViewModels;

public partial class ConnectionViewModel : ObservableObject
{
    /// All valid QTD values in display order; "" is the engine default.
    public static IReadOnlyList<string> QTDOptions { get; } =
        ["", "ShowOnce", "Always", "Never"];

    public UndoRedoStack? UndoStack { get; set; }

    public ConnectorViewModel Source { get; }
    public ConnectorViewModel Target { get; }

    private string _questionNodeTextDisplay;
    private float  _randomWeight;
    private IReadOnlyList<ConditionNode> _conditions = [];

    public string QuestionNodeTextDisplay
    {
        get => _questionNodeTextDisplay;
        set => Push(_questionNodeTextDisplay, value, "Edit link display",
            v => { _questionNodeTextDisplay = v; OnPropertyChanged(nameof(QuestionNodeTextDisplay));
                   OnPropertyChanged(nameof(IsAlways)); OnPropertyChanged(nameof(IsNever)); });
    }

    public float RandomWeight
    {
        get => _randomWeight;
        set => Push(_randomWeight, value, "Edit link weight",
            v => { _randomWeight = v; OnPropertyChanged(nameof(RandomWeight)); });
    }

    public IReadOnlyList<ConditionNode> Conditions
    {
        get => _conditions;
        set => Push(_conditions, value, "Edit link conditions",
            v => { _conditions = v; OnPropertyChanged(nameof(Conditions));
                   OnPropertyChanged(nameof(HasConditions));
                   OnPropertyChanged(nameof(ConditionCount)); });
    }

    public bool IsAlways      => _questionNodeTextDisplay == "Always";
    public bool IsNever       => _questionNodeTextDisplay == "Never";
    public bool HasConditions => _conditions.Count > 0;
    public int  ConditionCount => _conditions.Count;

    [ObservableProperty] private bool _isHighlighted;

    public ConnectionViewModel(
        ConnectorViewModel source,
        ConnectorViewModel target,
        string questionNodeTextDisplay = "",
        float  randomWeight            = 1f,
        IReadOnlyList<ConditionNode>? conditions = null)
    {
        Source                    = source;
        Target                    = target;
        _questionNodeTextDisplay  = questionNodeTextDisplay;
        _randomWeight             = randomWeight;
        _conditions               = conditions ?? [];
    }

    private void Push<T>(T current, T value, string description, Action<T> apply)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        if (UndoStack is null) { apply(value); return; }
        UndoStack.Execute(new SetPropertyCommand<T>(description, apply, current, value));
    }
}
