using System.Linq;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Controls;

namespace DialogEditor.Tests.Controls;

public class InlineDiffTextBlockTests
{
    private static string Text(InlineDiffTextBlock c) =>
        string.Concat((c.Inlines ?? new InlineCollection()).OfType<Run>().Select(r => r.Text));

    [AvaloniaFact]
    public void BeforeSide_RendersCommonPlusBeforeOnlyText()
    {
        var c = new InlineDiffTextBlock { ShowAfter = false, Before = "hello world", After = "hello there" };
        Assert.Equal("hello world", Text(c));
    }

    [AvaloniaFact]
    public void AfterSide_RendersCommonPlusAfterOnlyText()
    {
        var c = new InlineDiffTextBlock { ShowAfter = true, Before = "hello world", After = "hello there" };
        Assert.Equal("hello there", Text(c));
    }

    [AvaloniaFact]
    public void IdenticalText_RendersThatTextOnBothSides()
    {
        var before = new InlineDiffTextBlock { ShowAfter = false, Before = "same", After = "same" };
        var after  = new InlineDiffTextBlock { ShowAfter = true,  Before = "same", After = "same" };
        Assert.Equal("same", Text(before));
        Assert.Equal("same", Text(after));
    }

    [AvaloniaFact]
    public void NullStrings_RenderEmpty()
    {
        var c = new InlineDiffTextBlock { ShowAfter = false };
        Assert.Equal("", Text(c));
    }
}
