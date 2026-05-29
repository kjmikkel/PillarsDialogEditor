using DialogEditor.Patch;
using DialogEditor.Patch.GitConflict;
using Xunit;

namespace DialogEditor.Tests.Patch.GitConflict;

public class MergeBuilderTests
{
    private static DialogProject ProjectWithFieldChange(int nodeId, string field, string to)
    {
        var mod = new NodeModification(
            nodeId,
            new Dictionary<string, FieldChange> { [field] = new FieldChange("orig", to) },
            [], []);
        var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
            [], [], [mod]);
        return DialogProject.Empty("p").WithPatch(patch);
    }

    private static DialogProject ProjectWithDeletion(int nodeId)
        => DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [nodeId], []));

    [Fact]
    public void FieldEditResolvedToTheirs_TakesTheirsValue()
    {
        var mine   = ProjectWithFieldChange(4, "DefaultText", "friend");
        var theirs = ProjectWithFieldChange(4, "DefaultText", "traveler");
        var conflict = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));

        var merged = MergeBuilder.Build(mine, theirs,
            [(conflict, MergeSide.Theirs)]);

        var mod = merged.Patches["greeting"].ModifiedNodes.Single(m => m.NodeId == 4);
        Assert.Equal("traveler", mod.FieldChanges["DefaultText"].To);
    }

    [Fact]
    public void FieldEditResolvedToMine_KeepsMineValue()
    {
        var mine   = ProjectWithFieldChange(4, "DefaultText", "friend");
        var theirs = ProjectWithFieldChange(4, "DefaultText", "traveler");
        var conflict = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));

        var merged = MergeBuilder.Build(mine, theirs,
            [(conflict, MergeSide.Mine)]);

        var mod = merged.Patches["greeting"].ModifiedNodes.Single(m => m.NodeId == 4);
        Assert.Equal("friend", mod.FieldChanges["DefaultText"].To);
    }

    [Fact]
    public void TheirsOnlyConversation_IsCarriedThrough()
    {
        var mine   = ProjectWithFieldChange(1, "DefaultText", "a");
        var theirs = mine.WithPatch(new ConversationPatch(
            "shop", ConversationPatch.CurrentSchemaVersion, [], [], []));

        // greeting identical on both sides → no conflicts; shop is theirs-only.
        var merged = MergeBuilder.Build(mine, theirs, []);

        Assert.Contains("shop", merged.Patches.Keys);
        Assert.Contains("greeting", merged.Patches.Keys);
    }

    [Fact]
    public void DeleteVsEditResolvedToTheirsDelete_DropsNode()
    {
        var mine   = ProjectWithFieldChange(4, "DefaultText", "edited"); // mine edits
        var theirs = ProjectWithDeletion(4);                             // theirs deletes
        var conflict = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));

        var merged = MergeBuilder.Build(mine, theirs,
            [(conflict, MergeSide.Theirs)]);

        Assert.DoesNotContain(merged.Patches["greeting"].ModifiedNodes, m => m.NodeId == 4);
        Assert.Contains(4, merged.Patches["greeting"].DeletedNodeIds);
    }

    [Fact]
    public void DeleteVsEditResolvedToMineEdit_KeepsNode()
    {
        var mine   = ProjectWithFieldChange(4, "DefaultText", "edited"); // mine edits
        var theirs = ProjectWithDeletion(4);                             // theirs deletes
        var conflict = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));

        // Keep mine (the edit) → node stays, not deleted.
        var merged = MergeBuilder.Build(mine, theirs,
            [(conflict, MergeSide.Mine)]);

        Assert.Contains(merged.Patches["greeting"].ModifiedNodes, m => m.NodeId == 4);
        Assert.DoesNotContain(4, merged.Patches["greeting"].DeletedNodeIds);
    }
}
