using Dock.Model.Controls;
using Dock.Model.Core;
using DialogEditor.Avalonia.Docking;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Docking;

/// Issue 234: reading mode opens with the Condition search tool closed (it is for editors and the
/// Curious), but View > Condition search must still bring it back. Only the DEFAULT layout differs.
public class EditorDockFactoryReadingModeTests
{
    public EditorDockFactoryReadingModeTests() => Loc.Configure(new StubStringProvider());

    private static EditorDockFactory MakeFactory() => new(
        new GameBrowserViewModel(new StubDispatcher()),
        new ConversationViewModel(new StubDispatcher()),
        new NodeDetailViewModel(),
        new ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));

    private static IToolDock RightPane(IRootDock root)
    {
        IEnumerable<IDockable> Walk(IDockable d)
        {
            yield return d;
            if (d is IDock dock && dock.VisibleDockables is not null)
                foreach (var c in dock.VisibleDockables)
                    foreach (var x in Walk(c)) yield return x;
        }
        return Walk(root).OfType<IToolDock>().First(t => t.Id == "RightPane");
    }

    [Fact]
    public void DefaultReadingLayout_HidesConditionSearch_ButCanRestoreIt()
    {
        var factory = MakeFactory();
        var root = factory.CreateLayout(EditorMode.Reading);
        factory.InitLayout(root);

        Assert.Contains(root.HiddenDockables!, d => d.Id == EditorDockFactory.ConditionSearchId);
        Assert.DoesNotContain(RightPane(root).VisibleDockables!, d => d.Id == EditorDockFactory.ConditionSearchId);

        Assert.NotNull(factory.RestoreHiddenTool(root, EditorDockFactory.ConditionSearchId));

        Assert.Contains(RightPane(root).VisibleDockables!, d => d.Id == EditorDockFactory.ConditionSearchId);
        Assert.DoesNotContain(root.HiddenDockables ?? [], d => d.Id == EditorDockFactory.ConditionSearchId);
    }

    [Fact]
    public void DefaultEditingLayout_IsUnchanged()
    {
        var root = MakeFactory().CreateLayout(EditorMode.Editing);

        Assert.Contains(RightPane(root).VisibleDockables!, d => d.Id == EditorDockFactory.ConditionSearchId);
        Assert.Empty(root.HiddenDockables ?? []);
    }
}
