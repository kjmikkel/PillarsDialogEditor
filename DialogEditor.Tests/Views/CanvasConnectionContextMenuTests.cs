using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DialogEditor.Avalonia.Controls;
using Xunit;

namespace DialogEditor.Tests.Views;

/// Issue 359: right-clicking a link on the canvas must open its menu. Nodify's Connection overrides
/// OnPointerReleased without calling base, so Avalonia never raises ContextRequested for it;
/// CanvasConnection restores that. These tests drive real pointer input, because opening the menu
/// programmatically (as the older tests do) is exactly what hid the defect.
public class CanvasConnectionContextMenuTests
{
    private static (Window window, Nodify.Connection connection) Host(Nodify.Connection connection)
    {
        connection.Source = new Point(20, 50);
        connection.Target = new Point(220, 50);
        connection.ContextMenu = new ContextMenu { Items = { new MenuItem { Header = "Item" } } };
        var window = new Window { Content = new Canvas { Children = { connection } }, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, connection);
    }

    private static bool RightClickOpensMenu(Nodify.Connection connection)
    {
        var (window, c) = Host(connection);
        try
        {
            var spot = new Point(120, 50);
            window.MouseDown(spot, MouseButton.Right);
            window.MouseUp(spot, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            return c.ContextMenu!.IsOpen;
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CanvasConnection_RightClickOnLine_OpensItsContextMenu() =>
        Assert.True(RightClickOpensMenu(new CanvasConnection()));

    [AvaloniaFact]
    public void NodifyConnection_DoesNotRaiseContextRequested_ReasonCanvasConnectionExists() =>
        // If a Nodify upgrade makes this fail, Connection now calls base.OnPointerReleased and
        // CanvasConnection (and this test) can be retired.
        Assert.False(RightClickOpensMenu(new Nodify.Connection()));
}
