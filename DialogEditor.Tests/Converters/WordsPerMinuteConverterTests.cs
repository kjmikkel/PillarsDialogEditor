using System.Globalization;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Converters;

public class WordsPerMinuteConverterTests
{
    public WordsPerMinuteConverterTests() => Loc.Configure(new StubStringProvider());

    [Fact] // Goes through Loc, so the unit label stays translatable.
    public void Convert_UsesTheLocalisedOptionKey()
    {
        var result = new WordsPerMinuteConverter()
            .Convert(200, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(Loc.Format("PathStats_WpmOption", 200), result);
    }

    [Fact]
    public void Convert_NonInt_ReturnsNull()
    {
        var result = new WordsPerMinuteConverter()
            .Convert("nope", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Null(result);
    }
}
