using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class RepDispositionTallyServiceTests : IDisposable
{
    public void Dispose() => GameDataNameService.Clear();

    private static ConditionLeaf Disp(string axis) =>
        new("Boolean DispositionEqual(Axis, Rank)", new[] { axis, "2" }, false, "And");

    private static ConditionLeaf Rep(string guid) =>
        new("Boolean IsReputation(Guid, RankType, Int32, Operator)",
            new[] { guid, "Good", "1", "GreaterThan" }, false, "And");

    private static NodeEditSnapshot NodeWith(params ConditionNode[] conds) =>
        new(0, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false,
            [], conds, []);

    private static (string, ConversationEditSnapshot) Conv(string name, params NodeEditSnapshot[] nodes) =>
        (name, new ConversationEditSnapshot(nodes));

    [Fact]
    public void Analyze_BucketsByValue_AndSeedsUnusedDomainValuesToZero()
    {
        GameDataNameService.Register("Disposition", new[]
        {
            new NamedEntry("Benevolent", "Benevolent"),
            new NamedEntry("Cruel", "Cruel"),
            new NamedEntry("Honest", "Honest"),
        });

        var conv = Conv("c1",
            NodeWith(Disp("Benevolent")),
            NodeWith(Disp("Benevolent")),
            NodeWith(Disp("Benevolent")));

        var report = RepDispositionTallyService.Analyze(
            new[] { conv }, "poe2", ConditionCatalogue.Instance);

        Assert.Equal(3, report.DispositionTotal);
        var benevolent = report.DispositionRows.First(r => r.DisplayValue == "Benevolent");
        Assert.Equal(3, benevolent.Count);
        Assert.Equal(BalanceFlag.Over, benevolent.Flag);          // 3 vs expected 1 → >= 2x
        var cruel = report.DispositionRows.First(r => r.DisplayValue == "Cruel");
        Assert.Equal(0, cruel.Count);
        Assert.Equal(BalanceFlag.Ignored, cruel.Flag);            // never checked
    }

    [Fact]
    public void Analyze_UnresolvedValue_ShownAsUnresolvedRow_NotDropped()
    {
        GameDataNameService.Register("Disposition", new[] { new NamedEntry("Benevolent", "Benevolent") });
        var conv = Conv("c1", NodeWith(Disp("Ghostly")));   // not in domain

        var report = RepDispositionTallyService.Analyze(
            new[] { conv }, "poe2", ConditionCatalogue.Instance);

        var row = report.DispositionRows.First(r => r.DisplayValue == "Ghostly");
        Assert.True(row.IsUnresolved);
        Assert.Equal(1, row.Count);
    }

    [Fact]
    public void Analyze_FairShareFlags_AtEveryBoundary()
    {
        // Four factions; counts A=4, B=1, C=3, D=0. total=8, expected=2.0.
        GameDataNameService.Register("Faction", new[]
        {
            new NamedEntry("Aedyr — a", "a"),
            new NamedEntry("Bael — b", "b"),
            new NamedEntry("Casita — c", "c"),
            new NamedEntry("Deadfire — d", "d"),
        });

        var nodes = new List<NodeEditSnapshot>();
        for (int i = 0; i < 4; i++) nodes.Add(NodeWith(Rep("a")));
        nodes.Add(NodeWith(Rep("b")));
        for (int i = 0; i < 3; i++) nodes.Add(NodeWith(Rep("c")));

        var report = RepDispositionTallyService.Analyze(
            new[] { ("c1", new ConversationEditSnapshot(nodes)) }, "poe2", ConditionCatalogue.Instance);

        BalanceRow Row(string prefix) => report.ReputationRows.First(r => r.DisplayValue.StartsWith(prefix));
        Assert.Equal(BalanceFlag.Over,    Row("Aedyr").Flag);   // 4 >= 4.0 (2x)
        Assert.Equal(BalanceFlag.Under,   Row("Bael").Flag);    // 1 <= 1.0 (0.5x)
        Assert.Equal(BalanceFlag.Normal,  Row("Casita").Flag);  // 3: not >=4, not <=1
        Assert.Equal(BalanceFlag.Ignored, Row("Deadfire").Flag);// 0
    }
}
