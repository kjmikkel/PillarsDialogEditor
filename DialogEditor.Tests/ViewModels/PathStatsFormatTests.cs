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
        Assert.Equal(expected, PathStatsFormat.ReadingTime(words));
    }
}
