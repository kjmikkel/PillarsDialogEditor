using System.Globalization;
using DialogEditor.Avalonia.Converters;
using Xunit;

namespace DialogEditor.Tests.Converters;

public class CommitDateConverterTests
{
    private static readonly CommitDateConverter Conv = new();

    [Fact]
    public void FormatsShortDate_Invariant()
    {
        var date = new DateTimeOffset(2026, 5, 30, 14, 3, 0, TimeSpan.Zero);
        var result = Conv.Convert(date, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal("05/30/2026", result);   // invariant short-date pattern
    }

    [Fact]
    public void FormatsShortDate_GivenCulture()
    {
        var date = new DateTimeOffset(2026, 5, 30, 14, 3, 0, TimeSpan.Zero);
        var de   = CultureInfo.GetCultureInfo("de-DE");
        var result = Conv.Convert(date, typeof(string), null, de);
        Assert.Equal("30.05.2026", result);
    }

    [Fact]
    public void NonDate_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Conv.Convert("nope", typeof(string), null, CultureInfo.InvariantCulture));
    }
}
