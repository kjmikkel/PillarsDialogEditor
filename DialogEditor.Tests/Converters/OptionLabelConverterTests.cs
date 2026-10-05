using System.Globalization;
using DialogEditor.Avalonia.Converters;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Converters;

/// Issue 132: dropdown items are stored enum names; this turns one into its localised
/// label (or tooltip) using the ConverterParameter as the key prefix.
public class OptionLabelConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed class MapProvider(Dictionary<string, string> map) : IStringProvider
    {
        public string Get(string key) => map.TryGetValue(key, out var v) ? v : $"[{key}]";
        public bool TryGet(string key, out string value)
        {
            if (map.TryGetValue(key, out var v)) { value = v; return true; }
            value = string.Empty; return false;
        }
    }

    public OptionLabelConverterTests() => Loc.Configure(new MapProvider(new()
    {
        ["Option_DisplayBark"]                 = "Ausruf",
        ["Option_DisplayNotSet"]               = "(nicht gesetzt)",
        ["ToolTip_Option_Display_Fallback"]    = "Unbekannt: {0}",
    }));

    private static object? Convert(object? value, string prefix) =>
        new OptionLabelConverter().Convert(value, typeof(string), prefix, Inv);

    [Fact]
    public void KnownName_GetsItsLocalisedLabel() =>
        Assert.Equal("Ausruf", Convert("Bark", "Option_Display"));

    [Fact]
    public void EmptyValue_GetsTheNotSetLabel() =>
        Assert.Equal("(nicht gesetzt)", Convert("", "Option_Display"));

    [Fact]
    public void UnknownName_WithoutAFallbackKey_IsShownAsStored() =>
        Assert.Equal("Unknown(7)", Convert("Unknown(7)", "Option_Display"));

    [Fact]
    public void UnknownName_WithAFallbackKey_IsFormattedIntoIt() =>
        Assert.Equal("Unbekannt: Unknown(7)", Convert("Unknown(7)", "ToolTip_Option_Display"));

    [Fact]
    public void NonString_PassesThrough() =>
        Assert.Null(Convert(null, "Option_Display"));
}
