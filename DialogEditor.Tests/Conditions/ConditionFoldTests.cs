using DialogEditor.Core.Conditions;

namespace DialogEditor.Tests.Conditions;

public class ConditionFoldTests
{
    private static IReadOnlyList<(bool Result, bool NextIsAnd)> L(params (bool r, bool and)[] c)
        => c.Select(x => (x.r, x.and)).ToList();

    [Fact]
    public void Poe2_AndThenFalse_ReturnsFalse()
        => Assert.False(ConditionFold.Poe2(L((true, true), (false, false))));

    [Fact]
    public void Poe2_SingleTrue_ReturnsTrue()
        => Assert.True(ConditionFold.Poe2(L((true, false))));

    [Fact]
    public void Poe1AndPoe2_Disagree_OnAOrBAndC()
    {
        // A=false(Or), B=true(And), C=false(last)
        var list = L((false, false), (true, true), (false, false));
        Assert.True(ConditionFold.Poe1(list));
        Assert.False(ConditionFold.Poe2(list));
    }

    [Fact]
    public void Poe2_TrueOrAnything_ShortCircuitsTrue()
        => Assert.True(ConditionFold.Poe2(L((true, false), (false, true), (false, false))));

    [Fact]
    public void Poe1_Middle_AndFalse_ReturnsFalse()
        => Assert.False(ConditionFold.Poe1(L((true, true), (false, false), (true, false))));
}
