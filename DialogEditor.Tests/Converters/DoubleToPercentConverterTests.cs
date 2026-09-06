using System.Globalization;
using DialogEditor.Avalonia.Converters;

namespace DialogEditor.Tests.Converters;

public class DoubleToPercentConverterTests
{
    [Theory]
    [InlineData(1.0,  "100%")]
    [InlineData(1.25, "125%")]
    [InlineData(1.5,  "150%")]
    [InlineData(1.75, "175%")]
    [InlineData(2.0,  "200%")]
    public void Convert_FormatsAsPercent(double scale, string expected)
    {
        var result = new DoubleToPercentConverter().Convert(scale, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_NonDouble_ReturnsNull()
    {
        var result = new DoubleToPercentConverter().Convert("not a double", typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }
}
