namespace DialogEditor.Core.Editing;

public sealed class SetPropertyCommand<T>(
    string description,
    Action<T> apply,
    T oldValue,
    T newValue) : IEditCommand
{
    public string Description => description;
    public void Execute() => apply(newValue);
    public void Undo()    => apply(oldValue);
}
