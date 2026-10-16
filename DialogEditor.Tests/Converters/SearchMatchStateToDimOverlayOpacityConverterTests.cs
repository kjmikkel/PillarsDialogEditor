using System.Globalization;
using DialogEditor.Avalonia.Converters;
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.Converters;

public class SearchMatchStateToDimOverlayOpacityConverterTests
{
    [Theory]
    [InlineData(SearchMatchState.None, 0.0)]
    [InlineData(SearchMatchState.Match, 0.0)]
    [InlineData(SearchMatchState.Dimmed, 0.6)]
    public void Convert_MapsStateToOverlayOpacity(SearchMatchState state, double expected)
    {
        var result = new SearchMatchStateToDimOverlayOpacityConverter()
            .Convert(state, typeof(double), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, (double)result, 3);
    }
}
