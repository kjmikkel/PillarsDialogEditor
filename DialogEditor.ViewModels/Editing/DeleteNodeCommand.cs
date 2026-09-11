using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Editing;

internal sealed class DeleteNodeCommand(
    ConversationViewModel conversation,
    NodeViewModel node,
    IReadOnlyList<ConnectionViewModel> removedConnections) : IEditCommand
{
    public string Description => Loc.Format("Undo_DeleteNode", node.NodeId);

    public void Execute()
    {
        foreach (var c in removedConnections)
            conversation.Connections.Remove(c);
        conversation.Nodes.Remove(node);
        if (conversation.SelectedNode == node)
            conversation.SelectedNode = null;
    }

    public void Undo()
    {
        conversation.Nodes.Add(node);
        foreach (var c in removedConnections)
            conversation.Connections.Add(c);
    }
}
