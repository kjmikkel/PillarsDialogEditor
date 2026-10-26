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
public class EditorDockFactoryReadingModeTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"layout-{Guid.NewGuid():N}.json");
    public void Dispose() { try { File.Delete(_path); } catch (Exception) { /* best-effort */ } }

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

    private static bool InAVisibleDock(IRootDock root, string id)
    {
        bool Walk(IDockable d) =>
            d is IDock dock && dock.VisibleDockables is { } kids
            && (kids.Any(k => k.Id == id) || kids.Any(Walk));
        return Walk(root);
    }

    // The saved-layout route: a tool hidden before exit comes back after a restart (fresh factory,
    // layout read from disk), the way View > Condition search reaches it through ShowTool.
    [Fact]
    public void ShowTool_BringsBackAToolThatWasHiddenInASavedLayout()
    {
        var first = MakeFactory();
        var layout = first.CreateLayout();
        first.InitLayout(layout);
        first.HideDockable(first.FindDockable(layout, d => d.Id == EditorDockFactory.ConditionSearchId)!);
        Assert.False(InAVisibleDock(layout, EditorDockFactory.ConditionSearchId));
        new DockLayoutStore().Save(layout, _path);

        var second = MakeFactory();
        var restored = new DockLayoutStore().Load(_path)!;
        second.RestoreLayout(restored);

        var tool = second.ShowTool(restored, EditorDockFactory.ConditionSearchId);

        Assert.NotNull(tool);
        Assert.True(InAVisibleDock(restored, EditorDockFactory.ConditionSearchId));
    }

    [Fact]
    public void ShowTool_ForAnUnknownId_ReturnsNull_WithoutThrowing()
    {
        var factory = MakeFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);

        Assert.Null(factory.ShowTool(root, "NoSuchTool"));
    }

    [Fact]
    public void ShowTool_ForAToolAlreadyOpen_ReturnsIt()
    {
        var factory = MakeFactory();
        var root = factory.CreateLayout();
        factory.InitLayout(root);

        Assert.Equal(EditorDockFactory.ConditionSearchId, factory.ShowTool(root, EditorDockFactory.ConditionSearchId)?.Id);
    }
}
