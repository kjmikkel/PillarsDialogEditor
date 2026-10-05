using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Models;

/// Issue 129: both games store DialogueLink.RandomWeight as an int, so the editor's float
/// weight is turned into the whole number the game will read.
public class LinkWeightTests
{
    [Theory]
    [InlineData(1f,   1)]
    [InlineData(3f,   3)]
    [InlineData(1.5f, 2)]   // halves round away from zero, as a person would expect
    [InlineData(2.5f, 3)]
    [InlineData(2.4f, 2)]
    [InlineData(0f,   0)]   // 0 stays 0: the game never picks such a link
    public void ToGame_RoundsToTheNearestWholeNumber(float weight, int expected) =>
        Assert.Equal(expected, LinkWeight.ToGame(weight));

    [Theory]
    [InlineData(0.4f)]
    [InlineData(0.01f)]
    public void ToGame_SmallPositiveWeight_BecomesOne(float weight) =>
        // The game picks by cumulative int weight, so 0 means "never chosen". A small
        // positive weight was meant as "rarely", never as "never".
        Assert.Equal(1, LinkWeight.ToGame(weight));

    [Fact]
    public void ToGame_NegativeWeight_BecomesZero() =>
        Assert.Equal(0, LinkWeight.ToGame(-2f));
}
