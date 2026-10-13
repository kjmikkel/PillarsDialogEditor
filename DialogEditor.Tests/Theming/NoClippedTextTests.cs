using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
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
    public void Window_ClipsNoText(string typeName, double scale) =>
        AssertClipsNoText(() => (Window)Activator.CreateInstance(Type.GetType(typeName)!)!, scale);

    // Windows whose parameterless form is empty hide the rows that clip in real use, so these are
    // opened with representative content too.
    private static readonly Dictionary<string, Func<Window>> Populated = new()
    {
        // One condition row: its And/Or combo sat in a fixed 70 px column and showed "An" at 2.0x.
        ["ConditionEditor"] = () => new ConditionEditorWindow(new ConditionEditorViewModel(
            "canonical",
            [new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)", ["flag", "EqualTo", "1"], false, "And")],
            _ => { })),
    };

    public static IEnumerable<object[]> PopulatedCases() =>
        from name in Populated.Keys
        from scale in new[] { 1.0, 2.0 }
        select new object[] { name, scale };

    [AvaloniaTheory]
    [MemberData(nameof(PopulatedCases))]
    public void PopulatedWindow_ClipsNoText(string name, double scale) =>
        AssertClipsNoText(Populated[name], scale);

    private static void AssertClipsNoText(Func<Window> create, double scale)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => new Size(1366, 768);
        var w = create();
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

            // 2. The box must lie inside every ancestor that clips it: the window, a ScrollViewer's
            //    viewport, or any element with ClipToBounds (a ComboBox's content presenter, for
            //    one, cuts "And" to "An" without squeezing the TextBlock itself). Once the walk
            //    passes a viewport that scrolls on an axis, overflow on that axis is reachable by
            //    scrolling and is no longer checked further up.
            var (clipX, clipY) = (true, true);
            foreach (var a in tb.GetVisualAncestors())
            {
                var scrolls = a as ScrollContentPresenter;
                if (scrolls is not null)
                {
                    clipX &= !scrolls.CanHorizontallyScroll;
                    clipY &= !scrolls.CanVerticallyScroll;
                }
                if ((a.ClipToBounds || scrolls is not null || a == w) && tb.TransformToVisual(a) is { } m)
                {
                    var r = new Rect(tb.Bounds.Size).TransformToAABB(m);
                    var name = a.GetType().Name;
                    if (clipX && (r.X < -0.5 || r.Right > a.Bounds.Width + 0.5))
                        yield return (text, $"spans x {r.X:0}–{r.Right:0}, {name} is {a.Bounds.Width:0} wide");
                    if (clipY && (r.Y < -0.5 || r.Bottom > a.Bounds.Height + 0.5))
                        yield return (text, $"spans y {r.Y:0}–{r.Bottom:0}, {name} is {a.Bounds.Height:0} tall");
                }
                if (a == w) break;
            }
        }
    }
}
