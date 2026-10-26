using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DialogEditor.Avalonia.Behaviors;
using DialogEditor.Avalonia.Controls;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

/// GitHub issue 9: wavy underlines under misspelt words in Node Details' text boxes.
public class SpellSquigglesBehaviorTests
{
    // A stand-in checker: every occurrence of "captian" is misspelt.
    private static IReadOnlyList<SpellSpan> Captian(string text)
    {
        var spans = new List<SpellSpan>();
        for (var i = text.IndexOf("captian", StringComparison.Ordinal); i >= 0;
             i = text.IndexOf("captian", i + 1, StringComparison.Ordinal))
            spans.Add(new SpellSpan(i, "captian".Length));
        return spans;
    }

    private static TextBox Setup(string text, Func<string, IReadOnlyList<SpellSpan>>? finder)
    {
        var tb = new TextBox { Text = text, AcceptsReturn = true, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        SpellSquiggles.SetFinder(tb, finder);
        var window = new Window { Width = 600, Height = 200, Content = tb };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return tb;
    }

    [AvaloniaFact]
    public void MisspeltWord_GetsOneSquiggleUnderIt()
    {
        var tb = Setup("captian glows", Captian);
        var rect = Assert.Single(SpellSquiggles.GetSquiggleRects(tb));
        Assert.True(rect.Width > 0);
        Assert.True(rect.Height > 0);
    }

    [AvaloniaFact]
    public void TwoOccurrences_TwoSquiggles_LeftToRight()
    {
        var rects = SpellSquiggles.GetSquiggleRects(Setup("captian ship captian", Captian));
        Assert.Equal(2, rects.Count);
        Assert.True(rects[1].X > rects[0].Right);
    }

    [AvaloniaFact]
    public void FixingTheText_RemovesTheSquiggle()
    {
        var tb = Setup("captian glows", Captian);
        tb.Text = "captain glows";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(SpellSquiggles.GetSquiggleRects(tb));
    }

    [AvaloniaFact]
    public void TypingAMisspelling_AddsASquiggle()
    {
        var tb = Setup("glows", Captian);
        Assert.Empty(SpellSquiggles.GetSquiggleRects(tb));
        tb.Text = "glows captian";
        Dispatcher.UIThread.RunJobs();
        Assert.Single(SpellSquiggles.GetSquiggleRects(tb));
    }

    [AvaloniaFact]
    public void NoFinder_NoSquiggles()
        => Assert.Empty(SpellSquiggles.GetSquiggleRects(Setup("captian glows", null)));

    [AvaloniaFact]
    public void NewFinder_Rechecks()
    {
        // The view model hands over a new finder when the dictionary changes.
        var tb = Setup("captian glows", Captian);
        SpellSquiggles.SetFinder(tb, _ => []);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(SpellSquiggles.GetSquiggleRects(tb));
    }
}

public class SquiggleGeometryTests
{
    [Fact]
    public void Wave_SpansTheWord_AndStaysInItsBand()
    {
        var pts = SquiggleGeometry.Wave(left: 10, right: 50, bottom: 20, amplitude: 2, period: 4);
        Assert.Equal(10, pts[0].X, 3);
        Assert.Equal(50, pts[^1].X, 3);
        Assert.All(pts, p => Assert.InRange(p.Y, 18, 20));
        Assert.Contains(pts, p => p.Y <= 18.001);   // it actually waves
        Assert.Contains(pts, p => p.Y >= 19.999);
    }

    [Fact]
    public void Wave_XIncreases()
    {
        var pts = SquiggleGeometry.Wave(0, 13, 5, 1.5, 4);
        for (var i = 1; i < pts.Count; i++) Assert.True(pts[i].X > pts[i - 1].X);
    }

    [Fact]
    public void Wave_ZeroWidth_IsEmpty()
        => Assert.Empty(SquiggleGeometry.Wave(10, 10, 5, 2, 4));
}
