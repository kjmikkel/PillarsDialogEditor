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
        new("Text.OnAccent / Button.Primary",     "Palette.White", "Palette.Azure.600",   true),
        new("Text.OnAccent / Button.Destructive", "Palette.White", "Palette.Crimson.700", true),
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
    [InlineData("Palette.HighContrast", 7.0, 4.5)]
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

    // Borders are NOT in the shared Pairs list: Light/Dark borders are intentionally low-contrast
    // dividers (~1.3:1). High-Contrast is the one palette where bright, visible borders are the whole
    // point of the Line.* split — so it gets its own gate (spec 2026-06-09 §5). Non-text UI bar: >=4.5.
    private static readonly (string Line, string Surface)[] HcBorderPairs =
    {
        ("Palette.Line.Subtle",  "Palette.Neutral.115"), // Border.Subtle on Surface.Window
        ("Palette.Line.Subtle",  "Palette.Neutral.100"), // on Surface.Card
        ("Palette.Line.Default", "Palette.Neutral.115"),
        ("Palette.Line.Default", "Palette.Neutral.145"), // on Surface.Panel
        ("Palette.Line.Strong",  "Palette.Neutral.115"),
        // The pane GridSplitters (Brush.Border.Default) border the canvas backdrop on their
        // canvas side (Brush.Accent.Badge → Palette.Mauve.500), not just the dark panels — so
        // the divider must stay visible against the canvas too, or it vanishes into it.
        ("Palette.Line.Default", "Palette.Mauve.500"),   // GridSplitter on the canvas backdrop
    };

    // A highlighted connection (Brush.Connection.Highlighted = Palette.White) must stand out
    // from a resting one (Brush.Connection.Default = Palette.Connection.Base). In HC they
    // collided at pure white (Neutral.665 = white for text), so the highlight was invisible
    // among default links — hence the dedicated Connection.* primitive, dimmed in HC.
    [AvaloniaFact]
    public void HighContrastHighlightedConnectionStandsOut()
    {
        var dict  = PaletteHarness.Load("Palette.HighContrast");
        var ratio = Wcag.ContrastRatio(
            PaletteHarness.Color(dict, "Palette.White"),
            PaletteHarness.Color(dict, "Palette.Connection.Base"));
        Assert.True(ratio >= 1.8,
            $"highlighted vs resting connection in HC: {ratio:F2}:1 < 1.8:1");
    }

    [AvaloniaFact]
    public void HighContrastBordersAreVisible()
    {
        var dict = PaletteHarness.Load("Palette.HighContrast");
        var failures = new System.Collections.Generic.List<string>();
        foreach (var (line, surface) in HcBorderPairs)
        {
            var ratio = Wcag.ContrastRatio(
                PaletteHarness.Color(dict, line), PaletteHarness.Color(dict, surface));
            if (ratio < 4.5)
                failures.Add($"{line} on {surface}: {ratio:F2}:1 < 4.5:1");
        }
        Assert.True(failures.Count == 0,
            "High-Contrast borders must be visible (>=4.5:1):\n" + string.Join("\n", failures));
    }
}
