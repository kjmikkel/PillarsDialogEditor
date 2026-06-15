namespace DialogEditor.ViewModels;

/// <summary>
/// Raised by <see cref="ConversationViewModel.ConnectModeChanged"/> when keyboard
/// connect mode starts, completes with a new connection, or is cancelled.
/// <paramref name="Target"/> is non-null only for <see cref="ConnectModeChange.Connected"/>.
/// </summary>
public sealed record ConnectModeEventArgs(
    ConnectModeChange Change,
    NodeViewModel Source,
    NodeViewModel? Target);
