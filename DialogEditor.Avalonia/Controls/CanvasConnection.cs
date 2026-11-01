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
