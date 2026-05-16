using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.ViewModels;

public partial class ConnectionViewModel(
    ConnectorViewModel source,
    ConnectorViewModel target,
    string questionNodeTextDisplay = "") : ObservableObject
{
    public ConnectorViewModel Source { get; } = source;
    public ConnectorViewModel Target { get; } = target;
    public string QuestionNodeTextDisplay { get; } = questionNodeTextDisplay;

    // Computed helpers for platform-agnostic conditional styling
    public bool IsAlways => QuestionNodeTextDisplay == "Always";
    public bool IsNever  => QuestionNodeTextDisplay == "Never";

    [ObservableProperty]
    private bool _isHighlighted;

    public bool HasConditions => false;
}
