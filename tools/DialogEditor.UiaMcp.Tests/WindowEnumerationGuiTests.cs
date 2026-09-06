using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp.Tests;

/// <summary>
/// Live-app coverage for window enumeration (#16). WindowInventoryTests pins the rules
/// against a fake shaped like the probe; this pins that the real UIA tree still has that
/// shape — which markup and fakes cannot prove.
///
/// Deliberately ONE sequential test rather than several: opening windows mutates state
/// shared through the class fixture, and a modal in particular is sticky (it blocks the
/// menu invokes a later test would need). Ordering between xunit facts is not guaranteed,
/// so the sequence has to live inside a single fact to be honest.
/// </summary>
[Trait("Category", "Gui")]
public class WindowEnumerationGuiTests(EditorSessionFixture fixture) : IClassFixture<EditorSessionFixture>
{
    private void Open(params string[] path)
    {
        Assert.True(MenuNavigator.TryWalk(fixture.Session, path, out var leaf, out _, out var error),
            $"could not walk to [{string.Join("/", path)}]: {error}");
        var result = ElementOperator.Execute(fixture.Session.Tree(), leaf, ActionKind.Activate);
        Assert.StartsWith("ok", result);
        Thread.Sleep(1500);   // the window is created on the UI thread, after Invoke returns
    }

    [Fact]
    public void EnumeratesModelessAndModalWindowsAsTheyOpen()
    {
        var atRest = fixture.Session.Windows();
        Assert.Single(atRest);
        Assert.True(atRest[0].IsMain);
        Assert.False(atRest[0].IsModal);

        // Help > About is .Show() — a top-level sibling of the main window.
        Open("Help", "About…");
        var withAbout = fixture.Session.Windows();
        Assert.Equal(2, withAbout.Count);
        var about = withAbout.Single(w => w.Title == "About");
        Assert.False(about.IsModal);
        Assert.False(about.IsMain);
        Assert.Single(withAbout.Where(w => w.IsMain));

        // File > Settings is ShowDialog(this), so it nests UNDER the main window. This is
        // the assertion that would have failed against the fix issue #16 proposed:
        // enumerating only RootElement's children never sees it.
        Open("File", "Settings…");
        var withSettings = fixture.Session.Windows();
        var settings = withSettings.Single(w => w.Title == "Settings");
        Assert.True(settings.IsModal);
        Assert.Equal(3, withSettings.Count);
    }

    [Fact]
    public void TitlesStillResolveWhileWindowsCarryNoAutomationId()
    {
        // Honest about today's state: no Window element in the app has an AutomationId
        // yet, so the resolver's PRIMARY key matches nothing and every lookup lands on the
        // localised-title fallback. When the AutomationId sweep lands this test should be
        // joined by one resolving 'AboutWindow' by id -- and this one keeps the fallback
        // covered rather than being deleted.
        Assert.All(fixture.Session.Windows(), w => Assert.Equal("", w.AutomationId));

        var resolver = new WindowResolver(fixture.Session.Windows());
        var result = resolver.Resolve("Pillars Dialog Editor");

        Assert.Null(result.ErrorKind);
        Assert.True(result.Window!.IsMain);
    }
}
