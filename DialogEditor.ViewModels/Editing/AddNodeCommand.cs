using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Editing;

internal sealed class AddNodeCommand(ConversationViewModel conversation, NodeViewModel node)
    : IEditCommand
{
    public string Description => Loc.Format("Undo_AddNode", node.NodeId);
    public void Execute() => conversation.Nodes.Add(node);
    public void Undo()    => conversation.Nodes.Remove(node);
}
