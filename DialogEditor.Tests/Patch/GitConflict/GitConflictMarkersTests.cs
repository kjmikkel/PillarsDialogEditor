using DialogEditor.Patch.GitConflict;
using Xunit;

namespace DialogEditor.Tests.Patch.GitConflict;

public class GitConflictMarkersTests
{
    private const string Conflicted =
        "{\n" +
        "<<<<<<< HEAD\n" +
        "  \"Name\": \"mine\",\n" +
        "=======\n" +
        "  \"Name\": \"theirs\",\n" +
        ">>>>>>> feature\n" +
        "  \"SchemaVersion\": 1\n" +
        "}\n";

    [Fact]
    public void HasMarkers_TrueWhenConflictPresent()
        => Assert.True(GitConflictMarkers.HasMarkers(Conflicted));

    [Fact]
    public void HasMarkers_FalseForCleanJson()
        => Assert.False(GitConflictMarkers.HasMarkers("{ \"Name\": \"ok\" }"));

    [Fact]
    public void SplitSides_MineTakesOursHunk()
    {
        var (mine, _) = GitConflictMarkers.SplitSides(Conflicted);
        Assert.Contains("\"Name\": \"mine\"", mine);
        Assert.DoesNotContain("theirs", mine);
        Assert.DoesNotContain("<<<<<<<", mine);
        Assert.Contains("\"SchemaVersion\": 1", mine); // common context kept
    }

    [Fact]
    public void SplitSides_TheirsTakesTheirsHunk()
    {
        var (_, theirs) = GitConflictMarkers.SplitSides(Conflicted);
        Assert.Contains("\"Name\": \"theirs\"", theirs);
        Assert.DoesNotContain("\"Name\": \"mine\"", theirs);
        Assert.DoesNotContain(">>>>>>>", theirs);
    }

    [Fact]
    public void SplitSides_Diff3BaseSectionDroppedFromBothSides()
    {
        var diff3 =
            "<<<<<<< HEAD\n" + "mineline\n" +
            "||||||| base\n"  + "baseline\n" +
            "=======\n"       + "theirsline\n" +
            ">>>>>>> other\n";
        var (mine, theirs) = GitConflictMarkers.SplitSides(diff3);
        Assert.Contains("mineline", mine);
        Assert.DoesNotContain("baseline", mine);
        Assert.Contains("theirsline", theirs);
        Assert.DoesNotContain("baseline", theirs);
    }

    [Fact]
    public void SplitSides_MultipleHunksAllResolved()
    {
        var two =
            "a\n<<<<<<< H\nm1\n=======\nt1\n>>>>>>> O\nb\n" +
            "<<<<<<< H\nm2\n=======\nt2\n>>>>>>> O\nc\n";
        var (mine, theirs) = GitConflictMarkers.SplitSides(two);
        Assert.Equal("a\nm1\nb\nm2\nc\n", mine);
        Assert.Equal("a\nt1\nb\nt2\nc\n", theirs);
    }
}
