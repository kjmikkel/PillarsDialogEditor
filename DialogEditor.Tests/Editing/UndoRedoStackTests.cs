using DialogEditor.Core.Editing;

namespace DialogEditor.Tests.Editing;

public class UndoRedoStackTests
{
    private static IEditCommand MakeCommand(List<string> log, string name) =>
        new LambdaCommand(name,
            execute: () => log.Add($"do:{name}"),
            undo:    () => log.Add($"undo:{name}"));

    [Fact]
    public void Execute_RunsCommandAndAddsToHistory()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand(log, "A"));
        Assert.Equal(["do:A"], log);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Undo_RevertsLastCommand()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand(log, "A"));
        stack.Undo();
        Assert.Equal(["do:A", "undo:A"], log);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void Redo_ReappliesUndoneCommand()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand(log, "A"));
        stack.Undo();
        stack.Redo();
        Assert.Equal(["do:A", "undo:A", "do:A"], log);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Execute_AfterUndo_ClearsRedoStack()
    {
        var log = new List<string>();
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand(log, "A"));
        stack.Undo();
        stack.Execute(MakeCommand(log, "B"));
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoDescription_ReturnsTopCommandDescription()
    {
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand([], "Alpha"));
        Assert.Equal("Alpha", stack.UndoDescription);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var stack = new UndoRedoStack();
        stack.Execute(MakeCommand([], "A"));
        stack.Clear();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Null(stack.UndoDescription);
        Assert.Null(stack.RedoDescription);
    }

    [Fact]
    public void Undo_WhenEmpty_DoesNotThrow()
    {
        var stack = new UndoRedoStack();
        var ex = Record.Exception(() => stack.Undo());
        Assert.Null(ex);
    }

    [Fact]
    public void SetPropertyCommand_UndoRestoresOldValue()
    {
        string current = "original";

        var cmd = new SetPropertyCommand<string>(
            description: "Edit text",
            apply: v => current = v,
            oldValue: "original",
            newValue: "updated");

        var stack = new UndoRedoStack();
        stack.Execute(cmd);
        Assert.Equal("updated", current);

        stack.Undo();
        Assert.Equal("original", current);

        stack.Redo();
        Assert.Equal("updated", current);
    }
}

internal sealed class LambdaCommand(string description, Action execute, Action undo)
    : IEditCommand
{
    public string Description => description;
    public void Execute() => execute();
    public void Undo() => undo();
}
