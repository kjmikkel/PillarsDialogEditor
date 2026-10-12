using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

/// <summary>Issue 177: windows are sized from their design size and the font scale when they
/// open, and resized live when the scale changes. Every test restores scale 1.0 and the
/// working-area provider.</summary>
public class WindowSizingTests
{
    private static void Run(Size? workingArea, Action body)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => workingArea;
        try { body(); }
        finally
        {
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }

    private static Window Dialog() =>
        new() { Width = 300, MinWidth = 200, SizeToContent = SizeToContent.Height, Content = new TextBlock { Text = "x" } };

    [AvaloniaFact]
    public void OpenedAtDoubleScale_IsSizedBeforeAvaloniaReadsItsSize() => Run(null, () =>
    {
        new FontScaleApplier().Apply(2.0);
        var w = Dialog();
        w.Show();
        Assert.Equal(400, w.MinWidth);
        Assert.Equal(600, w.ClientSize.Width);   // the initial client size came from the scaled Width
        Assert.Equal(2.0, WindowSizing.GetAppliedScale(w));
        w.Close();
    });

    [AvaloniaFact]
    public void ClampOnlyWindow_IsClampedNotScaled() => Run(new Size(1366, 768), () =>
    {
        new FontScaleApplier().Apply(2.0);
        var w = new Window { Width = 1400, Height = 850 };
        WindowSizing.SetMode(w, WindowSizingMode.ClampOnly);
        w.Show();
        Assert.Equal(1334, w.ClientSize.Width);
        Assert.Equal(736, w.ClientSize.Height);
        w.Close();
    });

    [AvaloniaFact]
    public void OpenWindow_ResizesLiveWhenTheScaleChanges() => Run(null, () =>
    {
        var w = Dialog();
        w.Show();
        new FontScaleApplier().Apply(2.0);
        Assert.Equal(400, w.MinWidth);
        Assert.Equal(600, w.Width);
        new FontScaleApplier().Apply(1.0);
        Assert.Equal(200, w.MinWidth);
        Assert.Equal(300, w.Width);
        w.Close();
    });

    [AvaloniaFact]
    public void ReopenedWindow_DoesNotCompoundScale() => Run(null, () =>
    {
        new FontScaleApplier().Apply(2.0);
        var w = Dialog();
        w.Show();
        w.Hide();
        w.Show();
        Assert.Equal(400, w.MinWidth);
        w.Close();
    });

    [AvaloniaFact]
    public void ClosedWindow_IsNotRescaled() => Run(null, () =>
    {
        var w = Dialog();
        w.Show();
        w.Close();
        new FontScaleApplier().Apply(2.0);   // must not throw or touch the closed window
        Assert.Equal(200, w.MinWidth);
    });

    [AvaloniaFact]
    public void EditorMainWindow_IsClampOnly() =>
        Assert.Equal(WindowSizingMode.ClampOnly, WindowSizing.GetMode(new DialogEditor.Avalonia.Views.MainWindow()));

    [AvaloniaFact]
    public void PatchManagerMainWindow_IsClampOnly() =>
        Assert.Equal(WindowSizingMode.ClampOnly, WindowSizing.GetMode(new DialogEditor.PatchManager.MainWindow()));
}
