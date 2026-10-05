using System.Globalization;
using DialogEditor.Avalonia.Converters;

namespace DialogEditor.Tests.Converters;

public class NumericConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── WeightDecimalConverter ────────────────────────────────────────────

    [Fact]
    public void WeightDecimal_Convert_Int_ReturnsDecimal()
    {
        var result = new WeightDecimalConverter().Convert(2, typeof(decimal?), null, Inv);
        Assert.Equal(2m, result);
    }

    [Fact]
    public void WeightDecimal_Convert_Null_ReturnsNull()
    {
        var result = new WeightDecimalConverter().Convert(null, typeof(decimal?), null, Inv);
        Assert.Null(result);
    }

    [Fact]
    public void WeightDecimal_ConvertBack_Decimal_ReturnsInt()
    {
        var result = new WeightDecimalConverter().ConvertBack(3m, typeof(int), null, Inv);
        Assert.Equal(3, result);
    }

    [Fact]
    public void WeightDecimal_ConvertBack_TypedFraction_IsRoundedToAWholeWeight()
    {
        // The NumericUpDown still accepts "1.5" typed by hand; the games store an int (issue 129).
        var result = new WeightDecimalConverter().ConvertBack(1.5m, typeof(int), null, Inv);
        Assert.Equal(2, result);
    }

    [Fact]
    public void WeightDecimal_ConvertBack_Null_ReturnsNull()
    {
        var result = new WeightDecimalConverter().ConvertBack(null, typeof(int), null, Inv);
        Assert.Null(result);
    }
}
