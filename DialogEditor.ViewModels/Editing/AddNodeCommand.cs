using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Editing;

internal sealed class AddNodeCommand(ConversationViewModel conversation, NodeViewModel node)
    : IEditCommand
{
    public string Description => $"Add node {node.NodeId}";
    public void Execute() => conversation.Nodes.Add(node);
    public void Undo()    => conversation.Nodes.Remove(node);
}
