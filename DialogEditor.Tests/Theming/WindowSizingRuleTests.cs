using Avalonia;
using Avalonia.Controls;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

/// <summary>Issue 177: the pure rule that sizes windows from their design size, the font
/// scale and the screen. See docs/superpowers/specs/2026-10-13-window-sizing-design.md.</summary>
public class WindowSizingRuleTests
{
    private static readonly Size Small = new(1366, 768);   // limits: 1334 × 736
    private static WindowSize Design(double w = 420, double h = double.NaN, double minW = 420,
        double minH = 0, double maxW = double.PositiveInfinity, double maxH = double.PositiveInfinity)
        => new(w, h, minW, minH, maxW, maxH);

    [Fact]
    public void Compute_AtOne_WithoutScreen_IsIdentity()
        => Assert.Equal(Design(), WindowSizingRule.Compute(Design(), 1.0, null, WindowSizingMode.Scale));

    [Fact]
    public void Compute_AtTwo_DoublesFiniteDimensions()
    {
        var s = WindowSizingRule.Compute(Design(w: 420, h: 300, minW: 400, minH: 200, maxW: 500, maxH: 520),
                                         2.0, null, WindowSizingMode.Scale);
        Assert.Equal(new WindowSize(840, 600, 800, 400, 1000, 1040), s);
    }

    [Fact]
    public void Compute_KeepsNaNZeroAndInfinity()
    {
        var s = WindowSizingRule.Compute(Design(h: double.NaN, minH: 0), 2.0, null, WindowSizingMode.Scale);
        Assert.True(double.IsNaN(s.Height));
        Assert.Equal(0, s.MinHeight);
        Assert.True(double.IsPositiveInfinity(s.MaxWidth));
    }

    [Fact]
    public void Compute_ClampsSizesAndMinimumsToTheWorkingAreaLessMargins()
    {
        var s = WindowSizingRule.Compute(Design(w: 820, h: 620, minW: 700, minH: 400), 2.0, Small, WindowSizingMode.Scale);
        Assert.Equal(new WindowSize(1334, 736, 1334, 736, double.PositiveInfinity, double.PositiveInfinity), s);
    }

    // Review finding: a finite MaxWidth/MaxHeight becomes the OS's maximum track size, so capping
    // the maximums at the screen stopped a maximized window filling the screen, and stopped any
    // window growing on a larger second monitor. Maximums stay as authored unless the window sizes
    // itself to its content on that axis.
    [Fact]
    public void Compute_LeavesMaximumsUncapped_ForAManuallySizedWindow()
    {
        var s = WindowSizingRule.Compute(Design(w: 820, h: 620), 2.0, Small, WindowSizingMode.Scale, SizeToContent.Manual);
        Assert.True(double.IsPositiveInfinity(s.MaxWidth));
        Assert.True(double.IsPositiveInfinity(s.MaxHeight));
    }

    [Fact]
    public void Compute_CapsTheMaximum_OnlyOnASizeToContentAxis()
    {
        var s = WindowSizingRule.Compute(Design(w: 500, minW: 500, minH: 420), 2.0, Small, WindowSizingMode.Scale, SizeToContent.Height);
        Assert.Equal(736, s.MaxHeight);
        Assert.True(double.IsPositiveInfinity(s.MaxWidth));
    }

    [Fact]
    public void Compute_KeepsAnAuthoredMaximumBelowTheScreen()
    {
        var s = WindowSizingRule.Compute(Design(maxH: 520), 1.0, Small, WindowSizingMode.Scale, SizeToContent.Height);
        Assert.Equal(520, s.MaxHeight);
    }

    [Fact]
    public void Compute_ClampsMinimumsSoTheyCannotForceAnOversizeWindow()
    {
        var s = WindowSizingRule.Compute(Design(w: 500, minW: 500, minH: 420), 2.0, Small, WindowSizingMode.Scale);
        Assert.Equal(1000, s.Width);
        Assert.Equal(1000, s.MinWidth);
        Assert.Equal(736, s.MinHeight);
    }

    [Fact]
    public void Compute_ClampOnly_DoesNotScaleButStillClamps()
    {
        var s = WindowSizingRule.Compute(Design(w: 1400, h: 850, minW: 0), 2.0, Small, WindowSizingMode.ClampOnly);
        Assert.Equal(new WindowSize(1334, 736, 0, 0, double.PositiveInfinity, double.PositiveInfinity), s);
    }

    [Fact]
    public void Compute_ClampOnly_NeverCapsTheMaximum()
    {
        var s = WindowSizingRule.Compute(Design(w: 1400, h: double.NaN, minW: 0), 1.0, Small, WindowSizingMode.ClampOnly, SizeToContent.Height);
        Assert.True(double.IsPositiveInfinity(s.MaxHeight));
    }

    [Fact]
    public void Compute_WithoutScreen_ScalesButDoesNotClamp()
    {
        var s = WindowSizingRule.Compute(Design(w: 1400, minW: 0), 2.0, null, WindowSizingMode.Scale);
        Assert.Equal(2800, s.Width);
        Assert.True(double.IsPositiveInfinity(s.MaxWidth));
    }

    [Fact]
    public void Rescale_KeepsUserResizeInProportion()
    {
        var design  = Design(w: 420, minW: 420);
        var current = design with { Width = 600 };            // you widened it at 1.0
        var s = WindowSizingRule.Rescale(current, design, 1.0, 2.0, null, WindowSizingMode.Scale);
        Assert.Equal(1200, s.Width);
        Assert.Equal(840, s.MinWidth);
    }

    [Fact]
    public void Rescale_ClampsToScreen()
    {
        var design  = Design(w: 420, minW: 420);
        var current = design with { Width = 900 };
        var s = WindowSizingRule.Rescale(current, design, 1.0, 2.0, Small, WindowSizingMode.Scale);
        Assert.Equal(1334, s.Width);
    }

    [Fact]
    public void Rescale_BackDown_RestoresTheDesignMinimum()
    {
        var design  = Design(w: 420, minW: 420);
        var current = design with { Width = 840, MinWidth = 840 };
        var s = WindowSizingRule.Rescale(current, design, 2.0, 1.0, null, WindowSizingMode.Scale);
        Assert.Equal(420, s.Width);
        Assert.Equal(420, s.MinWidth);
    }

    [Fact]
    public void Rescale_KeepsAutoHeightAuto()
    {
        var design = Design(h: double.NaN);
        var s = WindowSizingRule.Rescale(design, design, 1.0, 2.0, Small, WindowSizingMode.Scale);
        Assert.True(double.IsNaN(s.Height));
    }

    [Fact]
    public void WorkingAreaInDips_DividesByScaling()
        => Assert.Equal(new Size(1366, 768), WindowSizingRule.WorkingAreaInDips(new PixelSize(2732, 1536), 2.0));

    // Review finding: a window moved to another monitor was rescaled against the main window's
    // screen. Prefer the window's own screen once it is shown, then its owner's, then the main
    // window's, then the primary.
    [Fact]
    public void PickScreen_PrefersTheWindowsOwnScreen() =>
        Assert.Equal("own", WindowSizingRule.PickScreen("own", "owner", "main", "primary"));

    [Fact]
    public void PickScreen_FallsBackToOwnerThenMainThenPrimary()
    {
        Assert.Equal("owner",   WindowSizingRule.PickScreen(null, "owner", "main", "primary"));
        Assert.Equal("main",    WindowSizingRule.PickScreen<string>(null, null, "main", "primary"));
        Assert.Equal("primary", WindowSizingRule.PickScreen<string>(null, null, null, "primary"));
        Assert.Null(WindowSizingRule.PickScreen<string>(null, null, null, null));
    }

    // Found in the final GUI check: Legend (default placement, or a position saved at a smaller
    // scale) opened at y=265 with a 1048 px height on a 1080 px screen, so its bottom third was
    // off-screen. A window that fits the screen is moved back inside it.
    private static readonly PixelRect Screen1080 = new(0, 0, 1920, 1080);

    [Fact]
    public void FitPosition_MovesAWindowUpWhenItsBottomIsOffScreen() =>
        Assert.Equal(new PixelPoint(242, 16), WindowSizingRule.FitPosition(new PixelRect(242, 265, 720, 1048), Screen1080, 16));

    [Fact]
    public void FitPosition_MovesAWindowLeftWhenItsRightEdgeIsOffScreen() =>
        Assert.Equal(new PixelPoint(1104, 100), WindowSizingRule.FitPosition(new PixelRect(1500, 100, 800, 400), Screen1080, 16));

    [Fact]
    public void FitPosition_LeavesAWindowThatAlreadyFitsAlone() =>
        Assert.Equal(new PixelPoint(5, 5), WindowSizingRule.FitPosition(new PixelRect(5, 5, 400, 300), Screen1080, 16));

    [Fact]
    public void FitPosition_KeepsTheTopLeftOnScreenWhenTheWindowIsTooBig() =>
        Assert.Equal(new PixelPoint(16, 16), WindowSizingRule.FitPosition(new PixelRect(300, 300, 2400, 1400), Screen1080, 16));

    [Fact]
    public void FitPosition_PullsAWindowBackFromAboveOrLeftOfTheScreen() =>
        Assert.Equal(new PixelPoint(16, 16), WindowSizingRule.FitPosition(new PixelRect(-200, -50, 400, 300), Screen1080, 16));

    // Found in the final GUI check: the clamp limited the CLIENT area to the working area, but the
    // title bar and borders sit outside it (39 px tall on Windows 11), so a clamped window's frame
    // still ran 23 px off a 1080 px screen. Client sizes are clamped against the working area
    // less the window's chrome.
    [Fact]
    public void ClientArea_SubtractsTheWindowChrome() =>
        Assert.Equal(new Size(1904, 1041), WindowSizingRule.ClientArea(new Size(1920, 1080), new Size(16, 39)));

    [Fact]
    public void ClientArea_NeverGoesNegative() =>
        Assert.Equal(new Size(0, 0), WindowSizingRule.ClientArea(new Size(10, 10), new Size(16, 39)));
}
