using DialogEditor.Core.Services;

namespace DialogEditor.Tests.Parsing;

public class ConditionFormatterTests
{
    [Fact]
    public void Empty_list_returns_none_label()
    {
        var result = ConditionFormatter.Format([]);
        Assert.Equal("None", result);
    }

    [Fact]
    public void Single_condition_returned_as_is()
    {
        var result = ConditionFormatter.Format(["GlobalValue(\"HasMetNpc\") == 1"]);
        Assert.Equal("GlobalValue(\"HasMetNpc\") == 1", result);
    }

    [Fact]
    public void Multiple_conditions_joined_with_newline()
    {
        var result = ConditionFormatter.Format(["CondA", "CondB", "CondC"]);
        Assert.Equal("CondA\nCondB\nCondC", result);
    }
}
