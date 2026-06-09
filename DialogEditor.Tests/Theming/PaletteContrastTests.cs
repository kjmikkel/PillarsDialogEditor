using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

public class PaletteContrastTests
{
    private readonly record struct Pair(string Label, string Fg, string Bg, bool NormalText);

    // Curated load-bearing role pairs (spec §5.1). Primitive keys; backing token in the label.
    private static readonly Pair[] Pairs =
    {
        new("Text.Primary / Surface.Window",  "Palette.Neutral.910", "Palette.Neutral.115", true),
        new("Text.Primary / Surface.Panel",   "Palette.Neutral.910", "Palette.Neutral.145", true),
        new("Text.Primary / Surface.Card",    "Palette.Neutral.910", "Palette.Neutral.100", true),
        new("Text.Primary / Surface.Input",   "Palette.Neutral.910", "Palette.Neutral.80",  true),
        new("Text.Secondary / Surface.Panel", "Palette.Neutral.800", "Palette.Neutral.145", true),
        new("Text.Secondary / Surface.Card",  "Palette.Neutral.800", "Palette.Neutral.100", true),
        new("Text.OnLight / Node.Npc.Body",      "Palette.Ink.Strong", "Palette.Parchment.100", true),
        new("Text.OnLight / Node.Player.Body",   "Palette.Ink.Strong", "Palette.Azure.150",     true),
        new("Text.OnLight / Node.Narrator.Body", "Palette.Ink.Strong", "Palette.Teal.150",      true),
        new("Text.OnAccent / Node.Npc.Header",      "Palette.White", "Palette.Crimson.700", true),
        new("Text.OnAccent / Node.Player.Header",   "Palette.White", "Palette.Azure.600",   true),
        new("Text.OnAccent / Node.Narrator.Header", "Palette.White", "Palette.Teal.600",    true),
        new("Text.OnAccent / Node.Script.Header",   "Palette.White", "Palette.Slate.700",   true),
        new("Text.OnAccent / Button.Confirm",     "Palette.White", "Palette.Green.600",  true),
        new("Text.OnAccent / Button.Caution",     "Palette.White", "Palette.Burnt.600",  true),
        new("Text.Status.Added / Surface.Card",   "Palette.Green.400", "Palette.Neutral.100", true),
        new("Text.Status.Changed / Surface.Card", "Palette.Amber.540", "Palette.Neutral.100", true),
        new("Text.Status.Removed / Surface.Card", "Palette.Red.450",   "Palette.Neutral.100", true),
        new("Text.Caption / Surface.Card",  "Palette.Neutral.600", "Palette.Neutral.100", false),
        new("Text.Muted / Surface.Card",    "Palette.Neutral.535", "Palette.Neutral.100", false),
        new("Severity.Warning / Surface.Panel", "Palette.Amber.500", "Palette.Neutral.145", false),
        new("Severity.Error / Surface.Panel",   "Palette.Red.500",   "Palette.Neutral.145", false),
        new("Severity.Info / Surface.Panel",    "Palette.Sky.450",   "Palette.Neutral.145", false),
    };

    // Dark is exempt (grandfathered baseline, spec §5). Thresholds: {normalText, largeUI}.
    [AvaloniaTheory]
    [InlineData("Palette.Light", 4.5, 3.0)]
    [InlineData("Palette.Colourblind", 4.5, 3.0)]
    public void PaletteMeetsContrastTargets(string set, double normalMin, double largeMin)
    {
        var dict = PaletteHarness.Load(set);
        var failures = new System.Collections.Generic.List<string>();
        foreach (var p in Pairs)
        {
            var ratio = Wcag.ContrastRatio(
                PaletteHarness.Color(dict, p.Fg), PaletteHarness.Color(dict, p.Bg));
            var min = p.NormalText ? normalMin : largeMin;
            if (ratio < min)
                failures.Add($"{p.Label}: {ratio:F2}:1 < {min:F1}:1");
        }
        Assert.True(failures.Count == 0,
            $"{set} contrast failures:\n" + string.Join("\n", failures));
    }
}
