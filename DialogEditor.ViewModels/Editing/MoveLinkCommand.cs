using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Editing;

/// Swaps two links of one node in <see cref="ConversationViewModel.Connections"/>, which is the node's link
/// order and so decides which link the game takes (issue 359). Undo swaps them back.
internal sealed class MoveLinkCommand(
    ConversationViewModel conversation,
    ConnectionViewModel connection,
    ConnectionViewModel sibling) : IEditCommand
{
    public string Description =>
        Loc.Format("Undo_MoveLink", connection.Target.GetNodeId());

    public void Execute() => Swap();

    public void Undo() => Swap();

    // The two connections trade places, so doing it again restores the original order exactly.
    private void Swap()
    {
        var a = conversation.Connections.IndexOf(connection);
        var b = conversation.Connections.IndexOf(sibling);
        if (a < 0 || b < 0) return;
        var (low, high) = a < b ? (a, b) : (b, a);
        conversation.Connections.Move(high, low);
        conversation.Connections.Move(low + 1, high);
        if (connection.Source.Owner is { } source) conversation.RefreshShadowing(source);
    }
}
