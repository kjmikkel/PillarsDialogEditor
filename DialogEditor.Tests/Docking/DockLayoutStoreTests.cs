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
    // Regression for the app-close defect: the real app calls factory.InitLayout(layout) before
    // ever saving (MainWindow.BuildDock does this on every layout, freshly-created or restored).
    // InitLayout wires Owner/Factory/Context back-references (and MdiBounds tracking state) onto
    // the model — a graph the PREVIOUS version of this test never exercised, because it saved
    // straight from CreateLayout() with no InitLayout call. That gap is exactly why the 0-byte-
    // file/"object cycle" defect (AppLog.Warn: "Dock layout save failed: A possible object cycle
    // was detected... Path: $.MdiBounds.X") shipped past this test suite.
    [Fact]
    public void SaveThenLoad_RoundTripsStructure_AfterInitLayout()
    {
        DialogEditor.ViewModels.Resources.Loc.Configure(new DialogEditor.Tests.Helpers.StubStringProvider());
        var factory = new EditorDockFactory(
            new DialogEditor.ViewModels.GameBrowserViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.ConversationViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.NodeDetailViewModel(),
            new DialogEditor.ViewModels.ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        var store = new DockLayoutStore();
        store.Save(layout, _path);

        Assert.True(File.Exists(_path));
        Assert.True(new FileInfo(_path).Length > 0, "layout.json must not be 0 bytes after Save");

        var restored = store.Load(_path);
        Assert.NotNull(restored);
        Assert.Contains("Browser", Flatten(restored!));
    }

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

    // Fix pass 2 (review): DockLayoutStore.Load only guards against unparseable JSON — a file
    // containing syntactically valid JSON that just isn't a dock layout (e.g. hand-edited,
    // or from an unrelated schema) must still come back as null/no-throw, never bubble an
    // exception up to the BuildDock restore-consumption site. (BuildDock's own try/catch
    // around RestoreLayout/InitLayout is exercised at the GUI level by the coordinator, since
    // it lives in code-behind with no unit-test seam; this covers the Load half we can reach.)
    [Fact]
    public void Load_ValidJsonButNotADockLayout_ReturnsNull_DoesNotThrow()
    {
        File.WriteAllText(_path, "{ \"foo\": \"bar\", \"nested\": { \"a\": 1 } }");
        var ex = Record.Exception(() => new DockLayoutStore().Load(_path));
        Assert.Null(ex);
    }

    // Fix pass 2 (review) Minor #2: ReplaceInList's fall-through for a recognised wrapper id
    // whose DockableLocator entry can't be resolved (e.g. a future maintenance slip where
    // WrapperIds and DockableLocator drift out of sync, or — as reproduced here — the locator
    // simply isn't wired yet) must not throw and must leave the wrapper as-is rather than
    // crashing; the AppLog.Warn added at that site is a diagnostic (verified by code review /
    // GUI log inspection, not asserted here since AppLog writes to the real app.log file with
    // no test seam).
    [Fact]
    public void ReplaceWrappers_UnresolvableWrapperId_DoesNotThrow()
    {
        DialogEditor.ViewModels.Resources.Loc.Configure(new DialogEditor.Tests.Helpers.StubStringProvider());
        var factory = new EditorDockFactory(
            new DialogEditor.ViewModels.GameBrowserViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.ConversationViewModel(new DialogEditor.Tests.Helpers.StubDispatcher()),
            new DialogEditor.ViewModels.NodeDetailViewModel(),
            new DialogEditor.ViewModels.ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));

        // A layout whose wrapper carries a recognised WrapperIds value ("Browser"), but the
        // factory's DockableLocator is deliberately never populated (SetLocators is never
        // called) — simulating the "locator can't resolve this known wrapper id" case via
        // reflection into the private ReplaceWrappers/ReplaceInList pair RestoreLayout uses.
        var layout = factory.CreateLayout();

        var method = typeof(EditorDockFactory).GetMethod(
            "ReplaceWrappers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var ex = Record.Exception(() => method!.Invoke(factory, new object[] { layout }));
        Assert.Null(ex);
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
