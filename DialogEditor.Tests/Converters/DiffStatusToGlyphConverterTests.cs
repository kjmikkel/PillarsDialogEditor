using System.Globalization;
using DialogEditor.Avalonia.Converters;
using DialogEditor.ViewModels;
using Xunit;

namespace DialogEditor.Tests.Converters;

public class DiffStatusToGlyphConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(DiffStatus.Added,   "+")]
    [InlineData(DiffStatus.Changed, "~")]
    [InlineData(DiffStatus.Removed, "-")]
    [InlineData(DiffStatus.Unchanged, "")]
    public void Convert_DiffStatus_ReturnsExpectedGlyph(DiffStatus status, string expected)
        => Assert.Equal(expected, new DiffStatusToGlyphConverter().Convert(status, typeof(string), null, Inv));

    [Fact]
    public void Convert_Null_ReturnsEmpty()
        => Assert.Equal("", new DiffStatusToGlyphConverter().Convert(null, typeof(string), null, Inv));
}
