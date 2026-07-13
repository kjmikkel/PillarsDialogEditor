using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class FindSnippetTests
{
    [Fact]
    public void ShortText_ReturnedWhole_NoEllipsis()
    {
        Assert.Equal("hello world", FindSnippet.Extract("hello world", 6, 5));
    }

    [Fact]
    public void LeadingContext_TrimmedWithEllipsis()
    {
        var text = new string('a', 50) + "MATCH" + new string('b', 50);
        var snip = FindSnippet.Extract(text, 50, 5, context: 10);
        Assert.StartsWith("…", snip);
        Assert.EndsWith("…", snip);
        Assert.Contains("MATCH", snip);
    }

    [Fact]
    public void Newlines_FlattenedToSpaces()
    {
        var snip = FindSnippet.Extract("line1\r\nMATCH\nline3", 7, 5, context: 10);
        Assert.DoesNotContain("\n", snip);
        Assert.DoesNotContain("\r", snip);
        Assert.Contains("MATCH", snip);
    }

    [Fact]
    public void MatchAtStart_NoLeadingEllipsis()
    {
        var snip = FindSnippet.Extract("MATCH" + new string('b', 50), 0, 5, context: 10);
        Assert.StartsWith("MATCH", snip);
        Assert.EndsWith("…", snip);
    }
}
