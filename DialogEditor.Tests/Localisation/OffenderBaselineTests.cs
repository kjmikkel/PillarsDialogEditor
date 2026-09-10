using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Localisation;

public class OffenderBaselineTests
{
    private static LiteralOffender O(string file, string text) => new(file, 1, text);

    [Fact]
    public void Compare_KnownOffender_IsNeitherAddedNorStale()
    {
        var (added, stale) = OffenderBaseline.Compare(
            ["A.cs|Add annotation"], [O("A.cs", "Add annotation")]);

        Assert.Empty(added);
        Assert.Empty(stale);
    }

    [Fact]
    public void Compare_OffenderNotInBaseline_IsReportedAsAdded()
    {
        var (added, _) = OffenderBaseline.Compare(
            ["A.cs|Add annotation"], [O("A.cs", "Add annotation"), O("B.cs", "Delete node {0}")]);

        Assert.Equal(["B.cs|Delete node {0}"], added);
    }

    [Fact]
    public void Compare_BaselineEntryThatIsFixed_IsReportedAsStale()
    {
        // The ratchet only turns one way: once a site is cleaned up, its baseline entry
        // must go, or the baseline silently re-licenses the same mistake later.
        var (_, stale) = OffenderBaseline.Compare(
            ["A.cs|Add annotation", "A.cs|Delete annotation"], [O("A.cs", "Add annotation")]);

        Assert.Equal(["A.cs|Delete annotation"], stale);
    }

    [Fact]
    public void Compare_SameTextMovedToAnotherFile_CountsAsAddedAndStale()
    {
        var (added, stale) = OffenderBaseline.Compare(
            ["A.cs|Edit node type"], [O("B.cs", "Edit node type")]);

        Assert.Equal(["B.cs|Edit node type"], added);
        Assert.Equal(["A.cs|Edit node type"], stale);
    }

    [Fact]
    public void Parse_PreservesTrailingSpaceInsideAnEntry()
    {
        // Literals built by concatenation keep their trailing space — "…LessThan, " +
        // "GreaterThanOrEqualTo". Trimming the whole line silently unmatches those
        // entries, so every one of them reads as a brand-new offender forever.
        var entries = OffenderBaseline.Parse("A.cs|Comparison operator: EqualTo, " + (char)10);

        Assert.Equal(["A.cs|Comparison operator: EqualTo, "], entries);
    }

    [Fact]
    public void Parse_SkipsCommentsAndBlankLines()
    {
        var entries = OffenderBaseline.Parse("# header\n\nA.cs|Add annotation\n  \nB.cs|Delete node {0}\n");

        Assert.Equal(["A.cs|Add annotation", "B.cs|Delete node {0}"], entries);
    }
}
