using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.WPF.ViewModels;

public partial class ConnectionViewModel(ConnectorViewModel source, ConnectorViewModel target)
    : ObservableObject
{
    public ConnectorViewModel Source { get; } = source;
    public ConnectorViewModel Target { get; } = target;

    [ObservableProperty]
    private bool _isHighlighted;
}
