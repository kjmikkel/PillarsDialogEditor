using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Editing;

internal sealed class DeleteAnnotationCommand(ConversationViewModel conversation, AnnotationViewModel annotation)
    : IEditCommand
{
    public string Description => Loc.Get("Undo_DeleteAnnotation");
    public void Execute() => conversation.Annotations.Remove(annotation);
    public void Undo()    => conversation.Annotations.Add(annotation);
}
