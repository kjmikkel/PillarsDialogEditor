using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DialogEditor.Avalonia.Shared;

namespace DialogEditor.Tests.Controls;

/// <summary>
/// GitHub issue 260: EscapeCloses lets a tool window close on Esc, as its close box does.
/// A control that uses Esc itself (an open dropdown, a completion popup) marks the key
/// handled, and the window must then stay open.
/// </summary>
public class EscapeClosesTests
{
    private static (Window Window, TextBox Box) Open(bool enabled)
    {
        var box = new TextBox();
        var window = new Window { Content = box };
        EscapeCloses.SetIsEnabled(window, enabled);
        window.Show();
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, box);
    }

    [AvaloniaFact]
    public void Escape_ClosesTheWindow()
    {
        var (window, _) = Open(enabled: true);
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed);
    }

    [AvaloniaFact]
    public void NotEnabled_EscapeDoesNothing()
    {
        var (window, _) = Open(enabled: false);
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(closed);
        window.Close();
    }

    [AvaloniaFact]
    public void EscapeAlreadyHandled_LeavesTheWindowOpen()
    {
        var (window, box) = Open(enabled: true);
        box.KeyDown += (_, e) => { if (e.Key == Key.Escape) e.Handled = true; };
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(closed);
        window.Close();
    }

    [AvaloniaFact]
    public void EscapeWithAModifier_LeavesTheWindowOpen()
    {
        var (window, _) = Open(enabled: true);
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.KeyPress(Key.Escape, RawInputModifiers.Shift, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(closed);
        window.Close();
    }
}
