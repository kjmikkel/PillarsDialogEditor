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
}
