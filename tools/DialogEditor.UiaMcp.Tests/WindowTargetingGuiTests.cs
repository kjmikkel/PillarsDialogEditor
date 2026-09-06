using DialogEditor.UiaMcp.Tools;
using ModelContextProtocol.Protocol;

namespace DialogEditor.UiaMcp.Tests;

/// <summary>
/// The payoff of #16, against the real app: tools can address a window that is not the
/// main one, and refuse to act into a blocked one.
///
/// Its own fixture (so its own app instance) rather than sharing WindowEnumerationGuiTests':
/// this class parks a modal, and xunit does not guarantee fact ordering within a class, so
/// sharing would make one test's leftovers another's flake.
/// </summary>
[Trait("Category", "Gui")]
public class WindowTargetingGuiTests(EditorSessionFixture fixture) : IClassFixture<EditorSessionFixture>
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
    public void ToolsCanInspectAndCaptureASecondaryWindowAndRefuseABlockedOne()
    {
        var inspection = new InspectionTools(fixture.Session);
        var screenshot = new ScreenshotTool(fixture.Session);

        // Baseline: one window, so read_tree stays silent about the window census.
        Assert.DoesNotContain("windows(", inspection.ReadTree());

        Open("Help", "About…");

        // The census now announces the dialog, which is how a run DISCOVERS one opened
        // rather than reading the main window and reporting that as the whole truth.
        var mainTree = inspection.ReadTree();
        Assert.Contains("windows(2)", mainTree);
        Assert.Contains("AboutWindow", mainTree);

        // Addressing by ClassName reaches the dialog's own contents -- the thing that was
        // impossible before #16, since the tree was rooted at the main window.
        var aboutTree = inspection.ReadTree(window: "AboutWindow");
        Assert.DoesNotContain("Error(", aboutTree);
        Assert.NotEqual(mainTree, aboutTree);

        // screenshot follows the same selector. The About window is far smaller than the
        // 1400x850 main window, so the dimensions alone prove it captured the right rect
        // rather than the main window it used to be hard-wired to.
        var shot = screenshot.Screenshot(window: "AboutWindow").ToList();
        var caption = Assert.IsType<TextContentBlock>(shot[0]).Text;
        Assert.Contains("capture of 'About'", caption);
        Assert.Contains("className='AboutWindow'", caption);

        // Now the guard. Settings is ShowDialog, so it blocks input app-wide; acting on
        // the main window must be refused rather than silently doing nothing.
        Open("File", "Settings…");

        var blocked = new ActionTools(fixture.Session)
            .Invoke(window: "MainWindow", name: "File", controlType: "MenuItem");

        Assert.StartsWith("Error(ModalOpen)", blocked);
        Assert.Contains("Settings", blocked);

        // ...while the modal itself stays addressable, or the caller would be locked out
        // of the only window it can act on.
        Assert.DoesNotContain("Error(", inspection.ReadTree(window: "SettingsWindow"));

        // invoke_menu is the sharpest case of all: the app's menu bar belongs to the main
        // window, so under a modal the click dispatches, reports success, and does
        // nothing. That is the false green #16 describes, so it must be refused too.
        var menuBlocked = new ActionTools(fixture.Session).InvokeMenu(new[] { "File", "Settings…" });
        Assert.StartsWith("Error(ModalOpen)", menuBlocked);
    }
}
