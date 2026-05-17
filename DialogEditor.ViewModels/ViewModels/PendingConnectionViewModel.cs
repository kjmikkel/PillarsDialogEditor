using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DialogEditor.ViewModels;

public partial class PendingConnectionViewModel : ObservableObject
{
    private readonly ConversationViewModel _conversation;

    public PendingConnectionViewModel(ConversationViewModel conversation)
    {
        _conversation = conversation;
        conversation.PropertyChanged += OnConversationPropertyChanged;
    }

    private void OnConversationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConversationViewModel.IsEditable))
            CompleteCommand.NotifyCanExecuteChanged();
    }
    [ObservableProperty]
    private ConnectorViewModel? _source;

    [RelayCommand]
    private void Start(ConnectorViewModel? connector) => Source = connector;

    [RelayCommand(CanExecute = nameof(CanComplete))]
    private void Complete(ConnectorViewModel? target)
    {
        if (Source is null || target is null || Source == target)
        {
            Source = null;
            return;
        }

        var alreadyExists = _conversation.Connections.Any(c =>
            c.Source == Source && c.Target == target);

        if (!alreadyExists)
            _conversation.AddConnection(Source, target);

        Source = null;
    }

    private bool CanComplete(ConnectorViewModel? target) => _conversation.IsEditable;
}
