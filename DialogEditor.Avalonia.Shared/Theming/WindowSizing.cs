using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Applies <see cref="WindowSizingRule"/> to every window (issue 177).
///
/// Hook: Window.WindowOpenedEvent. Avalonia's Window.ShowCore raises it as its very first step,
/// before it reads Width/Height/Min/Max into the initial client size and before startup
/// positioning. So a window is sized before it appears, and CenterOwner centres it at the new
/// size. (TemplateApplied, the spec's first idea, fires during the initial layout pass, after
/// the size has already been read.)
///
/// The XAML-authored size is captured the first time a window opens, as its design size.
/// Reopening re-derives from it, so the scale never compounds. Live scale changes rescale every
/// open window by new/old from its current size, which keeps your own resizing in proportion.
/// See docs/superpowers/specs/2026-10-13-window-sizing-design.md.
/// </summary>
public static class WindowSizing
{
    public static readonly AttachedProperty<WindowSizingMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<Window, WindowSizingMode>("Mode", typeof(WindowSizing));

    public static WindowSizingMode GetMode(Window w) => w.GetValue(ModeProperty);
    public static void SetMode(Window w, WindowSizingMode value) => w.SetValue(ModeProperty, value);

    /// <summary>The working area (DIPs) a window must fit, or null when no screen is known.
    /// Replaceable so tests can stand in a 1366×768 screen.</summary>
    public static Func<Window, Size?> WorkingAreaProvider { get; set; } = ScreenWorkingArea;

    private sealed class State
    {
        public required WindowSize Design;
        public double AppliedScale;
    }

    private static readonly ConditionalWeakTable<Window, State> States = new();
    private static readonly HashSet<Window> Open = new();
    private static bool _registered;

    public static double? GetAppliedScale(Window w) =>
        States.TryGetValue(w, out var s) ? s.AppliedScale : null;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        Window.WindowOpenedEvent.AddClassHandler(typeof(Window), (s, _) => OnOpening((Window)s!));
        Window.WindowClosedEvent.AddClassHandler(typeof(Window), (s, _) => Open.Remove((Window)s!));
        FontScaleService.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontScaleService.Scale)) RescaleOpenWindows();
        };
    }

    private static void OnOpening(Window w)
    {
        var scale = FontScaleService.Current.Scale;
        if (!States.TryGetValue(w, out var state))
        {
            state = new State { Design = WindowSize.Of(w) };
            States.Add(w, state);
        }
        WindowSizingRule.Compute(state.Design, scale, WorkingAreaProvider(w), GetMode(w), w.SizeToContent).ApplyTo(w);
        state.AppliedScale = scale;
        Open.Add(w);
    }

    private static void RescaleOpenWindows()
    {
        var scale = FontScaleService.Current.Scale;
        foreach (var w in Open.ToList())
        {
            if (!States.TryGetValue(w, out var state) || state.AppliedScale == scale) continue;
            WindowSizingRule.Rescale(WindowSize.Of(w), state.Design, state.AppliedScale, scale,
                                     WorkingAreaProvider(w), GetMode(w), w.SizeToContent).ApplyTo(w);
            state.AppliedScale = scale;
        }
    }

    // The owner is not assigned yet when WindowOpenedEvent fires, so use the main window's
    // screen (it owns nearly every dialog), else the primary screen.
    private static Size? ScreenWorkingArea(Window w)
    {
        var screens = w.Screens;
        if (screens is null) return null;
        Screen? screen = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
            && !ReferenceEquals(main, w) && main.IsVisible)
            screen = screens.ScreenFromWindow(main);
        screen ??= screens.Primary;
        return screen is null ? null : WindowSizingRule.WorkingAreaInDips(screen.WorkingArea.Size, screen.Scaling);
    }
}
