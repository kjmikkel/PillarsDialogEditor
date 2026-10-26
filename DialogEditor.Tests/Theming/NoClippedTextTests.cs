using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

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
public class NoClippedTextTests : IDisposable
{
    // The real strings: the stub provider returns resource keys, which are longer than any real
    // text and would make text set from code (table cells, issue 178) look clipped when it is not.
    public NoClippedTextTests()
    {
        Loc.Configure(new AvaloniaStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    // Closing a ModeChooserWindow without a choice saves Reading mode (issue 234). These tests open
    // every window type, so without a private settings file that leaked Reading into the run-wide
    // fallback file and later tests started in Reading mode.
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"nctt_{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

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
    private static readonly Dictionary<string, Func<Window>> Populated = new Dictionary<string, Func<Window>>
    {
        // One condition row: its And/Or combo sat in a fixed 70 px column and showed "An" at 2.0x.
        ["ConditionEditor_AndOrCombo"] = () => new ConditionEditorWindow(new ConditionEditorViewModel(
            "canonical",
            [new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)", ["flag", "EqualTo", "1"], false, "And")],
            _ => { })),
    }.Concat(TableWindowFixtures.All).ToDictionary();   // the tables of issue 178, with rows

    public static IEnumerable<object[]> PopulatedCases() =>
        from name in Populated.Keys
        from scale in new[] { 1.0, 2.0 }
        select new object[] { name, scale };

    [AvaloniaTheory]
    [MemberData(nameof(PopulatedCases))]
    public void PopulatedWindow_ClipsNoText(string name, double scale) =>
        AssertClipsNoText(Populated[name], scale);

    // The Legend's Connections rows were cut off at the right edge (issue 190). They only clip when
    // the window is narrower than the text, and the sizing rules grow the window with the font, so the
    // default size never shows it: shrink the window to its own MinWidth, the narrowest a user can go.
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void LegendWindow_AtMinWidth_ClipsNoText(double scale) =>
        AssertClipsNoText(() => new LegendWindow(), scale, w =>
        {
            w.Width = w.MinWidth;
            Dispatcher.UIThread.RunJobs();
        });

    // Batch VO Import at its narrowest (issue 190): the two file columns share what is left of the
    // row, and without a minimum the Play, Browse and Clear buttons are squeezed out of them.
    [AvaloniaTheory]
    [InlineData("BatchVoImport_Files", 1.0)]
    [InlineData("BatchVoImport_AllConversations", 1.0)]
    [InlineData("BatchVoImport_Files", 1.5)]
    [InlineData("BatchVoImport_AllConversations", 1.5)]
    public void BatchVoImport_AtMinWidth_ClipsNoText(string fixture, double scale) =>
        AssertClipsNoText(TableWindowFixtures.All[fixture], scale, w =>
        {
            w.Width = w.MinWidth;
            Dispatcher.UIThread.RunJobs();
        });

    // ...and the file name must keep room to be read: at least four em beside the buttons, so a
    // name is more than an ellipsis.
    [AvaloniaTheory]
    [InlineData("BatchVoImport_Files", 1.0)]
    [InlineData("BatchVoImport_AllConversations", 1.0)]
    [InlineData("BatchVoImport_Files", 1.5)]
    [InlineData("BatchVoImport_AllConversations", 1.5)]
    public void BatchVoImport_AtMinWidth_FileNamesKeepRoom(string fixture, double scale)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => new Size(1366, 768);
        var w = TableWindowFixtures.All[fixture]();
        try
        {
            new FontScaleApplier().Apply(scale);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.Width = w.MinWidth;
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            var names = w.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && t.TextTrimming != TextTrimming.None
                            && t.Text is { } x && x.EndsWith(".wav", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(2, names.Count);
            foreach (var n in names)
                Assert.True(n.Bounds.Width >= n.FontSize * 4,
                    $"{fixture} at {scale}x: file name \"{n.Text}\" has {n.Bounds.Width:0} px, wants {n.FontSize * 4:0}");
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }

    private static void AssertClipsNoText(Func<Window> create, double scale, Action<Window>? adjust = null)
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
            if (adjust is not null) { adjust(w); w.UpdateLayout(); }
            var clipped = Clipped(w).ToList();
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
