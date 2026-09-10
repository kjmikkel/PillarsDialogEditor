using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// ParameterValueViewModel.TypeHint tells the user what a condition or script
/// parameter expects. The condition and script editors bind it to ToolTip.Tip and to
/// AutomationProperties.HelpText, so it is read aloud by a screen reader as well as
/// hovered — squarely user-visible text.
/// </summary>
public class TypeHintTests
{
    public TypeHintTests() => Loc.Configure(new StubStringProvider());

    private static string Hint(string type) =>
        new ParameterValueViewModel { Type = type }.TypeHint;

    [Theory]
    [InlineData("String",         "ConditionHint_String")]
    [InlineData("Int32",          "ConditionHint_Int32")]
    [InlineData("Single",         "ConditionHint_Single")]
    [InlineData("Boolean",        "ConditionHint_Boolean")]
    [InlineData("Operator",       "ConditionHint_Operator")]
    [InlineData("GlobalVariable", "ConditionHint_GlobalVariable")]
    [InlineData("ObjectGuid",     "ConditionHint_ObjectGuid")]
    [InlineData("Guid",           "ConditionHint_Guid")]
    [InlineData("Conversation",   "ConditionHint_Conversation")]
    [InlineData("Quest",          "ConditionHint_Quest")]
    [InlineData("GameData",       "ConditionHint_GameData")]
    public void KnownTypes_TakeTheirHintFromResources(string type, string expectedKey)
    {
        Assert.Equal(expectedKey, Hint(type));
    }

    [Fact]
    public void EnumType_TakesItsHintFromResources()
    {
        Assert.Equal("ConditionHint_Enum", Hint("Enum:Some.Namespace+TheEnum"));
    }

    [Fact]
    public void UnknownType_FallsBackToAResource()
    {
        Assert.Equal("ConditionHint_Fallback", Hint("SomethingNew"));
    }

    [Fact]
    public void EmptyType_HasNoHintAtAll()
    {
        // HasTypeHint drives IsVisible on the hint row, so an empty type must stay
        // empty rather than resolving to a resource and showing a blank tooltip.
        Assert.Equal(string.Empty, Hint(""));
        Assert.False(new ParameterValueViewModel { Type = "" }.HasTypeHint);
    }
}

public class TypeHintResourceEndToEndTests
{
    public TypeHintResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    private static string Hint(string type) =>
        new ParameterValueViewModel { Type = type }.TypeHint;

    [AvaloniaFact]
    public void EveryKnownTypeResolves()
    {
        foreach (var type in new[]
                 {
                     "String", "Int32", "Single", "Boolean", "Operator", "GlobalVariable",
                     "ObjectGuid", "Guid", "Conversation", "Quest", "GameData",
                 })
        {
            var hint = Hint(type);
            Assert.False(string.IsNullOrWhiteSpace(hint));
            Assert.DoesNotContain("[ConditionHint_", hint);
        }
    }

    [AvaloniaFact]
    public void OperatorHint_StillListsTheGameSpellingOfEveryOperator()
    {
        // The operator names are written verbatim into game data, so the hint has to
        // keep them exactly — a translated "EqualTo" would send the user looking for
        // a value the game does not accept.
        var hint = Hint("Operator");

        foreach (var op in new[]
                 {
                     "EqualTo", "NotEqualTo", "GreaterThan", "LessThan",
                     "GreaterThanOrEqualTo", "LessThanOrEqualTo",
                 })
            Assert.Contains(op, hint);
    }

    [AvaloniaFact]
    public void EnumHint_NamesTheEnumWithDottedSeparators()
    {
        // The '+' Reflection uses for a nested type is rewritten to '.' for the reader.
        Assert.Equal("Enum value \u2014 type: Some.Namespace.TheEnum",
            Hint("Enum:Some.Namespace+TheEnum"));
    }

    [AvaloniaFact]
    public void FallbackHint_NamesTheUnrecognisedType()
    {
        Assert.Equal("Type: SomethingNew", Hint("SomethingNew"));
    }
}
