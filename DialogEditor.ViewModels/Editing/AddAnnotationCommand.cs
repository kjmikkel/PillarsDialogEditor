using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Editing;

internal sealed class AddAnnotationCommand(ConversationViewModel conversation, AnnotationViewModel annotation)
    : IEditCommand
{
    public string Description => "Add annotation";
    public void Execute() => conversation.Annotations.Add(annotation);
    public void Undo()    => conversation.Annotations.Remove(annotation);
}
