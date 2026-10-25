using DialogEditor.Core.Text;

namespace DialogEditor.Tests.Text;

/// <summary>
/// GitHub issue 258: the game's text markup (<c>&lt;i&gt;</c>, <c>&lt;ispeech&gt;</c>,
/// <c>&lt;link=…&gt;</c>, …) showed literally on cards and in summaries. TextMarkup turns it into
/// styled runs for cards and into plain text for one-line summaries. Only the tags the game
/// uses are touched, so a literal "&lt;3" or an unknown tag stays as written.
/// </summary>
public class TextMarkupTests
{
    [Theory]
    [InlineData("Ask about the <i>canonical</i> mod.", "Ask about the canonical mod.")]
    [InlineData("<ispeech>Show. Them.</ispeech>", "Show. Them.")]
    [InlineData("I'd <xg>nae</xg> be surprised.", "I'd nae be surprised.")]
    [InlineData("<color=\"red\">(Required - Incomplete)</color>", "(Required - Incomplete)")]
    [InlineData("We're <link=\"glossary://GlossaryEntry_Action_LieAhull\">lying ahull</link>.", "We're lying ahull.")]
    [InlineData("Fire <sprite=\"Inline\" name=\"Cannon\" tint=1> now", "Fire  now")]
    [InlineData("<b>Bold</b> and <I>caps</I>", "Bold and caps")]
    public void Strip_RemovesGameMarkup_KeepsTheText(string raw, string plain)
        => Assert.Equal(plain, TextMarkup.Strip(raw));

    [Theory]
    [InlineData("I <3 you")]
    [InlineData("a < b and c > d")]
    [InlineData("<foo>unknown</foo>")]
    [InlineData("")]
    public void Strip_LeavesOtherAngleBracketsAlone(string raw)
        => Assert.Equal(raw, TextMarkup.Strip(raw));

    [Fact]
    public void Strip_LenientAboutAMissingClosingQuote()
        // tags.json notes a few shipped lines miss the closing quote on a link attribute.
        => Assert.Equal("\"Perla Vailian, fentre?\"",
            TextMarkup.Strip("<link=\"neutralvalue://Vailian: Do you speak Vailian, sir?>\"Perla Vailian, fentre?\"</link>"));

    [Fact]
    public void Parse_MarksItalicAndBoldRuns()
    {
        var runs = TextMarkup.Parse("Plain <i>slanted <b>both</b></i> <ispeech>inner</ispeech>");
        Assert.Equal(
            [
                new MarkupRun("Plain ", false, false),
                new MarkupRun("slanted ", true, false),
                new MarkupRun("both", true, true),
                new MarkupRun(" ", false, false),
                new MarkupRun("inner", true, false),
            ],
            runs);
    }

    [Fact]
    public void Parse_UnclosedTag_StylesToTheEnd_AndAStrayCloseIsDropped()
    {
        var runs = TextMarkup.Parse("a </i>b <i>c");
        Assert.Equal([new MarkupRun("a b ", false, false), new MarkupRun("c", true, false)], runs);
    }

    [Fact]
    public void Parse_NoMarkup_IsOnePlainRun()
        => Assert.Equal([new MarkupRun("Hello", false, false)], TextMarkup.Parse("Hello"));
}
