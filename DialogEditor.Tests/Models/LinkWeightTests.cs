using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Models;

/// Issue 129: both games store DialogueLink.RandomWeight as an int, and so does the editor.
/// The one place a fraction can still come from is a number typed into the weight field.
public class LinkWeightTests
{
    [Theory]
    [InlineData("1",   1)]
    [InlineData("3",   3)]
    [InlineData("1.5", 2)]   // halves round away from zero, as a person would expect
    [InlineData("2.5", 3)]
    [InlineData("2.4", 2)]
    [InlineData("0",   0)]   // 0 stays 0: the game never picks such a link
    public void FromInput_RoundsToTheNearestWholeNumber(string typed, int expected) =>
        Assert.Equal(expected, LinkWeight.FromInput(decimal.Parse(typed, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("0.4")]
    [InlineData("0.01")]
    public void FromInput_SmallPositiveWeight_BecomesOne(string typed) =>
        // The game picks by cumulative int weight, so 0 means "never chosen". A small
        // positive weight was meant as "rarely", never as "never".
        Assert.Equal(1, LinkWeight.FromInput(decimal.Parse(typed, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void FromInput_NegativeWeight_BecomesZero() =>
        Assert.Equal(0, LinkWeight.FromInput(-2m));
}
