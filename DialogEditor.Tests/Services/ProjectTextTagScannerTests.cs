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

    // ── Spelling integration (spell checker feature) ────────────────────────

    private static SpellCheckService FixtureChecker(string dir)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DialogEditor.slnx")))
            root = root.Parent;
        var fixtures = Path.Combine(root!.FullName, "DialogEditor.Tests", "Fixtures", "spell");
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(fixtures, "test_en.aff"), Path.Combine(dir, "en_US.aff"), overwrite: true);
        File.Copy(Path.Combine(fixtures, "test_en.dic"), Path.Combine(dir, "en_US.dic"), overwrite: true);
        return new SpellCheckService(new SpellDictionaryStore(dir, Path.Combine(dir, "user.txt")));
    }

    [Fact]
    public void SpellingRow_ForPrimaryLanguageTypo_WithWordAndType()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"scanspell_{Guid.NewGuid():N}");
        try
        {
            var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(5, "the captian glows", "")],
            }));
            var rows = ProjectTextTagScanner.Scan(project, "poe2", "en", spell: FixtureChecker(dir));
            // "the" and "captian" are both unknown to the tiny fixture dic; assert captian present.
            var spelling = rows.Where(r => r.Type == TextIssueType.Spelling).ToList();
            Assert.Contains(spelling, r => r.Word == "captian" && r.NodeId == 5 && r.Language == "");
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { /* best-effort */ } }
    }

    [Fact]
    public void SpellingSkipsLanguagesWithoutDictionary_ChecksTranslationLanguage()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"scanspell_{Guid.NewGuid():N}");
        try
        {
            var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["fr"] = [new NodeTranslation(7, "zzqq pff", "")],   // no fr dictionary → skipped
                ["en"] = [new NodeTranslation(7, "captian", "")],    // en dictionary → checked
            }));
            var rows = ProjectTextTagScanner.Scan(project, "poe2", "en", spell: FixtureChecker(dir));
            var spelling = rows.Where(r => r.Type == TextIssueType.Spelling).ToList();
            Assert.All(spelling, r => Assert.Equal("", r.Language)); // only en (primary) rows
            Assert.Contains(spelling, r => r.Word == "captian");
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { /* best-effort */ } }
    }

    [Fact]
    public void NullSpellChecker_TagBehaviourUnchanged()
    {
        var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["en"] = [new NodeTranslation(5, "Hi [Player Nmae] captian", "")],
        }));
        var rows = ProjectTextTagScanner.Scan(project, "poe2", "en");
        var row = Assert.Single(rows);
        Assert.Equal(TextIssueType.Tag, row.Type);
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
