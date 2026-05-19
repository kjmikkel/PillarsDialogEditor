using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class MergeProjectTests
{
    private static ConversationPatch MakePatch(string conv, int nodeId, string field, string from, string to)
    {
        var mod = new NodeModification(nodeId,
            new Dictionary<string, FieldChange> { [field] = new FieldChange(from, to) },
            [], []);
        return new ConversationPatch(conv, 1, [], [], [mod]);
    }

    [Fact]
    public void MergeWith_DisjointConversations_BothPresent()
    {
        var a = DialogProject.Empty("A").WithPatch(MakePatch("conv1", 1, "DefaultText", "x", "y"));
        var b = DialogProject.Empty("B").WithPatch(MakePatch("conv2", 2, "DefaultText", "x", "z"));

        var merged = a.MergeWith(b);

        Assert.True(merged.Patches.ContainsKey("conv1"));
        Assert.True(merged.Patches.ContainsKey("conv2"));
    }

    [Fact]
    public void MergeWith_SameConversation_PatchesMerged()
    {
        var a = DialogProject.Empty("A").WithPatch(MakePatch("conv1", 1, "DefaultText", "old", "v1"));
        var b = DialogProject.Empty("B").WithPatch(MakePatch("conv1", 1, "FemaleText",  "",    "v2"));

        var merged = a.MergeWith(b);

        var patch = merged.Patches["conv1"];
        Assert.Single(patch.ModifiedNodes);
        Assert.Contains("DefaultText", patch.ModifiedNodes[0].FieldChanges.Keys);
        Assert.Contains("FemaleText",  patch.ModifiedNodes[0].FieldChanges.Keys);
    }

    [Fact]
    public void MergeWith_NewConversationsUnioned()
    {
        var a = DialogProject.Empty("A").WithNewConversation("new_a");
        var b = DialogProject.Empty("B").WithNewConversation("new_b");

        var merged = a.MergeWith(b);

        Assert.Contains("new_a", merged.NewConversations ?? []);
        Assert.Contains("new_b", merged.NewConversations ?? []);
    }

    [Fact]
    public void MergeWith_Layouts_Merged()
    {
        var a = DialogProject.Empty("A")
            .WithPatch(MakePatch("conv1", 1, "DefaultText", "x", "y"))
            .WithLayout("conv1", new Dictionary<int, LayoutPoint> { [1] = new LayoutPoint(10, 20) });
        var b = DialogProject.Empty("B")
            .WithPatch(MakePatch("conv1", 2, "DefaultText", "x", "z"))
            .WithLayout("conv1", new Dictionary<int, LayoutPoint> { [2] = new LayoutPoint(30, 40) });

        var merged = a.MergeWith(b);

        var layout = merged.GetLayout("conv1");
        Assert.NotNull(layout);
        Assert.True(layout.ContainsKey(1));
        Assert.True(layout.ContainsKey(2));
    }

    [Fact]
    public void MergeWith_EmptyProject_ReturnsOriginal()
    {
        var a = DialogProject.Empty("A").WithPatch(MakePatch("conv1", 1, "DefaultText", "x", "y"));
        var b = DialogProject.Empty("B");

        var merged = a.MergeWith(b);

        Assert.Single(merged.Patches);
        Assert.True(merged.Patches.ContainsKey("conv1"));
    }
}
