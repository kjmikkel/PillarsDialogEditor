using System.Globalization;
using DialogEditor.Avalonia.Converters;

namespace DialogEditor.Tests.Converters;

public class FontScaleToPercentConverterTests
{
    [Theory]
    [InlineData(1.0,  "100%")]
    [InlineData(1.25, "125%")]
    [InlineData(1.5,  "150%")]
    [InlineData(1.75, "175%")]
    [InlineData(2.0,  "200%")]
    public void Convert_FormatsAsPercent(double scale, string expected)
    {
        var result = new FontScaleToPercentConverter().Convert(scale, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_NonDouble_ReturnsNull()
    {
        var result = new FontScaleToPercentConverter().Convert("not a double", typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }
}
