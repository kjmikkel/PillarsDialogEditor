using Dock.Model.Controls;
using Dock.Model.Core;
using DialogEditor.Avalonia.Docking;

namespace DialogEditor.Tests.Docking;

public class DockLayoutStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"layout-{Guid.NewGuid():N}.json");
    public void Dispose() { try { File.Delete(_path); } catch { /* best-effort */ } }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
        => Assert.Null(new DockLayoutStore().Load(_path));

    [Fact]
    public void Load_CorruptFile_ReturnsNull_DoesNotThrow()
    {
        File.WriteAllText(_path, "{ not valid dock json");
        Assert.Null(new DockLayoutStore().Load(_path));   // logged, not thrown
    }

    [Fact]
    public void SaveThenLoad_RoundTripsStructure()
    {
        DialogEditor.ViewModels.Resources.Loc.Configure(new DialogEditor.Tests.Helpers.StubStringProvider());
        var factory = new EditorDockFactory(
            new DialogEditor.ViewModels.GameBrowserViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.ConversationViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.NodeDetailViewModel(),
            new DialogEditor.ViewModels.ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));
        var layout = factory.CreateLayout();

        var store = new DockLayoutStore();
        store.Save(layout, _path);
        var restored = store.Load(_path);

        Assert.NotNull(restored);
        // Assert a known id survives the round trip:
        Assert.Contains("Browser", Flatten(restored!));
    }

    // Discovered empirically (see task-9-report.md): Dock.Serializer.SystemTextJson's custom
    // ObservableCollection converter doesn't participate in ReferenceHandler.Preserve, so every
    // ActiveDockable/DefaultDockable is re-serialized as a DETACHED duplicate subtree, not a
    // $ref to the matching VisibleDockables entry — and leaf wrapper types (BrowserTool etc.)
    // have no parameterless constructor, so their [JsonIgnore] Inner ends up null after
    // deserialization. EditorDockFactory.RestoreLayout must fix both: swap in fresh, live-wired
    // wrapper instances by Id, and reconcile ActiveDockable/DefaultDockable back to the
    // (now-fresh) VisibleDockables entries.
    [Fact]
    public void RestoreLayout_ReattachesLiveInnerOnBrowserTool()
    {
        DialogEditor.ViewModels.Resources.Loc.Configure(new DialogEditor.Tests.Helpers.StubStringProvider());
        var browser = new DialogEditor.ViewModels.GameBrowserViewModel(new DialogEditor.Tests.Helpers.StubDispatcher());
        var factory = new EditorDockFactory(
            browser,
            new DialogEditor.ViewModels.ConversationViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.NodeDetailViewModel(),
            new DialogEditor.ViewModels.ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));
        var layout = factory.CreateLayout();

        var store = new DockLayoutStore();
        store.Save(layout, _path);
        var restored = store.Load(_path);
        Assert.NotNull(restored);

        factory.RestoreLayout(restored!);

        var browserTool = FindById(restored!, "Browser") as BrowserTool;
        Assert.NotNull(browserTool);
        Assert.Same(browser, browserTool!.Inner);
    }

    [Fact]
    public void RestoreLayout_ReconcilesActiveDockableReferences()
    {
        DialogEditor.ViewModels.Resources.Loc.Configure(new DialogEditor.Tests.Helpers.StubStringProvider());
        var factory = new EditorDockFactory(
            new DialogEditor.ViewModels.GameBrowserViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.ConversationViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.NodeDetailViewModel(),
            new DialogEditor.ViewModels.ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));
        var layout = factory.CreateLayout();

        var store = new DockLayoutStore();
        store.Save(layout, _path);
        var restored = store.Load(_path);
        Assert.NotNull(restored);

        factory.RestoreLayout(restored!);

        var leftDock = (IDock)FindById(restored!, "LeftPane")!;
        Assert.Same(leftDock.VisibleDockables![0], leftDock.ActiveDockable);
    }

    private static IDockable? FindById(IDockable d, string id)
    {
        if (d.Id == id) return d;
        if (d is IDock dock && dock.VisibleDockables is not null)
            foreach (var c in dock.VisibleDockables)
                if (FindById(c, id) is { } found) return found;
        return null;
    }

    private static IEnumerable<string?> Flatten(Dock.Model.Core.IDockable d)
    {
        yield return d.Id;
        if (d is Dock.Model.Core.IDock dock && dock.VisibleDockables is not null)
            foreach (var c in dock.VisibleDockables) foreach (var x in Flatten(c)) yield return x;
    }
}
