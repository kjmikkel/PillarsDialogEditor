using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Editing;

internal sealed class DeleteAnnotationCommand(ConversationViewModel conversation, AnnotationViewModel annotation)
    : IEditCommand
{
    public string Description => "Delete annotation";
    public void Execute() => conversation.Annotations.Remove(annotation);
    public void Undo()    => conversation.Annotations.Add(annotation);
}
