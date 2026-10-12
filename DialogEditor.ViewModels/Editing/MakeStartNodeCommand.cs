using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Logging;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels.Editing;

/// Renumbers a node to the start node, node 0 (issue 172). Links and layout follow the node
/// object; the comment, the staged VO files and the translation remap are moved here.
internal sealed class MakeStartNodeCommand(
    ConversationViewModel conversation, NodeViewModel node, int oldId, string? projectPath)
    : IEditCommand
{
    private bool    _ranOnce;
    private string? _displacedComment;

    public string Description => Loc.Format("Undo_MakeStartNode", oldId);

    public void Execute()
    {
        // The first run lets a VO failure escape, so UndoRedoStack never pushes the command
        // and MakeStartNode reports it. A Redo must not throw: the stack has already popped
        // the command, so the failure is logged and the model change goes ahead.
        var vo = VoFiles(oldId, StartNode.Id);
        if (!_ranOnce) vo?.Forward();
        else           TryMove(vo, v => v.Forward());
        _ranOnce = true;

        // A comment still stored under 0 belongs to a deleted node 0 (DeleteNodeCommand keeps
        // it for its own Undo); it must not attach to the new start node, and comes back on Undo.
        _displacedComment = conversation.TakeNodeComment(StartNode.Id);
        conversation.ApplyRenumber(node, oldId, StartNode.Id);
    }

    public void Undo()
    {
        TryMove(VoFiles(StartNode.Id, oldId), v => v.Forward());
        conversation.ApplyRenumber(node, StartNode.Id, oldId);
        if (_displacedComment is not null)
            conversation.RestoreNodeComment(StartNode.Id, _displacedComment);
    }

    // Worked out on every run from what is on disk now: VO imported between the edit and its
    // Undo or Redo is staged under the node's current ID.
    private StartNodeVoFiles? VoFiles(int fromId, int toId) =>
        StartNodeVoFiles.For(projectPath, node, conversation.ConversationName, fromId, toId);

    // StartNodeVoFiles logs a failed move itself; this records that the edit went on anyway.
    private static void TryMove(StartNodeVoFiles? vo, Action<StartNodeVoFiles> move)
    {
        if (vo is null) return;
        if (vo.ExistingTarget is { } taken)
        {
            AppLog.Warn($"Start node: voice-over not renamed, {taken} already exists");
            return;
        }
        try { move(vo); }
        catch (Exception ex) { AppLog.Warn($"Start node: voice-over left under its other name: {ex.Message}"); }
    }
}
