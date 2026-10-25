using System.Xml.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DialogEditor.Avalonia.Behaviors;

namespace DialogEditor.Tests.Behaviors;

/// <summary>
/// GitHub issue 258: MarkupText.Source fills a TextBlock with styled runs built from the game's
/// markup, so a card or a wrapped list row shows italic inner speech instead of literal tags.
/// </summary>
public class MarkupTextTests
{
    private static List<Run> Runs(TextBlock t) => t.Inlines!.OfType<Run>().ToList();

    [AvaloniaFact]
    public void Source_BuildsItalicAndBoldRuns()
    {
        var t = new TextBlock();
        MarkupText.SetSource(t, "a <i>b</i> <b>c</b>");

        var runs = Runs(t);
        Assert.Equal(["a ", "b", " ", "c"], runs.Select(r => r.Text));
        Assert.Equal(FontStyle.Italic, runs[1].FontStyle);
        Assert.Equal(FontWeight.Bold, runs[3].FontWeight);
        Assert.Equal(FontWeight.Normal, runs[0].FontWeight);
    }

    // Runs leave TextBlock.Text empty, and the automation peer names a TextBlock by its Text, so
    // the plain line is given as the accessible name instead.
    [AvaloniaFact]
    public void Source_GivesThePlainTextAsTheAutomationName()
    {
        var t = new TextBlock();
        MarkupText.SetSource(t, "<ispeech>Show.</ispeech> Them.");

        Assert.Equal("Show. Them.", AutomationProperties.GetName(t));
    }

    [AvaloniaFact]
    public void ChangingSource_ReplacesTheRuns()
    {
        var t = new TextBlock();
        MarkupText.SetSource(t, "<i>old</i>");
        MarkupText.SetSource(t, "new");

        Assert.Equal(["new"], Runs(t).Select(r => r.Text));
    }

    [AvaloniaFact]
    public void NullSource_LeavesNoText()
    {
        var t = new TextBlock();
        MarkupText.SetSource(t, "x");
        MarkupText.SetSource(t, null);

        Assert.Empty(Runs(t));
    }

    // The Speaker Line Browser wraps each full line, so it shows the markup styled like a card.
    [Fact]
    public void SpeakerLineBrowser_LineCell_UsesMarkupText()
    {
        var path = Path.Combine(RepoRoot(), "DialogEditor.Avalonia", "Views", "SpeakerLineBrowserWindow.axaml");
        var cells = XDocument.Load(path).Descendants()
            .Where(e => e.Name.LocalName == "TextBlock")
            .Where(e => e.Attributes().Any(a => a.Value.Contains("Row.LineText") || a.Value.Contains("LineText")))
            .ToList();
        Assert.NotEmpty(cells);
        Assert.All(cells, c =>
        {
            Assert.Null(c.Attribute("Text"));
            Assert.Contains(c.Attributes(), a => a.Name.LocalName == "MarkupText.Source");
        });
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
