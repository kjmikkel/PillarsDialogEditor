using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

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
        public bool KeepsOnScreen;
        public bool RefittedForChrome;
    }

    private static readonly ConditionalWeakTable<Window, State> States = new();
    private static readonly HashSet<Window> Open = new();
    private static bool _registered;

    // The size of a window's chrome (title bar + borders) in DIPs, learned from the first window
    // that opens (normally the main window) — every window here uses the same system chrome.
    // Unknown until then, which only the first window pays for (see KeepOnScreen).
    private static Size _chrome;

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
        if (!state.KeepsOnScreen)
        {
            // Opened fires once the window is placed, but its frame size (title bar included) is
            // only settled after the first layout, so the check waits for the dispatcher.
            w.Opened += (_, _) => Dispatcher.UIThread.Post(() => KeepOnScreen(w), DispatcherPriority.Background);
            state.KeepsOnScreen = true;
        }
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
            // A window that grew may now stick out below or to the right; move it once the
            // resize has been laid out.
            Dispatcher.UIThread.Post(() => KeepOnScreen(w), DispatcherPriority.Background);
        }
    }

    // Keeps a shown, normal-state window inside its screen's working area (issue 177).
    // Maximized and minimized windows are placed by the OS and left alone.
    private static void KeepOnScreen(Window w)
    {
        if (!w.IsVisible || w.WindowState != WindowState.Normal || w.Screens is not { } screens) return;
        var screen = screens.ScreenFromWindow(w) ?? screens.Primary;
        if (screen is null) return;

        var frame = w.FrameSize ?? w.ClientSize;
        var chrome = new Size(Math.Max(0, frame.Width - w.ClientSize.Width), Math.Max(0, frame.Height - w.ClientSize.Height));
        if (chrome != default) _chrome = chrome;

        // A window opened before the chrome was known was sized against the whole working area;
        // size it once more now that its chrome is known, then place it on the next pass.
        var area = WindowSizingRule.WorkingAreaInDips(screen.WorkingArea.Size, screen.Scaling);
        if (States.TryGetValue(w, out var state) && !state.RefittedForChrome)
        {
            state.RefittedForChrome = true;
            var limit = WindowSizingRule.ClientArea(area, chrome);
            if (frame.Width > area.Width - 2 * WindowSizingRule.ScreenMargin ||
                frame.Height > area.Height - 2 * WindowSizingRule.ScreenMargin)
            {
                WindowSizingRule.Rescale(WindowSize.Of(w), state.Design, state.AppliedScale, state.AppliedScale,
                                         limit, GetMode(w), w.SizeToContent).ApplyTo(w);
                Dispatcher.UIThread.Post(() => KeepOnScreen(w), DispatcherPriority.Background);
                return;
            }
        }

        var size   = PixelSize.FromSize(frame, screen.Scaling);
        var margin = (int)Math.Round(WindowSizingRule.ScreenMargin * screen.Scaling);
        var target = WindowSizingRule.FitPosition(new PixelRect(w.Position, size), screen.WorkingArea, margin);
        if (target != w.Position) w.Position = target;
    }

    // At open the window has no position and its owner is not assigned yet (WindowOpenedEvent
    // fires first), so it falls back to the main window's screen, which owns nearly every
    // dialog. On a live rescale it is shown, so its own screen counts.
    private static Size? ScreenWorkingArea(Window w)
    {
        var screens = w.Screens;
        if (screens is null) return null;
        Screen? ScreenOf(Window? x) => x is { IsVisible: true } ? screens.ScreenFromWindow(x) : null;
        var main = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } m }
                   && !ReferenceEquals(m, w) ? m : null;
        var screen = WindowSizingRule.PickScreen(ScreenOf(w), ScreenOf(w.Owner as Window), ScreenOf(main), screens.Primary);
        return screen is null ? null
            : WindowSizingRule.ClientArea(WindowSizingRule.WorkingAreaInDips(screen.WorkingArea.Size, screen.Scaling), _chrome);
    }
}
