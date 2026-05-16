using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Editing;

internal sealed class AddConnectionCommand(
    ConversationViewModel conversation,
    ConnectionViewModel connection) : IEditCommand
{
    public string Description =>
        $"Add connection {connection.Source.GetNodeId()} → {connection.Target.GetNodeId()}";

    public void Execute() => conversation.Connections.Add(connection);
    public void Undo()    => conversation.Connections.Remove(connection);
}
