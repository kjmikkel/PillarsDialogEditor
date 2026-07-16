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
}
