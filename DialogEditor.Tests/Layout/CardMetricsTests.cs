using DialogEditor.Core.Layout;

namespace DialogEditor.Tests.Layout;

/// Issue 82: one source of truth for a canvas card's size, shared by the node template
/// (through the Canvas.* tokens) and AutoLayoutService.
public class CardMetricsTests
{
    [Fact]
    public void HeaderAndFooterHeights_AreLineHeightPlusPadding()
    {
        Assert.Equal(16 + 2 * 3, CardMetrics.HeaderHeight);
        Assert.Equal(13 + 2 * 2, CardMetrics.FooterHeight);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EstimateHeight_EmptyAndWhitespace_IsOneLine(string? text)
    {
        Assert.Equal(1, CardMetrics.EstimateBodyLines(text));
        Assert.Equal(CardMetrics.EstimateHeight("x"), CardMetrics.EstimateHeight(text));
    }

    [Fact]
    public void EstimateHeight_ShortLine_IsOneLine() =>
        Assert.Equal(1, CardMetrics.EstimateBodyLines("Tell me about the storm."));

    [Fact]
    public void EstimateHeight_VeryLongText_CapsAtFourLines()
    {
        var text = string.Concat(Enumerable.Repeat("A long line of dialogue. ", 40));
        Assert.Equal(CardMetrics.MaxBodyLines, CardMetrics.EstimateBodyLines(text));
        Assert.Equal(CardMetrics.MaxHeight, CardMetrics.EstimateHeight(text));
    }

    [Fact]
    public void EstimateHeight_NeverDecreasesAsTextGrows()
    {
        var previous = 0.0;
        for (var n = 0; n <= 400; n += 7)
        {
            var h = CardMetrics.EstimateHeight(new string('a', n));
            Assert.True(h >= previous, $"height fell from {previous} to {h} at {n} characters");
            previous = h;
        }
    }

    [Fact]
    public void FourLineCard_FitsOldRowPitch() =>
        // Layouts saved before issue 82 used a 110 + 20 = 130 row pitch. A full card must fit
        // inside it so those layouts don't overlap with the new cards.
        Assert.True(CardMetrics.MaxHeight < 130, $"MaxHeight {CardMetrics.MaxHeight} >= 130");
}
