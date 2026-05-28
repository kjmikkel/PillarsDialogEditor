using System.Globalization;
using DialogEditor.Avalonia.Converters;

namespace DialogEditor.Tests.Converters;

public class BoolConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── BoolToOpacityConverter ────────────────────────────────────────────

    [Fact]
    public void BoolToOpacity_True_ReturnsOne()
    {
        var result = new BoolToOpacityConverter().Convert(true, typeof(double), null, Inv);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void BoolToOpacity_False_ReturnsPointTwo()
    {
        var result = new BoolToOpacityConverter().Convert(false, typeof(double), null, Inv);
        Assert.Equal(0.2, result);
    }

    [Fact]
    public void BoolToOpacity_NullValue_ReturnsPointTwo()
    {
        var result = new BoolToOpacityConverter().Convert(null, typeof(double), null, Inv);
        Assert.Equal(0.2, result);
    }

    // ── InverseBoolConverter ──────────────────────────────────────────────

    [Fact]
    public void InverseBool_Convert_True_ReturnsFalse()
    {
        var result = new InverseBoolConverter().Convert(true, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    [Fact]
    public void InverseBool_Convert_False_ReturnsTrue()
    {
        var result = new InverseBoolConverter().Convert(false, typeof(bool), null, Inv);
        Assert.Equal(true, result);
    }

    [Fact]
    public void InverseBool_ConvertBack_True_ReturnsFalse()
    {
        var result = new InverseBoolConverter().ConvertBack(true, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    // ── CountToBoolConverter ──────────────────────────────────────────────

    [Fact]
    public void CountToBool_Zero_ReturnsFalse()
    {
        var result = new CountToBoolConverter().Convert(0, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    [Fact]
    public void CountToBool_Positive_ReturnsTrue()
    {
        var result = new CountToBoolConverter().Convert(3, typeof(bool), null, Inv);
        Assert.Equal(true, result);
    }

    [Fact]
    public void CountToBool_Negative_ReturnsFalse()
    {
        var result = new CountToBoolConverter().Convert(-1, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    [Fact]
    public void CountToBool_NonInt_ReturnsFalse()
    {
        var result = new CountToBoolConverter().Convert("5", typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }
}
