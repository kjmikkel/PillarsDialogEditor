using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DialogEditor.Avalonia.Controls;

/// <summary>
/// A <see cref="Nodify.Connection"/> that opens its context menu on right-click (issue 359).
/// Nodify's <c>BaseConnection.OnPointerReleased</c> overrides Avalonia's
/// <c>Control.OnPointerReleased</c> without calling base, and that base method is what raises
/// <see cref="Control.ContextRequestedEvent"/> after a right-button release. So a stock
/// connection never raises ContextRequested and its ContextMenu never opens.
/// </summary>
public class CanvasConnection : Nodify.Connection
{
    /// <summary>
    /// Keep resolving the stock Nodify.Connection theme and its style selectors: without this the
    /// subclass would match none of them (same trap as CanvasEditor).
    /// </summary>
    protected override Type StyleKeyOverride => typeof(Nodify.Connection);

    /// <summary>
    /// Issue 361: a link must be reachable from the keyboard and by UI Automation. Focusable so
    /// L / Shift+L can put focus on it (then the Menu key opens its menu), but not a tab stop, so
    /// the editor stays the canvas's single Tab stop (same pattern as the node containers). A
    /// control peer makes it a control element with a name, so a screen reader announces it.
    /// </summary>
    public CanvasConnection()
    {
        Focusable = true;
        IsTabStop = false;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // Nodify's handler releases its pointer capture; it must still run.
        base.OnPointerReleased(e);

        // The same condition and raise as Avalonia's Control.OnPointerReleased.
        if (e.Source == this && !e.Handled && e.InitialPressMouseButton == MouseButton.Right)
        {
            var args = new ContextRequestedEventArgs(e);
            RaiseEvent(args);
            e.Handled = args.Handled;
        }
    }
}
