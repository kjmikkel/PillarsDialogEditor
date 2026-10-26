using DialogEditor.Core.Layout;

namespace DialogEditor.Tests.Layout;

/// GitHub issue 221: zoomed out, a card shrinks to its header and one line so a large hub's
/// shape reads; from 75% it is the full issue-82 card.
public class CardDetailTests
{
    [Theory]
    [InlineData(0.25, 1)]
    [InlineData(0.74, 1)]
    [InlineData(0.75, CardMetrics.MaxBodyLines)]
    [InlineData(1.00, CardMetrics.MaxBodyLines)]
    [InlineData(3.00, CardMetrics.MaxBodyLines)]
    public void BodyLines_FollowTheZoomBand(double zoom, int lines)
        => Assert.Equal(lines, CardDetail.BodyLinesAt(zoom));

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(0.75, true)]
    [InlineData(2.0, true)]
    public void Footer_HiddenOnlyWhenZoomedOut(double zoom, bool shown)
        => Assert.Equal(shown, CardDetail.ShowsFooterAt(zoom));

    [Fact]
    public void CompactCard_IsNeverTallerThanAnyNormalCard()
        // Layout places cards by their normal height, so a compact card only leaves space.
        => Assert.True(CardMetrics.CompactHeight <= CardMetrics.EstimateHeight("x"));
}
