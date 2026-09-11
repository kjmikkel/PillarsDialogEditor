using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Editing;

internal sealed class DeleteConnectionCommand(
    ConversationViewModel conversation,
    ConnectionViewModel connection) : IEditCommand
{
    public string Description =>
        Loc.Format("Undo_DeleteConnection", connection.Target.GetNodeId());

    public void Execute() => conversation.Connections.Remove(connection);
    public void Undo()    => conversation.Connections.Add(connection);
}
