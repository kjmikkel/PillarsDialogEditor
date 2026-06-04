using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.GitConflict;
using Xunit;

namespace DialogEditor.Tests.Patch.GitConflict;

public class GitMergeAnalyzerTests
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

    private static NodeEditSnapshot Node(int id, string displayType = "Conversation") =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", displayType, "None", "", "", "", false, false, [], [], []);

    private static DialogProject ProjectWithAddedNode(NodeEditSnapshot node)
    {
        var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
            [node], [], []);
        return DialogProject.Empty("p").WithPatch(patch);
    }

    private static DialogProject ProjectWithDeletion(int nodeId)
        => DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [nodeId], []));

    private static DialogProject ProjectWithTranslation(int nodeId, string lang, string text, string female = "")
    {
        var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                [lang] = [new NodeTranslation(nodeId, text, female)],
            },
        };
        return DialogProject.Empty("p").WithPatch(patch);
    }

    [Fact]
    public void SameFieldDifferentValue_IsFieldEditConflict()
    {
        var mine   = ProjectWithFieldChange(4, "DefaultText", "friend");
        var theirs = ProjectWithFieldChange(4, "DefaultText", "traveler");

        var conflicts = GitMergeAnalyzer.Analyze(mine, theirs);

        var c = Assert.Single(conflicts);
        Assert.Equal(MergeConflictKind.FieldEdit, c.Kind);
        Assert.Equal("greeting", c.ConversationName);
        Assert.Equal(4, c.NodeId);
        Assert.Equal("DefaultText", c.FieldName);
        Assert.Equal("friend",   c.MineValue);
        Assert.Equal("traveler", c.TheirsValue);
    }

    [Fact]
    public void SameFieldSameValue_IsNotConflict()
    {
        var mine   = ProjectWithFieldChange(4, "DefaultText", "same");
        var theirs = ProjectWithFieldChange(4, "DefaultText", "same");
        Assert.Empty(GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [Fact]
    public void DeleteOnMineEditOnTheirs_IsDeleteVsEdit()
    {
        var mine   = ProjectWithDeletion(4);
        var theirs = ProjectWithFieldChange(4, "DefaultText", "edited");

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.DeleteVsEdit, c.Kind);
        Assert.Equal(4, c.NodeId);
    }

    [Fact]
    public void EditOnMineDeleteOnTheirs_IsDeleteVsEdit()
    {
        var mine   = ProjectWithFieldChange(4, "DefaultText", "edited");
        var theirs = ProjectWithDeletion(4);

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.DeleteVsEdit, c.Kind);
        Assert.Equal(4, c.NodeId);
    }

    [Fact]
    public void AddAddDifferentContent_IsNodeAddAdd()
    {
        var mine   = ProjectWithAddedNode(Node(5, "Conversation"));
        var theirs = ProjectWithAddedNode(Node(5, "QuestionNode"));

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.NodeAddAdd, c.Kind);
        Assert.Equal(5, c.NodeId);
    }

    [Fact]
    public void AddAddIdenticalContent_IsNotConflict()
    {
        var mine   = ProjectWithAddedNode(Node(5, "Conversation"));
        var theirs = ProjectWithAddedNode(Node(5, "Conversation"));
        Assert.Empty(GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [Fact]
    public void ConversationOnlyInOneSide_IsNotConflict()
    {
        var mine   = ProjectWithFieldChange(1, "DefaultText", "a");
        var theirs = DialogProject.Empty("p"); // no patches
        Assert.Empty(GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [Fact]
    public void TranslationTextDiffers_IsTranslationEditConflict()
    {
        var mine   = ProjectWithTranslation(4, "en", "Hello friend");
        var theirs = ProjectWithTranslation(4, "en", "Hello traveler");

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.TranslationEdit, c.Kind);
        Assert.Equal("greeting", c.ConversationName);
        Assert.Equal(4, c.NodeId);
        Assert.Equal("en", c.FieldName);
        Assert.Equal("Hello friend",   c.MineValue);
        Assert.Equal("Hello traveler", c.TheirsValue);
    }

    [Fact]
    public void TranslationIdentical_IsNotConflict()
    {
        var mine   = ProjectWithTranslation(4, "en", "Hello");
        var theirs = ProjectWithTranslation(4, "en", "Hello");
        Assert.Empty(GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [Fact]
    public void TheirsOnlyLanguage_IsConversationLevel()
    {
        // theirs adds a French line mine lacks — the per-node merge can't preserve it,
        // so it surfaces as a whole-conversation choice rather than being dropped.
        var mine   = ProjectWithTranslation(4, "en", "Hello");
        var theirs = ProjectWithTranslation(4, "fr", "Bonjour");

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.ConversationLevel, c.Kind);
        Assert.Equal(-1, c.NodeId);
        Assert.Equal("greeting", c.ConversationName);
    }

    [Fact]
    public void TheirsOnlyAddedNode_IsConversationLevel()
    {
        var mine   = ProjectWithAddedNode(Node(5));
        var theirs = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(5), Node(9)], [], []));

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.ConversationLevel, c.Kind);
    }

    [Fact]
    public void TheirsOnlyDeletion_IsConversationLevel()
    {
        var mine   = ProjectWithFieldChange(7, "DisplayType", "X");  // mine touches node 7
        var theirs = ProjectWithDeletion(4);                         // theirs deletes node 4

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.ConversationLevel, c.Kind);
    }

    [Fact]
    public void UncoveredTheirsContent_SuppressesGranularConflicts()
    {
        var mineMod  = new NodeModification(4,
            new Dictionary<string, FieldChange> { ["DisplayType"] = new FieldChange("o", "A") }, [], []);
        var theirMod = new NodeModification(4,
            new Dictionary<string, FieldChange> { ["DisplayType"] = new FieldChange("o", "B") }, [], []);
        var mine   = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [mineMod]));
        var theirs = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(9)], [], [theirMod]));

        // A node-4 field edit AND a theirs-only added node 9 → single whole-conversation
        // conflict, not the granular FieldEdit.
        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.ConversationLevel, c.Kind);
    }

    [Fact]
    public void TranslationFemaleOnlyDiffers_DefaultEqual_FemaleCarriesDiff()
    {
        var mine   = ProjectWithTranslation(4, "en", "Hello", "HelloF");
        var theirs = ProjectWithTranslation(4, "en", "Hello", "HelloFemale");

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.TranslationEdit, c.Kind);
        Assert.Equal("Hello",       c.MineValue);          // shared Default
        Assert.Equal("Hello",       c.TheirsValue);
        Assert.Equal("HelloF",      c.MineFemaleValue);
        Assert.Equal("HelloFemale", c.TheirsFemaleValue);
    }

    [Fact]
    public void TranslationBothVariantsDiffer_AllFourValuesCarried()
    {
        var mine   = ProjectWithTranslation(4, "en", "Hi friend",   "Hi friendF");
        var theirs = ProjectWithTranslation(4, "en", "Hi traveler", "Hi travelerF");

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal("Hi friend",    c.MineValue);
        Assert.Equal("Hi traveler",  c.TheirsValue);
        Assert.Equal("Hi friendF",   c.MineFemaleValue);
        Assert.Equal("Hi travelerF", c.TheirsFemaleValue);
    }

    [Fact]
    public void UpdatedConditionsDiffer_IsFieldEditConflict()
    {
        var mineMod = new NodeModification(7, new Dictionary<string, FieldChange>(), [], [])
            { UpdatedConditions = [] };
        var theirMod = new NodeModification(7, new Dictionary<string, FieldChange>(), [], [])
            { UpdatedConditions = [new ConditionLeaf("Boolean Flag()", [], Not: false, Operator: "And")] };
        var mine   = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [mineMod]));
        var theirs = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [theirMod]));

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.FieldEdit, c.Kind);
        Assert.Equal("Conditions", c.FieldName);
    }
}
