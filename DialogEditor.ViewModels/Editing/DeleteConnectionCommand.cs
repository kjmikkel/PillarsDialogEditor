using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Editing;

internal sealed class DeleteConnectionCommand(
    ConversationViewModel conversation,
    ConnectionViewModel connection) : IEditCommand
{
    public string Description =>
        $"Delete connection → {connection.Target.GetNodeId()}";

    public void Execute() => conversation.Connections.Remove(connection);
    public void Undo()    => conversation.Connections.Add(connection);
}
