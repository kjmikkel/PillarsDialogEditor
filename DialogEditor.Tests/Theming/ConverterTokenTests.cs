using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;

namespace DialogEditor.Tests.Theming;

public class ConverterTokenTests
{
    [AvaloniaFact]
    public void Resolve_KnownKey_ReturnsRegistryBrushInstance()
    {
        Application.Current!.TryGetResource("Brush.Node.Npc.Header",
            Application.Current!.ActualThemeVariant, out var expected);
        Assert.Same(expected, TokenBrushes.Resolve("Brush.Node.Npc.Header"));
    }

    [AvaloniaFact]
    public void Resolve_UnknownKey_Throws()
        => Assert.Throws<KeyNotFoundException>(() => TokenBrushes.Resolve("Brush.Does.Not.Exist"));

    [AvaloniaTheory]
    [InlineData(DialogEditor.Core.Models.SpeakerCategory.Npc, "body", "Brush.Node.Npc.Body")]
    [InlineData(DialogEditor.Core.Models.SpeakerCategory.Script, null, "Brush.Node.Script.Header")]
    public void SpeakerCategoryConverter_ReturnsRegistryBrush(
        DialogEditor.Core.Models.SpeakerCategory cat, string? zone, string key)
    {
        var conv = new DialogEditor.Avalonia.Converters.SpeakerCategoryToBrushConverter();
        var result = conv.Convert(cat, typeof(global::Avalonia.Media.IBrush), zone, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Same(TokenBrushes.Resolve(key), result);
    }
}
