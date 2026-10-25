using DialogEditor.Core.Models;
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.Text;

/// <summary>
/// GitHub issue 258: a node's one-line preview (Node Details' link list, the canvas
/// accessibility name) drops the game's markup tags, and cuts after stripping so the tags
/// do not eat into the 80 characters.
/// </summary>
public class MarkupInSummariesTests
{
    private static NodeViewModel Node(string text) => new(
        new ConversationNode(1, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None"),
        new StringEntry(1, text, ""));

    [Fact]
    public void TextPreview_DropsMarkup()
        => Assert.Equal("Show. Them.", Node("<ispeech>Show. Them.</ispeech>").TextPreview);

    [Fact]
    public void TextPreview_CutsAfterStripping()
    {
        var words = new string('a', 80);
        Assert.Equal(words, Node($"<link=\"glossary://Long_Entry_Name\">{words}</link>").TextPreview);
    }
}
