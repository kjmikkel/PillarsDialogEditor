using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class StaleDataPrunerTests
{
    private static DialogProject BuildProject()
    {
        var patch = new ConversationPatch("conv_a", ConversationPatch.CurrentSchemaVersion, [], [7], [])
        {
            NodeComments = new Dictionary<int, string> { [7] = "gone", [3] = "live" },
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(7, "gone", ""), new NodeTranslation(3, "stays", "")],
                ["de"] = [new NodeTranslation(7, "weg", "")],
            },
        };
        return DialogProject.Empty("Test").WithPatch(patch)
            .WithLayout("conv_a", new Dictionary<int, LayoutPoint>
            {
                [7] = new LayoutPoint(1, 1), [3] = new LayoutPoint(2, 2),
            });
    }

    [Fact]
    public void Prune_RemovesComment_Translation_AndLayout_ForNode()
    {
        var project = BuildProject();
        var rows = new List<StaleDataRow>
        {
            new("conv_a", 7, StaleDataKind.Comment,     null, StaleConfidence.Confirmed),
            new("conv_a", 7, StaleDataKind.Translation, "en", StaleConfidence.Confirmed),
            new("conv_a", 7, StaleDataKind.Translation, "de", StaleConfidence.Confirmed),
            new("conv_a", 7, StaleDataKind.Layout,      null, StaleConfidence.Confirmed),
        };

        var result = StaleDataPruner.Prune(project, rows);
        var patch  = result.Patches["conv_a"];

        Assert.False(patch.NodeComments.ContainsKey(7));
        Assert.True(patch.NodeComments.ContainsKey(3));
        Assert.DoesNotContain(patch.Translations["en"], t => t.NodeId == 7);
        Assert.Contains(patch.Translations["en"], t => t.NodeId == 3);
        Assert.False(patch.Translations.ContainsKey("de")); // last entry removed → language dropped
        Assert.False(result.GetLayout("conv_a")!.ContainsKey(7));
        Assert.True(result.GetLayout("conv_a")!.ContainsKey(3));
    }

    [Fact]
    public void Prune_OnlyRemovesTranslation_ForNamedLanguage()
    {
        var project = BuildProject();
        // Only the German translation of node 7 is pruned; English row 7 stays.
        var rows = new List<StaleDataRow>
        {
            new("conv_a", 7, StaleDataKind.Translation, "de", StaleConfidence.Likely),
        };

        var patch = StaleDataPruner.Prune(project, rows).Patches["conv_a"];
        Assert.False(patch.Translations.ContainsKey("de"));
        Assert.Contains(patch.Translations["en"], t => t.NodeId == 7);
        Assert.True(patch.NodeComments.ContainsKey(7)); // comment untouched
    }

    [Fact]
    public void Prune_EmptyRows_ReturnsProjectUnchanged()
    {
        var project = BuildProject();
        var result  = StaleDataPruner.Prune(project, []);
        Assert.True(result.Patches["conv_a"].NodeComments.ContainsKey(7));
    }
}
