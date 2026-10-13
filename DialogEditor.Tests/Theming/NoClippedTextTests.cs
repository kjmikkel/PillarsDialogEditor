using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Guard for issue 177's done-when: on a 1366×768 screen, at 1.0× and 2.0×, no window clips
/// text. Each window type with a parameterless constructor is opened and every visible
/// TextBlock, including those inside control templates, is checked:
///  1. single-line text without an ellipsis must fit its own box (a fixed grid column or a
///     fixed-size control squeezes it, and the rest is cut off or spills over a neighbour);
///  2. the box must lie inside the nearest viewport that does not scroll in that direction:
///     the window itself, or a ScrollViewer with scrolling disabled on that axis.
/// Needs the Skia-backed test host (HeadlessTextMeasurementTests).
/// Main windows are excluded: their panes are covered by issues 82 and 85.
/// </summary>
public class NoClippedTextTests
{
    public NoClippedTextTests() => Loc.Configure(new StubStringProvider());

    // Known clipping that belongs to issue 178 (grid columns that overlap). Remove each entry
    // when 178 fixes it. Format: "<window type name>|<text>".
    private static readonly HashSet<string> TrackedByIssue178 =
    [
        "RepDispositionBalanceWindow|Count",   // Count/Share column headers
        "FindInProjectWindow|Node",            // results table headers
        "FindInProjectWindow|Language",
        "BlameWindow|Node",                    // attribution table header
        "BatchVoImportDialog|Status",          // file table headers (same layout as the 178 tables)
        "BatchVoImportDialog|Result",
    ];

    private static readonly Assembly[] Apps =
    [
        typeof(DialogEditor.Avalonia.App).Assembly,
        typeof(DialogEditor.Avalonia.Shared.ThemeOnboardingWindow).Assembly,
        typeof(DialogEditor.PatchManager.App).Assembly,
    ];

    public static IEnumerable<object[]> Cases() =>
        from t in Apps.SelectMany(a => a.GetTypes())
        where typeof(Window).IsAssignableFrom(t) && !t.IsAbstract
              && t.GetConstructor(Type.EmptyTypes) is not null && t.Name != "MainWindow"
        orderby t.FullName
        from scale in new[] { 1.0, 2.0 }
        select new object[] { t.AssemblyQualifiedName!, scale };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Window_ClipsNoText(string typeName, double scale)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => new Size(1366, 768);
        var w = (Window)Activator.CreateInstance(Type.GetType(typeName)!)!;
        try
        {
            new FontScaleApplier().Apply(scale);
            w.Show();
            Dispatcher.UIThread.RunJobs();   // let SizeToContent and the clamp settle (see TallWindowsFitTests)
            var clipped = Clipped(w).Where(c => !TrackedByIssue178.Contains($"{w.GetType().Name}|{c.Text}")).ToList();
            Assert.True(clipped.Count == 0,
                $"{w.GetType().Name} at {scale}x clips:\n" + string.Join("\n", clipped.Select(c => $"  \"{c.Text}\": {c.Why}")));
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }

    private static IEnumerable<(string Text, string Why)> Clipped(Window w)
    {
        foreach (var tb in w.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!tb.IsEffectivelyVisible || tb.Bounds.Width <= 0) continue;
            var layout = tb.TextLayout;
            var text = tb.Text ?? string.Concat(tb.Inlines?.OfType<global::Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? []);
            if (string.IsNullOrWhiteSpace(text)) continue;

            // 1. Single-line, untrimmed text must fit its own box.
            if (tb.TextWrapping == TextWrapping.NoWrap && tb.TextTrimming == TextTrimming.None)
            {
                var needed = layout.WidthIncludingTrailingWhitespace + tb.Padding.Left + tb.Padding.Right;
                if (needed > tb.Bounds.Width + 0.5)
                    yield return (text, $"needs {needed:0} px, box is {tb.Bounds.Width:0}");
            }

            // 2. The box must lie inside the nearest non-scrolling viewport on each axis.
            var (viewport, clipX, clipY) = NearestViewport(tb, w);
            if (tb.TransformToVisual(viewport) is not { } m) continue;
            var r = new Rect(tb.Bounds.Size).TransformToAABB(m);
            if (clipX && (r.X < -0.5 || r.Right > viewport.Bounds.Width + 0.5))
                yield return (text, $"spans x {r.X:0}–{r.Right:0}, viewport is {viewport.Bounds.Width:0} wide");
            if (clipY && (r.Y < -0.5 || r.Bottom > viewport.Bounds.Height + 0.5))
                yield return (text, $"spans y {r.Y:0}–{r.Bottom:0}, viewport is {viewport.Bounds.Height:0} tall");
        }
    }

    // The nearest ScrollContentPresenter ancestor clips its content; on an axis its ScrollViewer
    // can scroll, overflow is reachable and therefore fine. Without one, the window is the viewport.
    private static (Visual Viewport, bool ClipX, bool ClipY) NearestViewport(Visual v, Window w)
    {
        foreach (var a in v.GetVisualAncestors())
            if (a is ScrollContentPresenter scp)
                return (scp, !scp.CanHorizontallyScroll, !scp.CanVerticallyScroll);
        return (w, true, true);
    }
}
