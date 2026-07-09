namespace DialogEditor.Core.GameData;

/// Maps a GameData object's short $type name to its GameDataNameService lookup kind.
/// MUST mirror tools/catalogue-gen/generate.py `type_to_kind` (the generator stamps
/// these kind names into the catalogue's lookupKind fields offline; this side resolves
/// them at runtime). Exactly three rules — change both sides together.
public static class GameDataKindMapper
{
    public static string TypeToKind(string shortType)
    {
        if (shortType == "BaseStatsGameData") return "Class";
        if (shortType.EndsWith("ItemGameData", StringComparison.Ordinal)) return "Item";
        var kind = shortType.EndsWith("GameData", StringComparison.Ordinal)
            ? shortType[..^"GameData".Length]
            : shortType;
        return kind == "GenericAbility" ? "Ability" : kind;
    }
}
