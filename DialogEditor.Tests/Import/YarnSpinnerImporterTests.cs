using DialogEditor.Core.Import;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Import;

public class YarnSpinnerImporterTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string WriteTempYarn(string content, string? filename = null)
    {
        var name = filename ?? Path.GetRandomFileName();
        var path = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(name, ".yarn"));
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort cleanup */ }
    }

    private static readonly YarnSpinnerImporter Importer = new();

    // ── Single NPC line ───────────────────────────────────────────────────

    [Fact]
    public void Import_SingleNpcLine_ProducesOneNode()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Guard: Halt! Who goes there?
            ===
            """);

        var result = Importer.Import(path);

        Assert.Single(result.Nodes);
        var node = result.Nodes[0];
        Assert.Equal(1, node.NodeId);
        Assert.False(node.IsPlayerChoice);
        Assert.Equal(SpeakerCategory.Npc, node.SpeakerCategory);
        Assert.Equal("Halt! Who goes there?", node.DefaultText);
        Assert.Empty(node.Links);
    }

    // ── NPC line followed by choices ──────────────────────────────────────

    [Fact]
    public void Import_NpcLineFollowedByChoices_ProducesCorrectGraph()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Npc: Hello there!
            -> I need work.
            -> Goodbye.
            ===
            """);

        var result = Importer.Import(path);

        Assert.Equal(3, result.Nodes.Count);

        var npcNode = result.Nodes[0];
        Assert.False(npcNode.IsPlayerChoice);
        Assert.Equal(SpeakerCategory.Npc, npcNode.SpeakerCategory);
        Assert.Equal("Hello there!", npcNode.DefaultText);
        Assert.Equal(2, npcNode.Links.Count);
        Assert.Contains(npcNode.Links, l => l.ToNodeId == 2);
        Assert.Contains(npcNode.Links, l => l.ToNodeId == 3);

        var choice1 = result.Nodes[1];
        Assert.True(choice1.IsPlayerChoice);
        Assert.Equal(SpeakerCategory.Player, choice1.SpeakerCategory);
        Assert.Equal("I need work.", choice1.DefaultText);

        var choice2 = result.Nodes[2];
        Assert.True(choice2.IsPlayerChoice);
        Assert.Equal(SpeakerCategory.Player, choice2.SpeakerCategory);
        Assert.Equal("Goodbye.", choice2.DefaultText);
    }

    // ── Sequential NPC lines ──────────────────────────────────────────────

    [Fact]
    public void Import_SequentialNpcLines_LinkedInOrder()
    {
        var path = WriteTempYarn("""
            title: Talk
            ---
            Npc: Line one.
            Npc: Line two.
            ===
            """);

        var result = Importer.Import(path);

        Assert.Equal(2, result.Nodes.Count);

        var node1 = result.Nodes[0];
        Assert.Equal(1, node1.NodeId);
        Assert.Equal("Line one.", node1.DefaultText);
        Assert.Single(node1.Links);
        Assert.Equal(2, node1.Links[0].ToNodeId);

        var node2 = result.Nodes[1];
        Assert.Equal(2, node2.NodeId);
        Assert.Equal("Line two.", node2.DefaultText);
        Assert.Empty(node2.Links);
    }

    // ── Choice jump to target block ───────────────────────────────────────

    [Fact]
    public void Import_ChoiceJump_LinksToFirstNodeOfTargetBlock()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Npc: Need a job?
            -> Yes, I do. [[WorkNode]]
            -> No thanks. [[EndNode]]
            ===
            title: WorkNode
            ---
            Npc: Great, follow me.
            ===
            title: EndNode
            ---
            Npc: Farewell.
            ===
            """);

        var result = Importer.Import(path);

        // Start block: nodes 1 (NPC), 2 (choice Yes), 3 (choice No)
        // WorkNode block: node 4
        // EndNode block: node 5
        Assert.Equal(5, result.Nodes.Count);

        var choiceYes = result.Nodes.Single(n => n.DefaultText == "Yes, I do.");
        Assert.Single(choiceYes.Links);
        Assert.Equal(4, choiceYes.Links[0].ToNodeId);

        var choiceNo = result.Nodes.Single(n => n.DefaultText == "No thanks.");
        Assert.Single(choiceNo.Links);
        Assert.Equal(5, choiceNo.Links[0].ToNodeId);
    }

    // ── Dangling jump ─────────────────────────────────────────────────────

    [Fact]
    public void Import_DanglingJump_SkipsLink()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Npc: Going somewhere?
            -> Head out. [[NonExistentBlock]]
            ===
            """);

        var result = Importer.Import(path);

        var choice = result.Nodes.Single(n => n.IsPlayerChoice);
        Assert.Empty(choice.Links);
    }

    // ── Command lines skipped ─────────────────────────────────────────────

    [Fact]
    public void Import_CommandLines_Skipped()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            <<fade_out>>
            Guard: Stop right there!
            <<fade_in>>
            ===
            """);

        var result = Importer.Import(path);

        Assert.Single(result.Nodes);
        Assert.Equal("Stop right there!", result.Nodes[0].DefaultText);
    }

    // ── SuggestedName from filename ───────────────────────────────────────

    [Fact]
    public void Import_SuggestedName_FromFilename()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Guard: Hello.
            ===
            """, "village_guard");

        var result = Importer.Import(path);

        Assert.Equal("village_guard", result.SuggestedName);
    }

    // ── Texts match nodes ─────────────────────────────────────────────────

    [Fact]
    public void Import_TextsMatchNodes()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Npc: Greetings.
            -> I seek passage. [[Next]]
            ===
            title: Next
            ---
            Npc: Welcome.
            ===
            """);

        var result = Importer.Import(path);

        Assert.Equal(result.Nodes.Count, result.Texts.Count);
        foreach (var node in result.Nodes)
        {
            var text = result.Texts.SingleOrDefault(t => t.NodeId == node.NodeId);
            Assert.NotNull(text);
            Assert.Equal(node.DefaultText, text.DefaultText);
            Assert.Equal("", text.FemaleText);
        }
    }

    // ── Narrator speaker ──────────────────────────────────────────────────

    [Fact]
    public void Import_NarratorSpeaker_SetsNarratorCategory()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Narrator: The sun sets over the horizon.
            ===
            """);

        var result = Importer.Import(path);

        Assert.Single(result.Nodes);
        var node = result.Nodes[0];
        Assert.Equal(SpeakerCategory.Narrator, node.SpeakerCategory);
        Assert.False(node.IsPlayerChoice);
        Assert.Equal("The sun sets over the horizon.", node.DefaultText);
    }

    // ── Empty file ────────────────────────────────────────────────────────

    [Fact]
    public void Import_EmptyFile_ThrowsFormatException()
    {
        var path = WriteTempYarn("");

        Assert.Throws<FormatException>(() => Importer.Import(path));
    }

    // ── Link metadata ─────────────────────────────────────────────────────

    [Fact]
    public void Import_Links_HaveCorrectFromNodeIdAndDefaultWeight()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Npc: Choose wisely.
            -> Option A.
            ===
            """);

        var result = Importer.Import(path);

        var npcNode = result.Nodes[0];
        var link = npcNode.Links.Single();
        Assert.Equal(npcNode.NodeId, link.FromNodeId);
        Assert.Equal(1f, link.RandomWeight);
        Assert.Equal("", link.QuestionNodeTextDisplay);
        Assert.False(link.HasConditions);
        Assert.Null(link.Conditions);
    }

    // ── NodeEditSnapshot blank fields ─────────────────────────────────────

    [Fact]
    public void Import_NodeBlankFields_HaveCorrectDefaults()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Guard: Hello.
            ===
            """);

        var result = Importer.Import(path);

        var node = result.Nodes[0];
        Assert.Equal("", node.SpeakerGuid);
        Assert.Equal("", node.ListenerGuid);
        Assert.Equal("", node.ActorDirection);
        Assert.Equal("", node.Comments);
        Assert.Equal("", node.ExternalVO);
        Assert.False(node.HasVO);
        Assert.False(node.HideSpeaker);
        Assert.Equal("Conversation", node.DisplayType);
        Assert.Equal("None", node.Persistence);
        Assert.Empty(node.Conditions);
        Assert.Empty(node.Scripts);
    }

    // ── File extensions ───────────────────────────────────────────────────

    [Fact]
    public void FileExtensions_ContainsYarn()
    {
        Assert.Contains(".yarn", Importer.FileExtensions);
    }

    // ── Comment lines skipped ─────────────────────────────────────────────

    [Fact]
    public void Import_CommentLines_Skipped()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            // This is a comment
            Guard: Halt!
            // Another comment
            ===
            """);

        var result = Importer.Import(path);

        Assert.Single(result.Nodes);
        Assert.Equal("Halt!", result.Nodes[0].DefaultText);
    }

    // ── No-prefix line treated as Npc/Unknown speaker ─────────────────────

    [Fact]
    public void Import_NoPrefixLine_TreatedAsNpcWithUnknownSpeaker()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Just some text without a speaker.
            ===
            """);

        var result = Importer.Import(path);

        Assert.Single(result.Nodes);
        var node = result.Nodes[0];
        Assert.Equal(SpeakerCategory.Npc, node.SpeakerCategory);
        Assert.False(node.IsPlayerChoice);
        Assert.Equal("Just some text without a speaker.", node.DefaultText);
    }

    // ── Player speaker label ──────────────────────────────────────────────

    [Fact]
    public void Import_PlayerSpeakerLabel_SetsPlayerCategory()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Npc: Pick one.
            Player: I choose this!
            ===
            """);

        var result = Importer.Import(path);

        var playerNode = result.Nodes.Single(n => n.DefaultText == "I choose this!");
        Assert.True(playerNode.IsPlayerChoice);
        Assert.Equal(SpeakerCategory.Player, playerNode.SpeakerCategory);
    }

    // ── Skipped-construct warnings ────────────────────────────────────────

    [Fact]
    public void Import_SkippedConstructs_ReportedAsWarnings()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            <<if $gold > 10>>
            Merchant: You can afford this.
            <<set $seen = true>>
            <<endif>>
            ===
            """);

        var result = Importer.Import(path);

        Assert.Contains(result.Warnings, w => w.Construct == "if"  && w.Count == 1);
        Assert.Contains(result.Warnings, w => w.Construct == "set" && w.Count == 1);
        Assert.Contains(result.Warnings, w => w.Construct == "endif" && w.Count == 1);
    }

    [Fact]
    public void Import_RepeatedConstruct_TalliesCount()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            <<if $a>>
            Npc: One.
            <<if $b>>
            Npc: Two.
            ===
            """);

        var result = Importer.Import(path);

        Assert.Contains(result.Warnings, w => w.Construct == "if" && w.Count == 2);
    }

    [Fact]
    public void Import_NoConstructs_HasNoWarnings()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Npc: Plain dialogue only.
            ===
            """);

        var result = Importer.Import(path);

        Assert.Empty(result.Warnings);
    }
}
