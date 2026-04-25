using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.WPF.ViewModels;

public partial class ConnectionViewModel(
    ConnectorViewModel source,
    ConnectorViewModel target,
    string questionNodeTextDisplay = "") : ObservableObject
{
    public ConnectorViewModel Source { get; } = source;
    public ConnectorViewModel Target { get; } = target;
    public string QuestionNodeTextDisplay { get; } = questionNodeTextDisplay;

    [ObservableProperty]
    private bool _isHighlighted;
}
