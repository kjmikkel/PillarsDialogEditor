using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.GameData;

public class GameDataKindMapperTests
{
    [Theory]
    [InlineData("ShipGameData", "Ship")]                    // plain suffix strip
    [InlineData("ChangeStrengthGameData", "ChangeStrength")]
    [InlineData("BaseStatsGameData", "Class")]              // rule 1: explicit override
    [InlineData("ConsumableItemGameData", "Item")]          // rule 2: *ItemGameData -> Item
    [InlineData("ItemGameData", "Item")]
    [InlineData("GenericAbilityGameData", "Ability")]       // rule 3 alias
    [InlineData("NotAGameDataType", "NotAGameDataType")]    // no suffix: pass through
    public void TypeToKind_MapsPerContract(string shortType, string expected)
        => Assert.Equal(expected, GameDataKindMapper.TypeToKind(shortType));
}
