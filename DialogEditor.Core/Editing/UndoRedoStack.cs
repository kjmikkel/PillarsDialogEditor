namespace DialogEditor.Core.Editing;

public sealed class UndoRedoStack
{
    private readonly Stack<IEditCommand> _undoStack = new();
    private readonly Stack<IEditCommand> _redoStack = new();

    /// Raised after Execute() runs a new command. Lets owners (e.g. the canvas)
    /// flag dirty state for edits that go through the stack without touching it
    /// directly — such as node property setters pushed from the detail pane.
    public event Action? CommandExecuted;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? UndoDescription => CanUndo ? _undoStack.Peek().Description : null;
    public string? RedoDescription => CanRedo ? _redoStack.Peek().Description : null;

    public void Execute(IEditCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
        CommandExecuted?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
