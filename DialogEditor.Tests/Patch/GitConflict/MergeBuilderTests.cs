using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.GitConflict;
using Xunit;

namespace DialogEditor.Tests.Patch.GitConflict;

public class MergeBuilderTests
{
    private static NodeEditSnapshot Node(int id) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false, [], [], []);

    private static DialogProject ProjectWithAddedNodes(params NodeEditSnapshot[] nodes)
        => DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, nodes, [], []));

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

    private static DialogProject ProjectWithTranslation(int nodeId, string lang, string text)
    {
        var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                [lang] = [new NodeTranslation(nodeId, text, "")],
            },
        };
        return DialogProject.Empty("p").WithPatch(patch);
    }

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
    public void TranslationResolvedToTheirs_TakesTheirsText()
    {
        var mine   = ProjectWithTranslation(4, "en", "Hello friend");
        var theirs = ProjectWithTranslation(4, "en", "Hello traveler");
        var conflict = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));

        var merged = MergeBuilder.Build(mine, theirs, [(conflict, MergeSide.Theirs)]);

        var t = merged.Patches["greeting"].Translations["en"].Single(x => x.NodeId == 4);
        Assert.Equal("Hello traveler", t.DefaultText);
    }

    [Fact]
    public void TranslationResolvedToMine_KeepsMineText()
    {
        var mine   = ProjectWithTranslation(4, "en", "Hello friend");
        var theirs = ProjectWithTranslation(4, "en", "Hello traveler");
        var conflict = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));

        var merged = MergeBuilder.Build(mine, theirs, [(conflict, MergeSide.Mine)]);

        var t = merged.Patches["greeting"].Translations["en"].Single(x => x.NodeId == 4);
        Assert.Equal("Hello friend", t.DefaultText);
    }

    [Fact]
    public void ConversationLevelResolvedToTheirs_ReplacesWholePatch()
    {
        var mine   = ProjectWithAddedNodes(Node(5));
        var theirs = ProjectWithAddedNodes(Node(5), Node(9));
        var conflict = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.ConversationLevel, conflict.Kind);

        var merged = MergeBuilder.Build(mine, theirs, [(conflict, MergeSide.Theirs)]);

        Assert.Contains(merged.Patches["greeting"].AddedNodes, n => n.NodeId == 9);
    }

    [Fact]
    public void ConversationLevelResolvedToMine_KeepsMinePatch()
    {
        var mine   = ProjectWithAddedNodes(Node(5));
        var theirs = ProjectWithAddedNodes(Node(5), Node(9));
        var conflict = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));

        var merged = MergeBuilder.Build(mine, theirs, [(conflict, MergeSide.Mine)]);

        Assert.DoesNotContain(merged.Patches["greeting"].AddedNodes, n => n.NodeId == 9);
    }

    [Fact]
    public void TheirsOnlyLayout_IsCarriedThrough()
    {
        var mine   = DialogProject.Empty("p");
        var theirs = DialogProject.Empty("p").WithLayout(
            "greeting", new Dictionary<int, LayoutPoint> { [4] = new LayoutPoint(10, 20) });

        var merged = MergeBuilder.Build(mine, theirs, []);

        var layout = merged.GetLayout("greeting");
        Assert.NotNull(layout);
        Assert.Equal(new LayoutPoint(10, 20), layout![4]);
    }

    [Fact]
    public void LayoutOverlap_TheirsWins()
    {
        var mine   = DialogProject.Empty("p").WithLayout(
            "greeting", new Dictionary<int, LayoutPoint> { [4] = new LayoutPoint(1, 1) });
        var theirs = DialogProject.Empty("p").WithLayout(
            "greeting", new Dictionary<int, LayoutPoint> { [4] = new LayoutPoint(9, 9) });

        var merged = MergeBuilder.Build(mine, theirs, []);

        Assert.Equal(new LayoutPoint(9, 9), merged.GetLayout("greeting")![4]);
    }

    [Fact]
    public void NewConversations_AreUnioned()
    {
        var mine   = DialogProject.Empty("p").WithNewConversation("a");
        var theirs = DialogProject.Empty("p").WithNewConversation("b");

        var merged = MergeBuilder.Build(mine, theirs, []);

        Assert.Contains("a", merged.NewConversations!);
        Assert.Contains("b", merged.NewConversations!);
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

    [Fact]
    public void DeleteVsEditMineDeletesResolvedToTheirsEdit_RestoresNode()
    {
        var mine   = ProjectWithDeletion(4);                             // mine deletes
        var theirs = ProjectWithFieldChange(4, "DefaultText", "edited"); // theirs edits
        var conflict = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));

        var merged = MergeBuilder.Build(mine, theirs, [(conflict, MergeSide.Theirs)]);

        Assert.Contains(merged.Patches["greeting"].ModifiedNodes, m => m.NodeId == 4);
        Assert.DoesNotContain(4, merged.Patches["greeting"].DeletedNodeIds);
    }

    [Fact]
    public void DeleteVsEdit_DecidesByDeletedSide_NotByValueText()
    {
        // The sentinel used to be the display string "(deleted)" in TheirsValue. The
        // merge must key off the typed side instead, so the label the dialog shows can
        // be translated without changing which branch the merge takes.
        var mine   = ProjectWithFieldChange(4, "DefaultText", "edited");
        var theirs = ProjectWithDeletion(4);
        var conflict = new MergeConflict(
            MergeConflictKind.DeleteVsEdit, "greeting", 4, null, "DefaultText", "")
            { DeletedSide = MergeSide.Theirs };

        var merged = MergeBuilder.Build(mine, theirs, [(conflict, MergeSide.Theirs)]);

        Assert.DoesNotContain(merged.Patches["greeting"].ModifiedNodes, m => m.NodeId == 4);
        Assert.Contains(4, merged.Patches["greeting"].DeletedNodeIds);
    }
}
