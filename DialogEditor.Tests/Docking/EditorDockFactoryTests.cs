using Dock.Model.Controls;
using Dock.Model.Core;
using DialogEditor.Avalonia.Docking;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Docking;

public class EditorDockFactoryTests
{
    public EditorDockFactoryTests() => Loc.Configure(new StubStringProvider());

    private static EditorDockFactory MakeFactory() => new(
        new GameBrowserViewModel(new StubDispatcher()),
        new ConversationViewModel(new StubDispatcher()),
        new NodeDetailViewModel(),
        new ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));

    private static IEnumerable<IDockable> Descendants(IDockable d)
    {
        yield return d;
        if (d is IDock dock && dock.VisibleDockables is not null)
            foreach (var c in dock.VisibleDockables)
                foreach (var x in Descendants(c))
                    yield return x;
    }

    [Fact]
    public void CreateLayout_ContainsAllFourDockablesById()
    {
        var root = MakeFactory().CreateLayout();
        var ids  = Descendants(root).Select(d => d.Id).ToHashSet();
        Assert.Contains("Browser", ids);
        Assert.Contains("Canvas", ids);
        Assert.Contains("Details", ids);
        Assert.Contains("ConditionSearch", ids);
    }

    [Fact]
    public void CreateLayout_RightToolDock_TabsDetailsAndConditionSearch()
    {
        var root = MakeFactory().CreateLayout();
        var rightDock = Descendants(root).OfType<IToolDock>()
            .First(td => td.VisibleDockables!.Any(x => x.Id == "Details"));
        var ids = rightDock.VisibleDockables!.Select(x => x.Id).ToList();
        Assert.Contains("Details", ids);
        Assert.Contains("ConditionSearch", ids);   // tabbed together
    }

    [Fact]
    public void CreateLayout_RightToolDock_IncludesGameState()
    {
        var root = MakeFactory().CreateLayout();
        var rightDock = Descendants(root).OfType<IToolDock>().First(td => td.Id == "RightPane");
        Assert.Contains("GameState", rightDock.VisibleDockables!.Select(x => x.Id));
    }

    // Review focus 1: a layout.json saved before the Game State tool existed has no such dockable;
    // View > Game State must still add it to the right pane and show it.
    [Fact]
    public void ShowGameState_WithLayoutLackingTheTool_AddsItToTheRightPane()
    {
        var path = Path.Combine(Path.GetTempPath(), $"layout-{Guid.NewGuid():N}.json");
        try
        {
            var first = MakeFactory();
            var layout = first.CreateLayout();
            first.InitLayout(layout);
            // Simulate an old save: the tool is gone from the tree entirely.
            var tool = first.FindDockable(layout, d => d.Id == EditorDockFactory.GameStateId)!;
            ((IDock)tool.Owner!).VisibleDockables!.Remove(tool);
            new DockLayoutStore().Save(layout, path);

            var second = MakeFactory();
            var restored = new DockLayoutStore().Load(path)!;
            second.RestoreLayout(restored);
            Assert.Null(second.FindDockable(restored, d => d.Id == EditorDockFactory.GameStateId));

            var shown = second.ShowTool(restored, EditorDockFactory.GameStateId);

            Assert.NotNull(shown);
            var rightDock = Descendants(restored).OfType<IToolDock>().First(td => td.Id == "RightPane");
            Assert.Contains(rightDock.VisibleDockables!, d => d.Id == EditorDockFactory.GameStateId);
            Assert.NotNull(((DialogEditor.Avalonia.Docking.GameStateTool)shown!).Inner);
        }
        finally { try { File.Delete(path); } catch (Exception) { /* best-effort */ } }
    }
}
