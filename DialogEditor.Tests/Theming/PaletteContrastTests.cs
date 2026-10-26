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
        new("Text.OnAccent / ModeBadge.Reading",  "Palette.White", "Palette.Green.600",  true),
        new("Text.OnAccent / ModeBadge.Editing",  "Palette.White", "Palette.Burnt.600",  true),
        new("Text.OnAccent / ModeBadge.Reading.Hover",   "Palette.White", "Palette.Green.700", true),
        new("Text.OnAccent / ModeBadge.Reading.Pressed", "Palette.White", "Palette.Green.800", true),
        new("Text.OnAccent / ModeBadge.Editing.Hover",   "Palette.White", "Palette.Burnt.700", true),
        new("Text.OnAccent / ModeBadge.Editing.Pressed", "Palette.White", "Palette.Burnt.800", true),
        // Keyboard focus ring (two-tone, issue 234): the inner white ring sits on the badge fill
        // (covered by the OnAccent pairs above); the outer Text.Primary ring sits on the toolbar.
        new("ModeBadge.FocusRing.Outer / Control.Background", "Palette.Neutral.910", "Palette.Control.Background", false),
        new("Text.OnAccent / Button.Primary",     "Palette.White", "Palette.Azure.600",   true),
        new("Text.OnAccent / Button.Destructive", "Palette.White", "Palette.Crimson.700", true),
        new("Selection.Foreground / Selection.Background", "Palette.White", "Palette.Azure.600", true), // #83
        // Secondary buttons (issue 179): readable text on the fill, and an edge that marks the
        // button out from every surface it sits on (WCAG 1.4.11 non-text contrast).
        new("Button.Secondary.Foreground / Background", "Palette.Neutral.910", "Palette.Neutral.200", true),
        new("Button.Secondary.Foreground / Hover",      "Palette.Neutral.910", "Palette.Button.Hover", true),
        new("Button.Secondary.Border / Surface.Window", "Palette.Button.Edge", "Palette.Neutral.115", false),
        new("Button.Secondary.Border / Surface.Panel",  "Palette.Button.Edge", "Palette.Neutral.145", false),
        new("Button.Secondary.Border / Surface.Card",   "Palette.Button.Edge", "Palette.Neutral.100", false),
        new("Text.Status.Added / Surface.Card",  "Palette.Green.400", "Palette.Neutral.100", true),
        new("Text.Status.Changed / Surface.Card", "Palette.Amber.540", "Palette.Neutral.100", true),
        new("Text.Status.Removed / Surface.Card", "Palette.Red.450",   "Palette.Neutral.100", true),
        new("Text.Caption / Surface.Card",  "Palette.Neutral.600", "Palette.Neutral.100", false),
        // Muted is caption *text* (hints, counters, statistics), so it is held to the text bar on
        // every surface it sits on (GitHub issue 265).
        new("Text.Muted / Surface.Card",    "Palette.Neutral.535", "Palette.Neutral.100", true),
        new("Text.Muted / Surface.Window",  "Palette.Neutral.535", "Palette.Neutral.115", true),
        new("Text.Muted / Surface.Panel",   "Palette.Neutral.535", "Palette.Neutral.145", true),
        // Node card footers (condition count) and the no-text placeholder (GitHub issue 245).
        new("Text.OnLight.Muted / Node.Npc.Footer",      "Palette.Ink.Muted", "Palette.Parchment.200", true),
        new("Text.OnLight.Muted / Node.Player.Footer",   "Palette.Ink.Muted", "Palette.Azure.250",     true),
        new("Text.OnLight.Muted / Node.Narrator.Footer", "Palette.Ink.Muted", "Palette.Teal.250",      true),
        new("Text.OnLight.Muted / Node.Script.Footer",   "Palette.Ink.Muted", "Palette.Neutral.785",   true),
        new("Text.OnLight.Muted / Node.Bark.Footer",     "Palette.Ink.Muted", "Palette.Amber.570",     true),
        new("Text.OnLight.Muted / Node.Npc.Body",        "Palette.Ink.Muted", "Palette.Parchment.100", true),
        new("Text.OnLight.Muted / Node.Script.Body",     "Palette.Ink.Muted", "Palette.Neutral.875",   true),
        new("Text.OnLight.Muted / Node.Bark.Body",       "Palette.Ink.Muted", "Palette.Cream.100",     true),
        // Conversations dock rows (issue 85): folders, conversations and new conversations on the
        // tree background.
        new("Text.Primary / Surface.Subtle",    "Palette.Neutral.910", "Palette.Neutral.175", true),
        new("Text.Secondary / Surface.Subtle",  "Palette.Neutral.800", "Palette.Neutral.175", true),
        new("Text.Status.New / Surface.Subtle", "Palette.Green.400",   "Palette.Neutral.175", true),
        // The "changed in this project" dot (issue 85, part C) is a non-text indicator.
        new("Text.Status.Changed / Surface.Subtle", "Palette.Amber.540", "Palette.Neutral.175", false),
        // Hint and empty-state text (issue 180) is active text, so it must read as normal text on
        // every surface it sits on. Text.Muted fails this on Surface.Panel in Dark and Light.
        new("Text.Hint / Surface.Window", "Palette.Neutral.600", "Palette.Neutral.115", true),
        new("Text.Hint / Surface.Panel", "Palette.Neutral.600", "Palette.Neutral.145", true),
        new("Text.Hint / Surface.Card", "Palette.Neutral.600", "Palette.Neutral.100", true),
        new("Text.Hint / Surface.Inset", "Palette.Neutral.600", "Palette.Neutral.125", true),
        new("Severity.Warning / Surface.Panel", "Palette.Amber.500", "Palette.Neutral.145", false),
        new("Severity.Error / Surface.Panel",   "Palette.Red.500",   "Palette.Neutral.145", false),
        new("Severity.Info / Surface.Panel",    "Palette.Sky.450",   "Palette.Neutral.145", false),
    };

    // Thresholds: {normalText, largeUI}.
    [AvaloniaTheory]
    [InlineData("Palette.Dark", 4.5, 3.0)]
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
        // canvas side (Brush.Canvas.Background → Palette.Canvas.Base), not just the dark panels —
        // so the divider must stay visible against the canvas too, or it vanishes into it.
        ("Palette.Line.Default", "Palette.Canvas.Base"), // GridSplitter on the canvas backdrop
    };

    // A highlighted connection (Brush.Connection.Highlighted = Palette.Connection.Highlight) must stand out
    // from a resting one (Brush.Connection.Default = Palette.Connection.Base). In HC they
    // collided at pure white (Neutral.665 = white for text), so the highlight was invisible
    // among default links — hence the dedicated Connection.* primitive, dimmed in HC.
    [AvaloniaFact]
    public void HighContrastHighlightedConnectionStandsOut()
    {
        var dict  = PaletteHarness.Load("Palette.HighContrast");
        var ratio = Wcag.ContrastRatio(
            PaletteHarness.Color(dict, "Palette.Connection.Highlight"),
            PaletteHarness.Color(dict, "Palette.Connection.Base"));
        Assert.True(ratio >= 1.8,
            $"highlighted vs resting connection in HC: {ratio:F2}:1 < 1.8:1");
    }

    // Issue 82: the canvas backdrop follows the theme. Resting connections must stay at least
    // 3:1 against it (non-text UI).
    [AvaloniaTheory]
    [InlineData("Palette.Dark", "#FF2B2B2B")]
    [InlineData("Palette.Light", "#FFE4E4E0")]
    [InlineData("Palette.HighContrast", "#FF3A3A3A")]
    [InlineData("Palette.Colourblind", "#FF2B2B2B")]
    public void CanvasBackdrop_HasTheSpecValue(string palette, string expected)
    {
        var dict = PaletteHarness.Load(palette);
        Assert.Equal(global::Avalonia.Media.Color.Parse(expected), PaletteHarness.Color(dict, "Palette.Canvas.Base"));
    }

    [AvaloniaTheory]
    [InlineData("Palette.Dark")]
    [InlineData("Palette.Light")]
    [InlineData("Palette.HighContrast")]
    [InlineData("Palette.Colourblind")]
    public void RestingConnection_IsVisibleOnTheCanvas(string palette)
    {
        var dict = PaletteHarness.Load(palette);
        var ratio = Wcag.ContrastRatio(PaletteHarness.Color(dict, "Palette.Connection.Base"),
                                       PaletteHarness.Color(dict, "Palette.Canvas.Base"));
        Assert.True(ratio >= 3.0, $"{palette}: resting connection on canvas {ratio:F2}:1 < 3:1");
    }

    // GitHub issue 244: the selected node's links were white on Light's pale canvas (~1.27:1),
    // so the branch being followed was the hardest line to see. Visible on every canvas, and
    // distinct from a resting link.
    [AvaloniaTheory]
    [InlineData("Palette.Dark")]
    [InlineData("Palette.Light")]
    [InlineData("Palette.HighContrast")]
    [InlineData("Palette.Colourblind")]
    public void HighlightedConnection_IsVisibleOnTheCanvas_AndDistinctFromResting(string palette)
    {
        var dict = PaletteHarness.Load(palette);
        var highlight = PaletteHarness.Color(dict, "Palette.Connection.Highlight");
        var onCanvas  = Wcag.ContrastRatio(highlight, PaletteHarness.Color(dict, "Palette.Canvas.Base"));
        var vsResting = Wcag.ContrastRatio(highlight, PaletteHarness.Color(dict, "Palette.Connection.Base"));
        Assert.True(onCanvas >= 3.0,  $"{palette}: highlighted link on canvas {onCanvas:F2}:1 < 3:1");
        Assert.True(vsResting >= 1.8, $"{palette}: highlighted vs resting link {vsResting:F2}:1 < 1.8:1");
    }

    // Spec §6: a Never link is the faintest link, but it must still be visible on the backdrop.
    [AvaloniaTheory]
    [InlineData("Palette.Dark")]
    [InlineData("Palette.Light")]
    [InlineData("Palette.HighContrast")]
    [InlineData("Palette.Colourblind")]
    public void NeverConnection_IsVisibleOnTheCanvas(string palette)
    {
        var dict = PaletteHarness.Load(palette);
        // Brush.Connection.Never is Palette.Neutral.335 in every palette (Tokens.axaml).
        var ratio = Wcag.ContrastRatio(PaletteHarness.Color(dict, "Palette.Neutral.335"),
                                       PaletteHarness.Color(dict, "Palette.Canvas.Base"));
        Assert.True(ratio >= 1.5, $"{palette}: Never link on canvas {ratio:F2}:1 < 1.5:1");
    }

    // Spec §7: the card outline (Brush.Node.Border, a translucent overlay in Dark/Light/Colourblind)
    // is composited over the NPC body and must separate the card from the canvas.
    [AvaloniaTheory]
    [InlineData("Palette.Dark", 1.2)]
    [InlineData("Palette.Light", 1.2)]
    [InlineData("Palette.Colourblind", 1.2)]
    [InlineData("Palette.HighContrast", 3.0)]
    public void NodeBorder_SeparatesTheCardFromTheCanvas(string palette, double min)
    {
        var dict = PaletteHarness.Load(palette);
        var border = PaletteHarness.Color(dict, "Palette.Node.Border");
        var body = PaletteHarness.Color(dict, "Palette.Parchment.100"); // Brush.Node.Npc.Body
        double Mix(byte over, byte under) => (over * border.A + under * (255 - border.A)) / 255.0;
        var composited = global::Avalonia.Media.Color.FromRgb(
            (byte)Math.Round(Mix(border.R, body.R)), (byte)Math.Round(Mix(border.G, body.G)), (byte)Math.Round(Mix(border.B, body.B)));
        var ratio = Wcag.ContrastRatio(composited, PaletteHarness.Color(dict, "Palette.Canvas.Base"));
        Assert.True(ratio >= min, $"{palette}: card border on canvas {ratio:F2}:1 < {min:F1}:1");
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
