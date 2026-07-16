using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Analytics;

public class PathStatsServiceTests
{
    private static NodeEditSnapshot Node(
        int id, string defaultText = "", string femaleText = "",
        bool isPlayerChoice = false, string speaker = "",
        SpeakerCategory category = SpeakerCategory.Npc,
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, isPlayerChoice, category, speaker, "", defaultText, femaleText,
            "Conversation", "None", "", "", "", false, false, links ?? [], [], []);

    private static LinkEditSnapshot Link(int from, int to) => new(from, to, 1f, "", false);
    private static ConversationEditSnapshot Snap(params NodeEditSnapshot[] n) => new(n);

    [Fact]
    public void LongestAndShortest_TwoEndings()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 2)]),
            Node(1, "long",  isPlayerChoice: true, links: [Link(1, 3)]),
            Node(3, "aaa bbb ccc ddd"),           // 4 words
            Node(2, "short", isPlayerChoice: true)));

        Assert.Equal(6, report.DefaultLongestWords);   // start+long+4
        Assert.Equal(2, report.DefaultShortestWords);  // start+short
    }

    [Fact]
    public void HubLoop_Terminates_AndCountsNodeOnce()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a", links: [Link(0, 1)]),
            Node(1, "b", links: [Link(1, 2)]),
            Node(2, "c", links: [Link(2, 0)])));       // back-edge to ancestor 0

        Assert.Equal(3, report.DefaultLongestWords);   // a+b+c, loop cut
    }

    [Fact]
    public void PerOpeningChoice_ContentAndLongest()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 4)]),
            Node(1, "A", isPlayerChoice: true, links: [Link(1, 3)]),
            Node(3, "x y z"),                          // 3 words
            Node(4, "B", isPlayerChoice: true, links: [Link(4, 5)]),
            Node(5, "p")));                            // 1 word

        Assert.Equal(2, report.Branches.Count);
        var a = report.Branches.Single(b => b.ChoiceNodeId == 1);
        Assert.Equal(4, a.DefaultContentWords);        // A(1)+x y z(3)
        Assert.Equal(4, a.DefaultLongestWords);
        var b = report.Branches.Single(x => x.ChoiceNodeId == 4);
        Assert.Equal(2, b.DefaultContentWords);        // B(1)+p(1)
        Assert.Equal(2, b.DefaultLongestWords);
    }

    [Fact]
    public void WordsPerSpeaker_GroupedByGuid()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "aa bb", speaker: "npc1", links: [Link(0, 1)]),
            Node(1, "cc", speaker: "player", category: SpeakerCategory.Player, links: [Link(1, 2)]),
            Node(2, "dd", speaker: "npc1")));

        Assert.Equal(3, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "npc1").DefaultWords);
        Assert.Equal(1, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "player").DefaultWords);
    }

    [Fact]
    public void FemaleGate_NotSignificant_WhenNoFemaleText()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a b", links: [Link(0, 1)]),
            Node(1, "c d")));

        Assert.False(report.HasSignificantFemaleVariant);
        Assert.Equal(report.DefaultTotalWords, report.FemaleTotalWords);
    }

    [Fact]
    public void FemaleGate_Significant_WhenFemaleDiffersOver10Percent()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a", links: [Link(0, 1)]),
            Node(1, "b", femaleText: "one two three four five")));   // 5 vs 1

        Assert.True(report.HasSignificantFemaleVariant);
        Assert.Equal(2, report.DefaultTotalWords);   // a + b
        Assert.Equal(6, report.FemaleTotalWords);    // a + five
    }

    [Fact]
    public void EmptySnapshot_EmptyReport()
    {
        var report = PathStatsService.Analyze(Snap());
        Assert.Equal(0, report.DefaultTotalWords);
        Assert.Empty(report.WordsPerSpeaker);
        Assert.Empty(report.Branches);
    }

    [Fact]
    public void NoRoot_HeaderZero_ButSpeakersCounted()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(5, "hello world", speaker: "x")));   // no node 0

        Assert.Equal(0, report.DefaultLongestWords);
        Assert.Empty(report.Branches);
        Assert.Equal(2, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "x").DefaultWords);
    }
}
