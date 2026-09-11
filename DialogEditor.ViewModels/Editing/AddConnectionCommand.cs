using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Editing;

internal sealed class AddConnectionCommand(
    ConversationViewModel conversation,
    ConnectionViewModel connection) : IEditCommand
{
    public string Description => Loc.Format("Undo_AddConnection",
        connection.Source.GetNodeId(), connection.Target.GetNodeId());

    public void Execute() => conversation.Connections.Add(connection);
    public void Undo()    => conversation.Connections.Remove(connection);
}
