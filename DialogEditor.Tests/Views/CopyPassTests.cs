using System.Xml.Linq;

namespace DialogEditor.Tests.Views;

/// <summary>
/// GitHub issue 264: small copy and consistency fixes from the UI/UX pass, checked in the markup.
/// </summary>
public class CopyPassTests
{
    private static XDocument Load(string view)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx"))) dir = dir.Parent;
        return XDocument.Load(Path.Combine(dir!.FullName, "DialogEditor.Avalonia", "Views", view + ".axaml"));
    }

    private static IEnumerable<XElement> Named(XDocument d, string name) =>
        d.Descendants().Where(e => e.Name.LocalName == name);

    // F-26: the column holds Vanilla, Edited or New, so its header can't be "Vanilla".
    [Fact]
    public void SpeakerLines_OriginColumnHeader_NamesTheColumn_NotOneOfItsValues()
    {
        var header = Named(Load("SpeakerLineBrowserWindow"), "TextBlock")
            .Single(e => e.Attribute("Classes")?.Value == "col-header" && e.Attribute("Grid.Column")?.Value == "0");
        Assert.Equal("{DynamicResource SpeakerLines_Header_Status}", header.Attribute("Text")?.Value);
        // A header's tooltip is its own full text (issue 190, TableColumnsTests).
        Assert.Equal("{DynamicResource SpeakerLines_Header_Status}", header.Attribute("ToolTip.Tip")?.Value);
    }

    // F-28: the summary line already says "No text issues found."; the window said it twice.
    [Fact]
    public void TextTagValidation_SaysNoIssuesOnce()
        => Assert.DoesNotContain(Named(Load("TextTagValidationWindow"), "TextBlock"),
            e => e.Attribute("Text")?.Value == "{DynamicResource TextTagValidation_NoIssues}");

    // F-29: the Legend documents ⊞ / ⊟ for the Conversations pane. The buttons were lost when the
    // pane moved into the dock; they are back, so the Legend is true again.
    [Theory]
    [InlineData("{Binding ExpandAllCommand}",   "ToolTip_ExpandAll",   "AutomationName_ExpandAllFolders")]
    [InlineData("{Binding CollapseAllCommand}", "ToolTip_CollapseAll", "AutomationName_CollapseAllFolders")]
    public void Conversations_HasTheExpandAndCollapseAllButtons(string command, string tip, string name)
    {
        var button = Named(Load("GameBrowserView"), "Button").SingleOrDefault(e => e.Attribute("Command")?.Value == command);
        Assert.True(button is not null, $"GameBrowserView has no button bound to {command}");
        Assert.Equal($"{{DynamicResource {tip}}}", button!.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal($"{{DynamicResource {tip}}}", button.Attribute("AutomationProperties.HelpText")?.Value);
        Assert.Equal($"{{DynamicResource {name}}}", button.Attribute("AutomationProperties.Name")?.Value);
    }
}
