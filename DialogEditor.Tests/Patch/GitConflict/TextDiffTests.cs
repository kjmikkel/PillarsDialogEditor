using DialogEditor.Patch.GitConflict;
using Xunit;

namespace DialogEditor.Tests.Patch.GitConflict;

public class TextDiffTests
{
    [Fact]
    public void Diff_SharedPrefixAndSuffix_IsolatesMiddle()
    {
        var spans = TextDiff.Diff("Welcome back, friend.", "Welcome back, traveler.");

        Assert.Collection(spans,
            s => { Assert.Equal("Welcome back, ", s.Text); Assert.Equal(DiffKind.Common, s.Kind); },
            s => { Assert.Equal("friend",         s.Text); Assert.Equal(DiffKind.MineOnly, s.Kind); },
            s => { Assert.Equal("traveler",       s.Text); Assert.Equal(DiffKind.TheirsOnly, s.Kind); },
            s => { Assert.Equal(".",              s.Text); Assert.Equal(DiffKind.Common, s.Kind); });
    }

    [Fact]
    public void Diff_IdenticalStrings_IsSingleCommonSpan()
    {
        var spans = TextDiff.Diff("same", "same");
        var only = Assert.Single(spans);
        Assert.Equal("same", only.Text);
        Assert.Equal(DiffKind.Common, only.Kind);
    }

    [Fact]
    public void Diff_NoCommonChars_IsMineThenTheirs()
    {
        var spans = TextDiff.Diff("abc", "xyz");
        Assert.Collection(spans,
            s => { Assert.Equal("abc", s.Text); Assert.Equal(DiffKind.MineOnly, s.Kind); },
            s => { Assert.Equal("xyz", s.Text); Assert.Equal(DiffKind.TheirsOnly, s.Kind); });
    }

    [Fact]
    public void Diff_PureInsertion_OnlyTheirsMiddle()
    {
        var spans = TextDiff.Diff("go", "go now");
        Assert.Collection(spans,
            s => { Assert.Equal("go",   s.Text); Assert.Equal(DiffKind.Common, s.Kind); },
            s => { Assert.Equal(" now", s.Text); Assert.Equal(DiffKind.TheirsOnly, s.Kind); });
    }
}
