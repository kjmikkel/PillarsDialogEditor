namespace DialogEditor.Core.Editing;

/// <summary>
/// An undoable single-property change.
///
/// The description arrives as a factory, not a string, because UndoRedoStack reads it
/// when the stack is peeked rather than when the command is pushed — and the editor can
/// change UI language while a conversation is open. Capturing the resolved text here
/// would freeze each history entry in the language its edit was made in. Core has no
/// localisation dependency, so the caller supplies the lookup.
/// </summary>
public sealed class SetPropertyCommand<T>(
    Func<string> describe,
    Action<T> apply,
    T oldValue,
    T newValue) : IEditCommand
{
    public string Description => describe();
    public void Execute() => apply(newValue);
    public void Undo()    => apply(oldValue);
}
