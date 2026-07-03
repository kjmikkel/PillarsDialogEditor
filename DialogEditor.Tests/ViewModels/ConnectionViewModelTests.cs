using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class ConnectionViewModelTests
{
    public ConnectionViewModelTests() => Loc.Configure(new StubStringProvider());

    private static ConnectionViewModel MakeConn(
        string qtd = "",
        float  weight = 1f,
        IReadOnlyList<ConditionNode>? conditions = null)
        => new(new ConnectorViewModel(), new ConnectorViewModel(), qtd, weight, conditions);

    // ── Push<T> — same value: no command pushed ───────────────────────────

    [Fact]
    public void SetProperty_SameValue_DoesNotPushToUndoStack()
    {
        var conn  = MakeConn(qtd: "ShowOnce");
        var stack = new UndoRedoStack();
        conn.UndoStack = stack;
        conn.QuestionNodeTextDisplay = "ShowOnce";
        Assert.False(stack.CanUndo);
    }

    // ── Push<T> — without stack: direct apply ─────────────────────────────

    [Fact]
    public void SetProperty_WithoutUndoStack_AppliesDirectly()
    {
        var conn = MakeConn();
        conn.QuestionNodeTextDisplay = "Always";
        Assert.Equal("Always", conn.QuestionNodeTextDisplay);
    }

    // ── Push<T> — with stack: undoable ───────────────────────────────────

    [Fact]
    public void SetQuestionNodeTextDisplay_WithUndoStack_IsUndoable()
    {
        var conn  = MakeConn(qtd: "");
        var stack = new UndoRedoStack();
        conn.UndoStack               = stack;
        conn.QuestionNodeTextDisplay = "Always";
        stack.Undo();
        Assert.Equal("", conn.QuestionNodeTextDisplay);
    }

    [Fact]
    public void SetRandomWeight_WithUndoStack_IsUndoable()
    {
        var conn  = MakeConn(weight: 1f);
        var stack = new UndoRedoStack();
        conn.UndoStack    = stack;
        conn.RandomWeight = 2.5f;
        stack.Undo();
        Assert.Equal(1f, conn.RandomWeight);
    }

    // ── IsAlways / IsNever ────────────────────────────────────────────────

    [Fact]
    public void IsAlways_TrueWhenQTDIsAlways()
    {
        var conn = MakeConn(qtd: "Always");
        Assert.True(conn.IsAlways);
        Assert.False(conn.IsNever);
    }

    [Fact]
    public void IsNever_TrueWhenQTDIsNever()
    {
        var conn = MakeConn(qtd: "Never");
        Assert.True(conn.IsNever);
        Assert.False(conn.IsAlways);
    }

    [Fact]
    public void IsAlways_FalseWhenQTDIsEmpty()
    {
        var conn = MakeConn(qtd: "");
        Assert.False(conn.IsAlways);
        Assert.False(conn.IsNever);
    }

    [Fact]
    public void SetQTD_RaisesPropertyChangedForIsAlwaysAndIsNever()
    {
        var conn    = MakeConn();
        var changed = new List<string?>();
        conn.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        conn.QuestionNodeTextDisplay = "Always";
        Assert.Contains(nameof(conn.IsAlways), changed);
        Assert.Contains(nameof(conn.IsNever),  changed);
    }

    // ── HasConditions / ConditionCount ────────────────────────────────────

    [Fact]
    public void HasConditions_FalseWhenEmpty()
    {
        var conn = MakeConn();
        Assert.False(conn.HasConditions);
        Assert.Equal(0, conn.ConditionCount);
    }

    [Fact]
    public void HasConditions_TrueWhenConditionsSet()
    {
        var conn = MakeConn();
        conn.Conditions = [new ConditionLeaf("Boolean A()", [], false, "And")];
        Assert.True(conn.HasConditions);
        Assert.Equal(1, conn.ConditionCount);
    }

    // ── ConditionCountLabel (link-card button face, 2026-07-02 pane rework) ──

    [Fact]
    public void ConditionCountLabel_ComposesGlyphAndCount()
    {
        var conn = MakeConn();
        // StubStringProvider echoes keys → the localised glyph appears as its key.
        Assert.Equal("Link_ConditionGlyph 0", conn.ConditionCountLabel);
    }

    [Fact]
    public void SetConditions_RaisesConditionCountLabelChange()
    {
        var conn    = MakeConn();
        var changed = new List<string?>();
        conn.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        conn.Conditions = [new ConditionLeaf("Boolean A()", [], false, "And")];
        Assert.Contains(nameof(conn.ConditionCountLabel), changed);
        Assert.Equal("Link_ConditionGlyph 1", conn.ConditionCountLabel);
    }

    [Fact]
    public void SetConditions_WithUndoStack_IsUndoable()
    {
        var conn  = MakeConn();
        var stack = new UndoRedoStack();
        conn.UndoStack  = stack;
        conn.Conditions = [new ConditionLeaf("Boolean A()", [], false, "And")];
        stack.Undo();
        Assert.Empty(conn.Conditions);
    }
}
