using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Editing;

internal sealed class DeleteNodeCommand(
    ConversationViewModel conversation,
    NodeViewModel node,
    IReadOnlyList<ConnectionViewModel> removedConnections) : IEditCommand
{
    private IReadOnlyList<int> _indices = [];

    public string Description => Loc.Format("Undo_DeleteNode", node.NodeId);

    public void Execute()
    {
        // Remember each link's slot: link order decides which link fires (issue 353).
        _indices = removedConnections.Select(c => conversation.Connections.IndexOf(c)).ToList();
        foreach (var c in removedConnections)
            conversation.Connections.Remove(c);
        conversation.Nodes.Remove(node);
        if (conversation.SelectedNode == node)
            conversation.SelectedNode = null;
    }

    public void Undo()
    {
        conversation.Nodes.Add(node);
        // Ascending order, so each insert lands at its original slot once earlier ones are back.
        foreach (var (c, i) in removedConnections.Zip(_indices).OrderBy(p => p.Second))
            if (i < 0 || i > conversation.Connections.Count) conversation.Connections.Add(c);
            else conversation.Connections.Insert(i, c);
    }
}
