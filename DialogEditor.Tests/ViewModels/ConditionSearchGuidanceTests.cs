using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// <summary>
/// GitHub issue 264 (F-19): the Condition search pane opened with a greyed-out Search button,
/// no example and a blank status line, so nothing said what to do or that results are
/// highlights on the canvas. The status line now starts with that guidance and returns to it
/// on Clear.
/// </summary>
public class ConditionSearchGuidanceTests
{
    public ConditionSearchGuidanceTests() => Loc.Configure(new StubStringProvider());

    private static ConditionSearchViewModel Vm() => new("poe2", () => null, _ => { }, () => { });

    [Fact]
    public void OpensWithGuidance()
        => Assert.Equal("CondSearch_Hint", Vm().MatchCountText);

    [Fact]
    public void Clear_ReturnsToTheGuidance()
    {
        var vm = Vm();
        vm.MatchCountText = "Matches: 3";
        vm.ClearCommand.Execute(null);
        Assert.Equal("CondSearch_Hint", vm.MatchCountText);
    }
}
