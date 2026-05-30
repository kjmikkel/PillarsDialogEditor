using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class ProjectDiffTests
{
    private static NodeEditSnapshot Node(int id, string display = "Conversation") =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", display, "None", "", "", "", false, false, [], [], []);

    private static DialogProject WithAdded(params NodeEditSnapshot[] nodes) =>
        DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, nodes, [], []));

    [Fact]
    public void AddedNode_InBOnly_IsAdded()
    {
        var a = WithAdded(Node(1));
        var b = WithAdded(Node(1), Node(2));

        var change = Assert.Single(ProjectDiff.Diff(a, b));
        Assert.Equal("greeting", change.Name);
        Assert.Equal([2], change.Added);
        Assert.Empty(change.Removed);
        Assert.Empty(change.Modified);
    }

    [Fact]
    public void NodeInAOnly_IsRemoved()
    {
        var a = WithAdded(Node(1), Node(2));
        var b = WithAdded(Node(1));

        var change = Assert.Single(ProjectDiff.Diff(a, b));
        Assert.Equal([2], change.Removed);
    }

    [Fact]
    public void SameNodeDifferentContent_IsModified()
    {
        var a = WithAdded(Node(1, "Conversation"));
        var b = WithAdded(Node(1, "QuestionNode"));

        var change = Assert.Single(ProjectDiff.Diff(a, b));
        Assert.Equal([1], change.Modified);
    }

    [Fact]
    public void IdenticalProjects_HaveNoChanges()
    {
        var a = WithAdded(Node(1));
        var b = WithAdded(Node(1));
        Assert.Empty(ProjectDiff.Diff(a, b));
    }

    [Fact]
    public void TranslationChange_IsModified()
    {
        DialogProject WithText(string text) =>
            DialogProject.Empty("p").WithPatch(
                new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [])
                {
                    Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                    { ["en"] = [new NodeTranslation(5, text, "")] }
                });

        var change = Assert.Single(ProjectDiff.Diff(WithText("Hi"), WithText("Hello")));
        Assert.Equal([5], change.Modified);
    }
}
