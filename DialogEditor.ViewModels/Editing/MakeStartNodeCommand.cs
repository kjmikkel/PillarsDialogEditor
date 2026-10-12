using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Logging;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels.Editing;

/// Renumbers a node to the start node, node 0 (issue 172). Links and layout follow the node
/// object; the comment, the staged VO files and the translation remap are moved here.
internal sealed class MakeStartNodeCommand(
    ConversationViewModel conversation, NodeViewModel node, int oldId, StartNodeVoFiles? vo)
    : IEditCommand
{
    private bool _ranOnce;

    public string Description => Loc.Format("Undo_MakeStartNode", oldId);

    public void Execute()
    {
        // The first run lets a VO failure escape, so UndoRedoStack never pushes the command
        // and MakeStartNode reports it. A Redo must not throw: the stack has already popped
        // the command, so the failure is logged and the model change goes ahead.
        if (!_ranOnce) vo?.Forward();
        else           TryMove(() => vo?.Forward());
        _ranOnce = true;
        conversation.ApplyRenumber(node, oldId, StartNode.Id);
    }

    public void Undo()
    {
        TryMove(() => vo?.Back());
        conversation.ApplyRenumber(node, StartNode.Id, oldId);
    }

    // StartNodeVoFiles has already logged the error; this records that the edit went on.
    private static void TryMove(Action move)
    {
        try { move(); }
        catch (Exception ex) { AppLog.Warn($"Start node: voice-over left under its other name: {ex.Message}"); }
    }
}
