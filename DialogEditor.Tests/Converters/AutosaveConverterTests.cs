using System.Globalization;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Converters;

/// The Settings autosave option labels (issue #11). Loc is stubbed to echo keys, so
/// these assert which RESOURCE each value picks — the branching is the logic worth
/// pinning; the wording itself lives in Strings.axaml.
public class AutosaveIntervalConverterTests
{
    private static object? Convert(object? value)
    {
        Loc.Configure(new StubStringProvider());
        return new AutosaveIntervalConverter()
            .Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Zero_IsOff() => Assert.Equal("Settings_AutosaveInterval_Off", Convert(0));

    [Fact]
    public void Negative_IsAlsoOff() => Assert.Equal("Settings_AutosaveInterval_Off", Convert(-1));

    [Fact]
    public void SubMinute_UsesTheSecondsResource()
        => Assert.Equal("Settings_AutosaveInterval_Seconds", Convert(30));

    [Fact]
    public void ExactlyOneMinute_UsesTheSingularResource()
        => Assert.Equal("Settings_AutosaveInterval_OneMinute", Convert(60));

    [Fact]
    public void WholeMinutes_UseThePluralResource()
    {
        Assert.Equal("Settings_AutosaveInterval_Minutes", Convert(120));
        Assert.Equal("Settings_AutosaveInterval_Minutes", Convert(600));
    }

    [Fact]
    public void NonWholeMinutes_StayInSeconds_SoNoOptionReadsOnePointFiveMinutes()
        => Assert.Equal("Settings_AutosaveInterval_Seconds", Convert(90));

    [Fact]
    public void NonInteger_IsNull() => Assert.Null(Convert("60"));

    [Fact]
    public void ConvertBack_IsNotSupported()
        => Assert.Throws<NotSupportedException>(() => new AutosaveIntervalConverter()
            .ConvertBack(0, typeof(int), null, CultureInfo.InvariantCulture));
}

public class AutosaveGenerationsConverterTests
{
    private static object? Convert(object? value)
    {
        Loc.Configure(new StubStringProvider());
        return new AutosaveGenerationsConverter()
            .Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void One_UsesTheSingularResource()
        => Assert.Equal("Settings_AutosaveGenerations_One", Convert(1));

    [Fact]
    public void MoreThanOne_UsesThePluralResource()
        => Assert.Equal("Settings_AutosaveGenerations_Many", Convert(3));

    [Fact]
    public void NonInteger_IsNull() => Assert.Null(Convert(null));

    [Fact]
    public void ConvertBack_IsNotSupported()
        => Assert.Throws<NotSupportedException>(() => new AutosaveGenerationsConverter()
            .ConvertBack(1, typeof(int), null, CultureInfo.InvariantCulture));
}
