using System.Globalization;
using DialogEditor.Avalonia.Converters;

namespace DialogEditor.Tests.Converters;

public class NumericConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── FloatDecimalConverter ─────────────────────────────────────────────

    [Fact]
    public void FloatDecimal_Convert_Float_ReturnsDecimal()
    {
        var result = new FloatDecimalConverter().Convert(1.5f, typeof(decimal?), null, Inv);
        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void FloatDecimal_Convert_Null_ReturnsNull()
    {
        var result = new FloatDecimalConverter().Convert(null, typeof(decimal?), null, Inv);
        Assert.Null(result);
    }

    [Fact]
    public void FloatDecimal_ConvertBack_Decimal_ReturnsFloat()
    {
        var result = new FloatDecimalConverter().ConvertBack(1.5m, typeof(float), null, Inv);
        Assert.Equal(1.5f, result);
    }

    [Fact]
    public void FloatDecimal_ConvertBack_Null_ReturnsNull()
    {
        var result = new FloatDecimalConverter().ConvertBack(null, typeof(float), null, Inv);
        Assert.Null(result);
    }
}
