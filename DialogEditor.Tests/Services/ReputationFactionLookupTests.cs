using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// The PoE2 faction-reputation conditions take a faction GUID; that parameter must
/// use the "Faction" lookup so the editor offers faction names, not the (useless
/// here) Speaker/companion list. Regression guard for the mislabel where
/// ReputationRankEquals/Greater used lookupKind "Speaker".
public class ReputationFactionLookupTests
{
    [Theory]
    [InlineData("ReputationRankEquals")]
    [InlineData("ReputationRankGreater")]
    [InlineData("IsReputation")]
    public void FactionReputationCondition_FactionParam_UsesFactionLookup(string methodName)
    {
        var entry = ConditionCatalogue.Instance.All
            .First(e => e.MethodName == methodName);

        // The GUID parameter (the faction being checked) is the first parameter.
        var factionParam = entry.Parameters[0];

        Assert.Equal("Faction", factionParam.LookupKind);
    }
}
