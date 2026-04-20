using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class ConditionFormatterTests
{
    [Fact]
    public void Format_SimpleFunction_ReturnsFunctionWithParams()
    {
        var result = ConditionFormatter.Format(
            "Boolean IsGlobalValue(String, Operator, Int32)",
            ["some_flag", "EqualTo", "1"],
            not: false);

        Assert.Equal("IsGlobalValue(some_flag, EqualTo, 1)", result);
    }

    [Fact]
    public void Format_WithNot_PrefixesNOT()
    {
        var result = ConditionFormatter.Format(
            "Boolean IsCompanionActiveInParty(Guid)",
            ["b1a7e803-0000-0000-0000-000000000000"],
            not: true);

        Assert.Equal("NOT IsCompanionActiveInParty(b1a7e803-0000-0000-0000-000000000000)", result);
    }

    [Fact]
    public void Format_NoParameters_ReturnsEmptyParens()
    {
        var result = ConditionFormatter.Format(
            "Boolean SomeCheck()",
            [],
            not: false);

        Assert.Equal("SomeCheck()", result);
    }

    [Fact]
    public void Format_FunctionNameWithoutReturnType_ReturnsFullName()
    {
        var result = ConditionFormatter.Format(
            "IsReady",
            [],
            not: false);

        Assert.Equal("IsReady()", result);
    }
}
