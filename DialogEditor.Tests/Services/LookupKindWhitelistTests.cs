using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class LookupKindWhitelistTests
{
    // The full set of lookup kinds the generated catalogue emits (tools/catalogue-gen).
    // Kinds derive from the GameData $type referenced by each parameter; many have no
    // runtime loader yet and stay dormant (raw display) until one is added — that is
    // safe (GameDataNameService.Get returns empty for an unregistered kind). This set
    // is a change-detector: a new kind means updating the generator's output here.
    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal)
    {
        "Ability", "Affliction", "AfflictionType", "AttackBase", "Attribute",
        "Background", "ChangeStrength", "CharacterClass", "CharacterSubClass",
        "Conversation", "CreatureType", "Culture", "Deity", "Disposition",
        "EncounterBiome", "Equippable", "Faction", "GameData", "GlobalVariable",
        "GodChallenge", "Item", "ItemMod", "Keyword", "LootList", "Map", "MetaTeam",
        "NoiseLevel", "PaladinOrder", "Phrase", "PlayerNamedFeature",
        "ProgressionUnlockable", "Quest", "Race", "Schedule",
        "ScriptedInteractionImage", "Ship", "ShipCaptain", "ShipCrewPersonality",
        "ShipDuelEvent", "ShipTriumph", "ShipTrophy", "ShipUpgrade", "Skill",
        "Speaker", "StatusEffect", "Team", "TextRollSettings", "Topic", "Tutorial",
        "VisualStateName", "WeatherPattern", "WorldMap", "WorldMapEncounter",
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
