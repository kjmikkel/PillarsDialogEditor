using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Editing;

internal sealed class DeleteConnectionCommand(
    ConversationViewModel conversation,
    ConnectionViewModel connection) : IEditCommand
{
    public string Description =>
        Loc.Format("Undo_DeleteConnection", connection.Target.GetNodeId());

    // Link order is the order in Connections and decides which link the game takes (issue 353),
    // so undo puts the link back where it was rather than at the end.
    private int _index = -1;

    public void Execute()
    {
        _index = conversation.Connections.IndexOf(connection);
        conversation.Connections.Remove(connection);
    }

    public void Undo()
    {
        if (_index < 0 || _index > conversation.Connections.Count) conversation.Connections.Add(connection);
        else conversation.Connections.Insert(_index, connection);
    }
}
