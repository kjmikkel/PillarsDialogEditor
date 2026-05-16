namespace DialogEditor.Core.Editing;

public interface IEditCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}
