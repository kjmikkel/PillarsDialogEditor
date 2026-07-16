using DialogEditor.Core.Models;

namespace DialogEditor.ViewModels.Services;

public enum FactionCheckDomain { Disposition, Reputation }

public record FactionCheck(FactionCheckDomain Domain, string RawValue);

/// Classifies a condition leaf as a reputation/disposition check and enumerates the
/// full value domain per game (so never-checked values can be shown as zero rows).
/// Lives in DialogEditor.ViewModels because it needs the ConditionCatalogue (which lives
/// here) and GameDataNameService. Pure: no IO.
public static class FactionCheckClassifier
{
    private static readonly HashSet<string> DispositionMethods = new(StringComparer.OrdinalIgnoreCase)
        { "DispositionEqual", "DispositionGreaterOrEqual", "IsDisposition" };
    private static readonly HashSet<string> ReputationMethods = new(StringComparer.OrdinalIgnoreCase)
        { "ReputationRankEquals", "ReputationRankGreater", "IsReputation", "ReputationRankByTagEquals" };

    /// Returns the checked value (disposition axis / faction identity) or null when the leaf
    /// is not a reputation/disposition check. The checked value is parameter index 0 for every
    /// known entry.
    public static FactionCheck? Classify(ConditionLeaf leaf, string gameId, ConditionCatalogue catalogue)
    {
        var entry = catalogue.FindByFullName(leaf.FullName, gameId);
        if (entry is null || leaf.Parameters.Count == 0) return null;

        var method = entry.MethodName;
        var domain =
            DispositionMethods.Contains(method) ? FactionCheckDomain.Disposition :
            ReputationMethods.Contains(method)  ? FactionCheckDomain.Reputation  :
            (FactionCheckDomain?)null;
        if (domain is null) return null;

        return new FactionCheck(domain.Value, leaf.Parameters[0]);
    }

    /// The full set of possible values for a domain, so never-checked values appear as 0 rows.
    /// Reputation → the Faction GUID lookup. Disposition → the Disposition GUID lookup (PoE2)
    /// or, when absent, the PoE1 Axis enum options from the catalogue.
    public static IReadOnlyList<NamedEntry> PossibleValues(
        FactionCheckDomain domain, string gameId, ConditionCatalogue catalogue)
    {
        if (domain == FactionCheckDomain.Reputation)
            return GameDataNameService.Get("Faction");

        var lookup = GameDataNameService.Get("Disposition");
        if (lookup.Count > 0) return lookup;

        var entry = catalogue.Find("DispositionEqual");
        var options = entry?.Parameters.FirstOrDefault()?.Options ?? [];
        return options.Select(o => new NamedEntry(o, o)).ToList();
    }

    /// Resolves a stored value to its display name (GUID → name), or returns the raw value
    /// when it is already a display value or cannot be resolved (an unresolved/stale value).
    public static string ResolveDisplay(
        FactionCheckDomain domain, string rawValue, string gameId, ConditionCatalogue catalogue)
    {
        foreach (var e in PossibleValues(domain, gameId, catalogue))
            if (string.Equals(e.StoredValue, rawValue, StringComparison.OrdinalIgnoreCase))
                return e.DisplayName;
        return rawValue;
    }
}
