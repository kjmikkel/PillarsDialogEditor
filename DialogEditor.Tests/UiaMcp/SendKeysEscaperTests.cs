using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// SendKeys treats + ^ % ~ ( ) { } [ ] as syntax, so literal text containing them must be
/// brace-wrapped. Getting this wrong does not error — it silently sends the WRONG KEYS,
/// which is why it is worth a unit test rather than a comment.
/// </summary>
public class SendKeysEscaperTests
{
    [Theory]
    [InlineData("[", "{[}")]
    [InlineData("]", "{]}")]
    [InlineData("+", "{+}")]
    [InlineData("^", "{^}")]
    [InlineData("%", "{%}")]
    [InlineData("~", "{~}")]
    [InlineData("(", "{(}")]
    [InlineData(")", "{)}")]
    public void EscapesEverySendKeysMetacharacter(string input, string expected)
        => Assert.Equal(expected, SendKeysEscaper.EscapeLiteral(input));

    [Fact]
    public void EscapesBracesThemselves()
    {
        Assert.Equal("{{}", SendKeysEscaper.EscapeLiteral("{"));
        Assert.Equal("{}}", SendKeysEscaper.EscapeLiteral("}"));
    }

    [Fact]
    public void LeavesOrdinaryTextAlone()
        => Assert.Equal("Eder says hello", SendKeysEscaper.EscapeLiteral("Eder says hello"));

    [Fact]
    public void EscapesMetacharactersEmbeddedInRealDialogueText()
    {
        // Dialogue text carries substitution tokens in square brackets, so this is the
        // realistic case rather than an edge case.
        Assert.Equal("Hello {[}playername{]}, 50{%} done",
            SendKeysEscaper.EscapeLiteral("Hello [playername], 50% done"));
    }

    [Fact]
    public void HandlesEmptyInput()
        => Assert.Equal("", SendKeysEscaper.EscapeLiteral(""));
}
