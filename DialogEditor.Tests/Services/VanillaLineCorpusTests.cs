using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class VanillaLineCorpusTests
{
    private const string Long  = "the wind howls through the rigging tonight";
    private const string Long2 = "she says the wind howls through the rigging";

    private static ConversationNode Node(int id) =>
        new(id, false, SpeakerCategory.Npc, "spk", "", [], [], [], "Conversation", "None");

    private static Conversation Conv(string name, params (int Id, string Def, string Fem)[] lines) =>
        new(name,
            lines.Select(l => Node(l.Id)).ToList(),
            new StringTable(lines.Select(l => new StringEntry(l.Id, l.Def, l.Fem))));

    [Fact]
    public void Load_EmitsDefaultAndFemaleText()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", (1, "  " + Long + " ", Long2)));

        var lines = VanillaLineCorpus.Load(provider);

        Assert.Equal(2, lines.Count);
        Assert.Contains(new VanillaLine("c", 1, Long, false), lines);   // trimmed
        Assert.Contains(new VanillaLine("c", 1, Long2, true), lines);
    }

    [Fact] // Same 4-word floor as the writer side, applied at load so the index stays small.
    public void Load_DropsBlankAndShortLines()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            Conv("c", (1, "Yes.", ""), (2, "", ""), (3, "three words only", ""), (4, Long, "")));

        var lines = VanillaLineCorpus.Load(provider);

        Assert.Equal(4, Assert.Single(lines).NodeId);
    }

    [Fact] // A node with no string-table entry (script-only) contributes nothing.
    public void Load_NodeWithoutStringEntry_Skipped()
    {
        var conv = new Conversation("c", [Node(1), Node(2)],
            new StringTable([new StringEntry(1, Long, "")]));
        var provider = new FakeGameDataProvider("poe2", "en", conv);

        Assert.Equal(1, Assert.Single(VanillaLineCorpus.Load(provider)).NodeId);
    }

    [Fact]
    public void Load_UnreadableConversation_SkippedOthersLoad()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            Conv("broken", (1, Long, "")), Conv("fine", (2, Long, "")));
        provider.Unreadable.Add("broken");

        var lines = VanillaLineCorpus.Load(provider);

        Assert.Equal("fine", Assert.Single(lines).ConversationName);
    }

    [Fact]
    public void Load_CancelledToken_Throws()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", (1, Long, "")));

        Assert.Throws<OperationCanceledException>(() =>
            VanillaLineCorpus.Load(provider, new CancellationToken(canceled: true)));
    }
}
