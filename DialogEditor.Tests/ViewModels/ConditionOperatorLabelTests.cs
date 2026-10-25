using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// <summary>
/// GitHub issue 266: the condition editor showed an Operator parameter as the raw enum
/// "EqualTo" in a free-text box. It now offers a fixed list of plain labels ("equals",
/// "is at least") and still saves the enum name the game reads.
///
/// The bundled catalogues type these parameters "Enum:Operator" and list "Equals" where the
/// game's enum (Game.GameData.Operator / Conditionals.Operator) has "EqualTo", so picking it
/// saved a value the game can't parse. The labels come from the game's own names instead.
/// </summary>
public class ConditionOperatorLabelTests
{
    public ConditionOperatorLabelTests() => Loc.Configure(new StubStringProvider());

    // The shape the bundled conditions.json and scripts.json really use.
    private static ConditionParameter OperatorParam() => new(
        "Operator", "Enum:Operator", "Comparison operator.", "EqualTo",
        Options: ["Equals", "GreaterThan", "LessThan", "NotEqualTo", "GreaterThanOrEqualTo", "LessThanOrEqualTo"]);

    private static ConditionEntry Entry() => new(
        "IsGlobalValue", "Is Global Value", "Globals", ["poe2"], "",
        [
            new ConditionParameter("Tag", "String", "", "MyFlag"),
            OperatorParam(),
            new ConditionParameter("Value", "Int32", "", "0"),
        ]);

    private static ParameterValueViewModel Operator(string stored)
    {
        var leaf = new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)",
            ["MyFlag", stored, "1"], false, "And");
        return new ConditionRowViewModel(leaf, Entry()).Parameters[1];
    }

    [Fact]
    public void StoredOperator_IsShownAsItsPlainLabel()
        => Assert.Equal("ConditionOperator_GreaterThanOrEqualTo", Operator("GreaterThanOrEqualTo").Value);

    [Fact]
    public void Choices_AreThePlainLabels_InTheGamesOrder()
        => Assert.Equal(
            ["ConditionOperator_EqualTo", "ConditionOperator_GreaterThan", "ConditionOperator_LessThan",
             "ConditionOperator_NotEqualTo", "ConditionOperator_GreaterThanOrEqualTo", "ConditionOperator_LessThanOrEqualTo"],
            Operator("EqualTo").EnumOptions);

    [Fact]
    public void PickingALabel_SavesTheEnumName()
    {
        var op = Operator("EqualTo");
        op.Value = "ConditionOperator_LessThan";
        Assert.Equal("LessThan", op.EffectiveValue);
    }

    [Fact]
    public void Equals_FromTheOldCatalogue_IsReadAsEqualTo_AndSavedCorrectly()
    {
        var op = Operator("Equals");
        Assert.Equal("ConditionOperator_EqualTo", op.Value);
        Assert.Equal("EqualTo", op.EffectiveValue);
    }

    [Fact]
    public void UnknownStoredOperator_IsKeptAsIs()
    {
        var op = Operator("Approximately");
        Assert.Equal("Approximately", op.Value);
        Assert.Equal("Approximately", op.EffectiveValue);
    }

    [Fact]
    public void Operator_IsAFixedChoice_NotFreeText()
    {
        var op = Operator("EqualTo");
        Assert.True(op.IsFixedChoice);
        Assert.False(op.IsFreeEnum);
    }

    [Fact]
    public void OtherEnums_StayFreeText()
    {
        var p = new ParameterValueViewModel { Type = "Enum:RankType", Options = ["Good", "Bad"] };
        Assert.False(p.IsFixedChoice);
        Assert.True(p.IsFreeEnum);
    }

    [Fact]
    public void ScriptOperator_GetsTheSameLabels()
    {
        var entry = new ScriptCatalogueEntry("SetGlobalIfGlobal", "Set Global If Global", "Globals", ["poe2"], "",
            [new ConditionParameter("Tag", "String", "", ""), OperatorParam()]);
        var call = new ScriptCall("Void SetGlobalIfGlobal(String, Operator)", ["MyFlag", "NotEqualTo"], ScriptCategory.Enter);

        var op = new ScriptRowViewModel(call, entry).Parameters[1];

        Assert.Equal("ConditionOperator_NotEqualTo", op.Value);
        Assert.True(op.IsFixedChoice);
        Assert.Equal("NotEqualTo", op.EffectiveValue);
    }
}
