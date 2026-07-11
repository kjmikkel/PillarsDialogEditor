using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ProjectTextTagScannerTests
{
    public ProjectTextTagScannerTests() => Loc.Configure(new StubStringProvider());

    private static ConversationPatch Patch(
        string name,
        IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> translations)
        => new(name, ConversationPatch.CurrentSchemaVersion, [], [], [])
           { Translations = translations };

    private static DialogProject Project(params ConversationPatch[] patches)
    {
        var project = DialogProject.Empty("Test");
        foreach (var p in patches) project = project.WithPatch(p);
        return project;
    }

    [Fact]
    public void PrimaryLanguageTypo_ReportedAsDefault()
    {
        var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["en"] = [new NodeTranslation(5, "Hi [Player Nmae]", "")],
        }));
        var row = Assert.Single(ProjectTextTagScanner.Scan(project, "poe2", "en"));
        Assert.Equal(("conv_a", 5, ""), (row.ConversationName, row.NodeId, row.Language));
        Assert.Equal("Validation_UnknownToken_Suggest", row.Message); // stub echoes the key
    }

    [Fact]
    public void TranslationTypo_CarriesLanguageCode()
    {
        var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["fr"] = [new NodeTranslation(7, "Bonjour [Player Nmae]", "")],
        }));
        var row = Assert.Single(ProjectTextTagScanner.Scan(project, "poe2", "en"));
        Assert.Equal("fr", row.Language);
    }

    [Fact]
    public void FemaleText_Validated()
    {
        var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["en"] = [new NodeTranslation(3, "clean", "she says <i>oops")],
        }));
        var row = Assert.Single(ProjectTextTagScanner.Scan(project, "poe2", "en"));
        Assert.Equal("Validation_UnbalancedMarkup", row.Message);
    }

    [Fact]
    public void CleanProject_Empty()
    {
        var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["en"] = [new NodeTranslation(1, "Hello [Player Name].", "")],
        }));
        Assert.Empty(ProjectTextTagScanner.Scan(project, "poe2", "en"));
    }

    [Fact]
    public void Rows_OrderedByConversationNodeLanguage()
    {
        var project = Project(
            Patch("conv_b", new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(2, "[Player Nmae]", "")],
            }),
            Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["fr"] = [new NodeTranslation(9, "[Player Nmae]", "")],
                ["en"] = [new NodeTranslation(9, "[Player Nmae]", "")],
            }));
        var rows = ProjectTextTagScanner.Scan(project, "poe2", "en");
        Assert.Equal(
            [("conv_a", 9, ""), ("conv_a", 9, "fr"), ("conv_b", 2, "")],
            rows.Select(r => (r.ConversationName, r.NodeId, r.Language)).ToList());
    }

    [Fact]
    public void DefensiveAddedNodesText_ValidatedWhenPresent()
    {
        // Current schema zeroes AddedNodes text at save; a legacy patch might not.
        var node = new NodeEditSnapshot(
            4, false, SpeakerCategory.Npc, "", "", "legacy [Player Nmae]", "",
            "Conversation", "None", "", "", "", false, false, [], [], []);
        var patch = new ConversationPatch("conv_a", 1, [node], [], []);
        var project = Project(patch);
        var row = Assert.Single(ProjectTextTagScanner.Scan(project, "poe2", "en"));
        Assert.Equal(4, row.NodeId);
        Assert.Equal("", row.Language);
    }
}
