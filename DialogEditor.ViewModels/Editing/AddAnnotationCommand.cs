using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Editing;

internal sealed class AddAnnotationCommand(ConversationViewModel conversation, AnnotationViewModel annotation)
    : IEditCommand
{
    public string Description => Loc.Get("Undo_AddAnnotation");
    public void Execute() => conversation.Annotations.Add(annotation);
    public void Undo()    => conversation.Annotations.Remove(annotation);
}
