namespace DialogEditor.ViewModels.Services;

/// How a value's check count compares to its even-split fair share within its domain.
public enum BalanceFlag { Normal, Over, Under, Ignored }

/// One tallied value (a disposition axis or a faction): how often it is checked, its share
/// of the even-split expected count, and its fair-share flag. IsUnresolved marks a value seen
/// in the data but absent from the game's known domain (e.g. a stale GUID) — shown, not dropped.
public record BalanceRow(
    string DisplayValue, int Count, double ShareVsExpected, BalanceFlag Flag, bool IsUnresolved);

/// The full reputation/disposition balance over a set of conversations. Dispositions and
/// reputations are tallied into separate domains, each with its own fair-share baseline.
public record RepDispositionReport(
    IReadOnlyList<BalanceRow> DispositionRows,
    IReadOnlyList<BalanceRow> ReputationRows,
    int DispositionTotal,
    int ReputationTotal,
    int ConversationsAnalyzed);
