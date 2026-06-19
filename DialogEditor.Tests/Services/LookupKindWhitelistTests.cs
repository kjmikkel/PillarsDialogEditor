using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class LookupKindWhitelistTests
{
    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal)
    {
        "Speaker", "Quest", "Item", "Ability", "GlobalVariable",
        "Class", "Race", "Subrace", "Background", "Culture",
        "Deity", "PaladinOrder", "Faction", "Disposition",
        "DispositionStrength", "Skill", "Phrase", "Keyword",
        "StatusEffect", "CreatureType", "Map", "Conversation",
        "WeaponType", "ArmorType"
    };

    [Fact]
    public void ConditionCatalogue_AllLookupKinds_AreInWhitelist()
    {
        var kinds = ConditionCatalogue.Instance.All
            .SelectMany(e => e.Parameters)
            .Select(p => p.LookupKind)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct();

        foreach (var kind in kinds)
            Assert.Contains(kind, KnownKinds);
    }

    [Fact]
    public void ScriptCatalogue_AllLookupKinds_AreInWhitelist()
    {
        var kinds = ScriptCatalogue.Instance.All
            .SelectMany(e => e.Parameters)
            .Select(p => p.LookupKind)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct();

        foreach (var kind in kinds)
            Assert.Contains(kind, KnownKinds);
    }
}
