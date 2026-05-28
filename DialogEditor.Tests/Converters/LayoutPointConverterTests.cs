using System.Globalization;
using Avalonia;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Converters;

public class LayoutPointConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Fact]
    public void Convert_LayoutPoint_ReturnsAvaloniaPoint()
    {
        var result = new LayoutPointConverter().Convert(new LayoutPoint(10, 20), typeof(Point), null, Inv);
        Assert.Equal(new Point(10, 20), result);
    }

    [Fact]
    public void Convert_NonLayoutPoint_ReturnsUnsetValue()
    {
        var result = new LayoutPointConverter().Convert("not a point", typeof(Point), null, Inv);
        Assert.Equal(AvaloniaProperty.UnsetValue, result);
    }

    [Fact]
    public void ConvertBack_AvaloniaPoint_ReturnsLayoutPoint()
    {
        var result = new LayoutPointConverter().ConvertBack(new Point(10, 20), typeof(LayoutPoint), null, Inv);
        Assert.Equal(new LayoutPoint(10, 20), result);
    }

    [Fact]
    public void ConvertBack_UnsetValue_ReturnsDefaultLayoutPoint()
    {
        var result = new LayoutPointConverter().ConvertBack(AvaloniaProperty.UnsetValue, typeof(LayoutPoint), null, Inv);
        Assert.Equal(default(LayoutPoint), result);
    }

    [Fact]
    public void ConvertBack_Null_ReturnsDefaultLayoutPoint()
    {
        var result = new LayoutPointConverter().ConvertBack(null, typeof(LayoutPoint), null, Inv);
        Assert.Equal(default(LayoutPoint), result);
    }
}
