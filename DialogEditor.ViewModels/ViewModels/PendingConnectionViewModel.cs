using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DialogEditor.ViewModels;

public partial class PendingConnectionViewModel(ConversationViewModel conversation)
    : ObservableObject
{
    [ObservableProperty]
    private ConnectorViewModel? _source;

    [RelayCommand]
    private void Start(ConnectorViewModel? connector) => Source = connector;

    [RelayCommand]
    private void Complete(ConnectorViewModel? target)
    {
        if (Source is null || target is null || Source == target)
        {
            Source = null;
            return;
        }

        var alreadyExists = conversation.Connections.Any(c =>
            c.Source == Source && c.Target == target);

        if (!alreadyExists)
            conversation.AddConnection(Source, target);

        Source = null;
    }
}
