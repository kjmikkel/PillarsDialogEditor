using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.Tests.Theming;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// <summary>
/// Issue #94: lists on the standard Fluent <see cref="ListBox"/> selection drew their row text
/// in its RESTING colours on a selection highlight that came from Fluent's accent brushes, not
/// our tokens — so no palette gate covered it, and Muted text (#888) sat at ~2.3:1 on it in Dark
/// and Colourblind. Every text element on a selected row must be legible (WCAG AA, 7:1 in High
/// Contrast) against the highlight actually drawn behind it, in every palette.
///
/// Like <see cref="GameBrowserSelectionContrastTests"/> (#83) this measures the REALISED
/// controls — the row's TextBlock foregrounds against the item presenter's background — so a
/// template that bypasses the tokens still fails.
/// </summary>
public class ListSelectionContrastTests
{
    public ListSelectionContrastTests() => Loc.Configure(new StubStringProvider());

    public static IEnumerable<object[]> Cases()
    {
        (string Theme, double Min)[] themes =
            [("Dark", 4.5), ("Light", 4.5), ("Colourblind", 4.5), ("HighContrast", 7.0)];
        string[] windows = ["FindInProject", "SpeakerLines"];
        foreach (var (theme, min) in themes)
            foreach (var window in windows)
                yield return [theme, window, min];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void SelectedRowText_ContrastsWithSelectionHighlight(string theme, string windowName, double min)
    {
        try
        {
            new ThemeApplier().Apply(theme);

            // Both windows fill ResultsList from code-behind, and their row templates use
            // reflection bindings, so placeholder items realise the real template: the
            // bindings resolve to nothing, but every TextBlock keeps its styled Foreground.
            Window window = windowName == "FindInProject"
                ? new FindInProjectWindow()
                : new SpeakerLineBrowserWindow();
            var list = window.FindControl<ListBox>("ResultsList")!;
            list.ItemsSource = new[] { "first", "second" };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            list.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var item = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            Assert.True(item.IsSelected, "the first row did not become selected");

            var presenter = item.GetVisualDescendants().OfType<ContentPresenter>()
                .First(p => p.Name == "PART_ContentPresenter");
            var bg = Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color;

            var labels = presenter.GetVisualDescendants().OfType<TextBlock>().ToList();
            Assert.NotEmpty(labels);
            foreach (var label in labels)
            {
                var fg = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
                var ratio = Wcag.ContrastRatio(fg, bg);
                Assert.True(ratio >= min,
                    $"{theme} / {windowName}: selected-row text {fg} on highlight {bg} = {ratio:F2}:1 < {min:F1}:1");
            }
            window.Close();
        }
        finally { new ThemeApplier().Apply("Dark"); }
    }

    // The other half of the fix: moving row colours from local values into role classes must not
    // flatten the resting colours, and the selection colour must follow the selection — a row
    // that was selected and is no longer drops back to its own colours.
    [AvaloniaFact]
    public void UnselectedRows_KeepTheirRestingColours()
    {
        var window = new FindInProjectWindow();
        var list = window.FindControl<ListBox>("ResultsList")!;
        list.ItemsSource = new[] { "first", "second" };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        list.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        list.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Color Token(string key) => ((ISolidColorBrush)global::Avalonia.Application.Current!
            .FindResource(key)!).Color;
        var colours = list.ContainerFromIndex(0)!.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => ((ISolidColorBrush)t.Foreground!).Color).ToList();

        // Column order in the Find in Project row template: conversation, node, field, language, snippet.
        Assert.Equal(
            [Token("Brush.Text.Primary"), Token("Brush.Text.Secondary"), Token("Brush.Text.Secondary"),
             Token("Brush.Text.Muted"), Token("Brush.Text.Secondary")],
            colours);
        window.Close();
    }

    // Guards every OTHER list too, without building each window: a Foreground set directly on a
    // TextBlock is a local value, which outranks every style — so the shared selected-row rule
    // could never replace it, and that row would keep its resting colour on the highlight
    // (exactly the #83 / #94 defect). Row text must take its colour from a style class instead.
    [Fact]
    public void ListRowTemplates_DoNotSetLocalTextForeground()
    {
        var viewsDir = Path.Combine(RepoRoot(), "DialogEditor.Avalonia", "Views");
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(viewsDir, "*.axaml"))
        {
            var xaml = File.ReadAllText(file);
            foreach (Match template in Regex.Matches(xaml,
                         @"<ListBox\.ItemTemplate>.*?</ListBox\.ItemTemplate>", RegexOptions.Singleline))
            {
                foreach (Match tag in Regex.Matches(template.Value, @"<(TextBlock|ToggleButton)\b[^>]*>"))
                    if (Regex.IsMatch(tag.Value, @"\sForeground="))
                        offenders.Add($"{Path.GetFileName(file)}: {Regex.Replace(tag.Value, @"\s+", " ")}");
            }
        }
        Assert.True(offenders.Count == 0,
            "List row text sets a local Foreground (use a list-* style class):\n" + string.Join("\n", offenders));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
