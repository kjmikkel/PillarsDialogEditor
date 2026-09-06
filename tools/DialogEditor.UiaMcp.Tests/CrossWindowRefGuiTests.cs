using DialogEditor.UiaMcp.Tools;

namespace DialogEditor.UiaMcp.Tests;

/// <summary>
/// Its OWN fixture, and therefore its own app instance, deliberately: this test needs a
/// session with no modal in it, and WindowTargetingGuiTests parks one. Sharing a fixture
/// made it pass alone and fail in the full run -- xunit gives no ordering guarantee
/// between facts, so "the other test has not run yet" is not something to rely on.
/// </summary>
[Trait("Category", "Gui")]
public class CrossWindowRefGuiTests(EditorSessionFixture fixture) : IClassFixture<EditorSessionFixture>
{
    private void Open(params string[] path)
    {
        Assert.True(MenuNavigator.TryWalk(fixture.Session, path, out var leaf, out _, out var error),
            $"could not walk to [{string.Join("/", path)}]: {error}");
        Assert.StartsWith("ok", ElementOperator.Execute(
            fixture.Session.Tree(), leaf, DialogEditor.UiaMcp.Core.ActionKind.Activate));
        Thread.Sleep(1500);
    }

    [Fact]
    public void ARefMintedInOneWindowSaysSoRatherThanClaimingItWentStale()
    {
        var inspection = new InspectionTools(fixture.Session);
        Open("Help", "About…");

        // Mint refs against the About window, then use one against the main window.
        var aboutTree = inspection.ReadTree(window: "AboutWindow", filter: "all");
        var reference = System.Text.RegularExpressions.Regex.Match(aboutTree, @"ref_\d+").Value;
        Assert.NotEmpty(reference);

        var result = new ActionTools(fixture.Session).Invoke(reference: reference);

        // "Re-run read_tree" alone would send the caller to re-read the WRONG window and
        // hit the same wall -- the ref is live, it just belongs to another tree.
        Assert.StartsWith("Error(StaleRef)", result);
        Assert.Contains("window", result);
    }

}
