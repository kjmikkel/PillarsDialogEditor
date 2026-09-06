using DialogEditor.ViewModels;

namespace DialogEditor.Tests.ViewModels;

public class PathStatsFormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(200, "1:00")]   // 200 words @ 200 wpm = 1 min
    [InlineData(350, "1:45")]   // 350/200 min = 1.75 min = 1:45
    [InlineData(100, "0:30")]
    public void ReadingTime_FormatsMinutesSeconds(int words, string expected)
    {
        Assert.Equal(expected, PathStatsFormat.ReadingTime(words, PathStatsFormat.DefaultWordsPerMinute));
    }

    [Fact] // The historical constant stays the baseline every caller and test shares.
    public void DefaultWordsPerMinute_Is200()
    {
        Assert.Equal(200, PathStatsFormat.DefaultWordsPerMinute);
    }

    [Theory] // The same word count reads slower at a slower configured speed.
    [InlineData(100, 100, "1:00")]
    [InlineData(100, 200, "0:30")]
    [InlineData(600, 120, "5:00")]
    [InlineData(600, 300, "2:00")]
    public void ReadingTime_HonoursConfiguredSpeed(int words, int wpm, string expected)
    {
        Assert.Equal(expected, PathStatsFormat.ReadingTime(words, wpm));
    }

    [Theory] // settings.json is hand-editable, so a nonsense speed must not divide by zero.
    [InlineData(0)]
    [InlineData(-50)]
    public void ReadingTime_NonPositiveSpeed_FallsBackToDefault(int wpm)
    {
        Assert.Equal(PathStatsFormat.ReadingTime(200, PathStatsFormat.DefaultWordsPerMinute),
                     PathStatsFormat.ReadingTime(200, wpm));
    }

    [Fact] // Omitting the speed keeps the historical 200 wpm behaviour.
    public void ReadingTime_DefaultArgument_Is200Wpm()
    {
        Assert.Equal("1:00", PathStatsFormat.ReadingTime(200));
    }
}
