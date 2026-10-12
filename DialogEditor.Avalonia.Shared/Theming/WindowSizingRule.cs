using Avalonia;
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>The six size values of a window. NaN means "auto" (SizeToContent), as in Avalonia.</summary>
public readonly record struct WindowSize(
    double Width, double Height,
    double MinWidth, double MinHeight,
    double MaxWidth, double MaxHeight)
{
    public static WindowSize Of(Window w) =>
        new(w.Width, w.Height, w.MinWidth, w.MinHeight, w.MaxWidth, w.MaxHeight);

    // Limits first, so Width/Height are never briefly outside them.
    public void ApplyTo(Window w)
    {
        w.MinWidth = MinWidth;   w.MinHeight = MinHeight;
        w.MaxWidth = MaxWidth;   w.MaxHeight = MaxHeight;
        w.Width    = Width;      w.Height    = Height;
    }
}

/// <summary>Scale: window geometry follows the font scale. ClampOnly: only kept on screen
/// (main windows, whose panes already resize).</summary>
public enum WindowSizingMode { Scale, ClampOnly }

/// <summary>
/// The pure sizing rule behind issue 177. The font scale changes text size but not layout
/// units, so a Width="420" dialog stays 420 while its text doubles and its buttons are cut
/// off. This multiplies a window's XAML design size by the scale, then clamps every dimension,
/// minimums included, to the screen's working area. Clamping only Width would not be enough:
/// Avalonia lets MinWidth win over MaxWidth, so a doubled MinWidth could still push a window
/// off a 1366×768 screen. See docs/superpowers/specs/2026-10-13-window-sizing-design.md.
/// </summary>
public static class WindowSizingRule
{
    /// <summary>DIPs kept free between a clamped window and each screen edge.</summary>
    public const double ScreenMargin = 16;

    public static WindowSize Compute(WindowSize design, double scale, Size? workingArea, WindowSizingMode mode)
    {
        var k = mode == WindowSizingMode.Scale ? scale : 1.0;
        // NaN, 0 and Infinity survive multiplication by a positive k, which is what we want.
        var scaled = new WindowSize(
            design.Width * k, design.Height * k,
            design.MinWidth * k, design.MinHeight * k,
            design.MaxWidth * k, design.MaxHeight * k);
        return Clamp(scaled, workingArea);
    }

    /// <summary>
    /// Live rescale of an open window. Width and Height scale by newScale/oldScale from their
    /// CURRENT values, so a window you resized keeps that size in proportion. The limits are
    /// recomputed from the design size, so they never drift.
    /// </summary>
    public static WindowSize Rescale(WindowSize current, WindowSize design, double oldScale, double newScale,
                                     Size? workingArea, WindowSizingMode mode)
    {
        var ratio  = mode == WindowSizingMode.Scale ? newScale / oldScale : 1.0;
        var limits = Compute(design, newScale, workingArea, mode);
        return limits with
        {
            Width  = Fit(current.Width  * ratio, limits.MinWidth,  limits.MaxWidth),
            Height = Fit(current.Height * ratio, limits.MinHeight, limits.MaxHeight),
        };
    }

    /// <summary>Screen.WorkingArea is in physical pixels; window sizes are in DIPs.</summary>
    public static Size WorkingAreaInDips(PixelSize workingArea, double scaling) =>
        workingArea.ToSize(scaling);

    private static WindowSize Clamp(WindowSize s, Size? workingArea)
    {
        if (workingArea is not { } area) return s;
        var maxW = Math.Max(0, area.Width  - 2 * ScreenMargin);
        var maxH = Math.Max(0, area.Height - 2 * ScreenMargin);
        return new WindowSize(
            Cap(s.Width, maxW), Cap(s.Height, maxH),
            Math.Min(s.MinWidth, maxW), Math.Min(s.MinHeight, maxH),
            Math.Min(s.MaxWidth, maxW), Math.Min(s.MaxHeight, maxH));
    }

    private static double Cap(double v, double limit) => double.IsNaN(v) ? v : Math.Min(v, limit);

    private static double Fit(double v, double min, double max) =>
        double.IsNaN(v) ? v : Math.Max(min, Math.Min(v, max));
}
