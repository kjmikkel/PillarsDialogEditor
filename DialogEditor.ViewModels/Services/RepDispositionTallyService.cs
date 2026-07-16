using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels.Services;

/// Aggregates reputation/disposition checks by value into a balance report. Pure; no IO.
/// Walks each node's condition sites (own + link conditions; scripts are never checks),
/// classifies each leaf, buckets by value within its domain, seeds every domain value at 0,
/// and flags each row against the domain's even-split fair share.
public static class RepDispositionTallyService
{
    private const double OverFactor  = 2.0;
    private const double UnderFactor = 0.5;

    public static RepDispositionReport Analyze(
        IReadOnlyList<(string Name, ConversationEditSnapshot Snapshot)> conversations,
        string gameId, ConditionCatalogue catalogue)
    {
        var dispCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var repCounts  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, snap) in conversations)
            foreach (var node in snap.Nodes)
                foreach (var leaf in node.ConditionLeaves())
                {
                    var check = FactionCheckClassifier.Classify(leaf, gameId, catalogue);
                    if (check is null) continue;
                    var bucket = check.Domain == FactionCheckDomain.Disposition ? dispCounts : repCounts;
                    bucket[check.RawValue] = bucket.GetValueOrDefault(check.RawValue) + 1;
                }

        var dispRows = BuildRows(FactionCheckDomain.Disposition, dispCounts, gameId, catalogue);
        var repRows  = BuildRows(FactionCheckDomain.Reputation,  repCounts,  gameId, catalogue);

        return new RepDispositionReport(
            dispRows, repRows,
            dispCounts.Values.Sum(), repCounts.Values.Sum(),
            conversations.Count);
    }

    private static IReadOnlyList<BalanceRow> BuildRows(
        FactionCheckDomain domain, Dictionary<string, int> counts,
        string gameId, ConditionCatalogue catalogue)
    {
        // Seed every domain value at 0; append encountered-but-unresolved values.
        var seeded = new Dictionary<string, (string Display, bool Unresolved)>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in FactionCheckClassifier.PossibleValues(domain, gameId, catalogue))
            seeded[v.StoredValue] = (v.DisplayName, false);
        foreach (var raw in counts.Keys)
            if (!seeded.ContainsKey(raw))
                seeded[raw] = (FactionCheckClassifier.ResolveDisplay(domain, raw, gameId, catalogue), true);

        var total    = counts.Values.Sum();
        var rowCount = seeded.Count;
        var expected = rowCount > 0 ? (double)total / rowCount : 0;

        var rows = seeded.Select(kv =>
        {
            var count = counts.GetValueOrDefault(kv.Key);
            var flag =
                count == 0                                       ? BalanceFlag.Ignored :
                expected > 0 && count >= OverFactor  * expected  ? BalanceFlag.Over    :
                expected > 0 && count <= UnderFactor * expected  ? BalanceFlag.Under   :
                BalanceFlag.Normal;
            var share = expected > 0 ? count / expected : 0;
            return new BalanceRow(kv.Value.Display, count, share, flag, kv.Value.Unresolved);
        });

        static int FlagOrder(BalanceFlag f) => f switch
        { BalanceFlag.Over => 0, BalanceFlag.Under => 1, BalanceFlag.Ignored => 2, _ => 3 };

        return rows
            .OrderBy(r => FlagOrder(r.Flag))
            .ThenByDescending(r => r.Count)
            .ThenBy(r => r.DisplayValue, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
