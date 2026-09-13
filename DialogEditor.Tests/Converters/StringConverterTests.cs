using System.Globalization;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Converters;

public class StringConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── NullOrEmptyToBoolConverter ────────────────────────────────────────

    [Fact]
    public void NullOrEmptyToBool_Null_ReturnsFalse()
        => Assert.Equal(false, new NullOrEmptyToBoolConverter().Convert(null, typeof(bool), null, Inv));

    [Fact]
    public void NullOrEmptyToBool_EmptyString_ReturnsFalse()
        => Assert.Equal(false, new NullOrEmptyToBoolConverter().Convert("", typeof(bool), null, Inv));

    [Fact]
    public void NullOrEmptyToBool_NonEmptyString_ReturnsTrue()
        => Assert.Equal(true, new NullOrEmptyToBoolConverter().Convert("hello", typeof(bool), null, Inv));

    [Fact]
    public void NullOrEmptyToBool_NonStringObject_ReturnsFalse()
        => Assert.Equal(false, new NullOrEmptyToBoolConverter().Convert(42, typeof(bool), null, Inv));

    // ── QTDDisplayConverter ───────────────────────────────────────────────

    [Fact]
    public void QTDDisplay_Convert_EmptyString_ReturnsParameter()
    {
        var result = new QTDDisplayConverter().Convert("", typeof(string), "(default)", Inv);
        Assert.Equal("(default)", result);
    }

    [Fact]
    public void QTDDisplay_Convert_EmptyStringWithNoParameter_FallsBackToTheResource()
    {
        // The fallback used to be a hard-coded "(default)". The view passes the localised
        // string as ConverterParameter; when it is missing the converter must still reach
        // for the same resource rather than showing English.
        Loc.Configure(new StubStringProvider());

        Assert.Equal("Option_QTD_Default",
            new QTDDisplayConverter().Convert("", typeof(string), null, Inv));
    }

    [Fact]
    public void QTDDisplay_ConvertBack_ResourceFallbackValue_ReturnsEmpty()
    {
        Loc.Configure(new StubStringProvider());

        Assert.Equal("",
            new QTDDisplayConverter().ConvertBack("Option_QTD_Default", typeof(string), null, Inv));
    }

    [Fact]
    public void QTDDisplay_Convert_NonEmptyString_PassesThrough()
    {
        var result = new QTDDisplayConverter().Convert("ShowOnce", typeof(string), "(default)", Inv);
        Assert.Equal("ShowOnce", result);
    }

    [Fact]
    public void QTDDisplay_Convert_NullValue_PassesThrough()
    {
        // null is not an empty string — passes through unchanged
        var result = new QTDDisplayConverter().Convert(null, typeof(string), "(default)", Inv);
        Assert.Null(result);
    }

    [Fact]
    public void QTDDisplay_ConvertBack_ParameterValue_ReturnsEmpty()
    {
        var result = new QTDDisplayConverter().ConvertBack("(default)", typeof(string), "(default)", Inv);
        Assert.Equal("", result);
    }

    [Fact]
    public void QTDDisplay_ConvertBack_NonParameterValue_PassesThrough()
    {
        var result = new QTDDisplayConverter().ConvertBack("ShowOnce", typeof(string), "(default)", Inv);
        Assert.Equal("ShowOnce", result);
    }
}
