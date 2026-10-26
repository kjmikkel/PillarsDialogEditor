using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// GitHub issue 312: while the writer types, the squiggles already drawn follow the text and
/// only the word being edited loses its squiggle, until the delayed re-check runs.
public class SpellSpanShiftTests
{
    private static IReadOnlyList<SpellSpan> Shift(string before, string after, params SpellSpan[] spans)
        => SpellSpanShift.Apply(spans, before, after);

    [Fact]
    public void EditAfterTheWord_LeavesItAlone()
        => Assert.Equal([new SpellSpan(0, 7)], Shift("captian glows", "captian glowsx", new SpellSpan(0, 7)));

    [Fact]
    public void InsertBeforeTheWord_MovesItRight()
        => Assert.Equal([new SpellSpan(4, 7)], Shift("captian", "The captian", new SpellSpan(0, 7)));

    [Fact]
    public void DeleteBeforeTheWord_MovesItLeft()
        => Assert.Equal([new SpellSpan(0, 7)], Shift("The captian", "captian", new SpellSpan(4, 7)));

    [Fact]
    public void EditInsideTheWord_DropsIt()
        => Assert.Empty(Shift("captian glows", "captaian glows", new SpellSpan(0, 7)));

    [Fact]
    public void LetterTypedRightAfterTheWord_DropsIt()
        // "captian" became "captians": a different word, so wait for the re-check.
        => Assert.Empty(Shift("captian", "captians", new SpellSpan(0, 7)));

    [Fact]
    public void SpaceTypedRightAfterTheWord_KeepsIt()
        => Assert.Equal([new SpellSpan(0, 7)], Shift("captian", "captian ", new SpellSpan(0, 7)));

    [Fact]
    public void LetterTypedRightBeforeTheWord_DropsIt()
        => Assert.Empty(Shift("captian", "xcaptian", new SpellSpan(0, 7)));

    [Fact]
    public void SpaceTypedRightBeforeTheWord_MovesIt()
        => Assert.Equal([new SpellSpan(1, 7)], Shift("captian", " captian", new SpellSpan(0, 7)));

    [Fact]
    public void OnlyTheEditedWordGoes()
    {
        // "tonite" is edited; "captian" (before) stays and "sails" (after) is not misspelt.
        var spans = Shift("The captian sails tonite.", "The captian sails tonighte.",
                          new SpellSpan(4, 7), new SpellSpan(18, 6));
        Assert.Equal([new SpellSpan(4, 7)], spans);
    }

    [Fact]
    public void SameText_ReturnsTheSpans()
        => Assert.Equal([new SpellSpan(0, 7)], Shift("captian", "captian", new SpellSpan(0, 7)));

    [Fact]
    public void ReplacingEverything_DropsAll()
        => Assert.Empty(Shift("captian glows", "", new SpellSpan(0, 7)));
}
