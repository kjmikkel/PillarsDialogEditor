using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using DialogEditor.Avalonia.Docking;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Docking;

/// GitHub issue 319: a pane collapsed to the window edge ("pinned") came back after a restart
/// as a ~50 px pop-out. Two defects, both found in the running app:
///  - RestoreLayout swaps each deserialized tool wrapper for a fresh live instance, which threw
///    away the pop-out width Dock stores on the tool (PinnedBounds), so the pop-out fell back
///    to sizing to its content: Dock's 50 px minimum.
///  - Closing the app while a pop-out was open made the save throw (Dock's transient
///    RootDock.PinnedDock drags its Factory's IDockable-keyed dictionaries into the JSON), and
///    the save had already truncated layout.json, so the user's layout was lost entirely.
public class DockLayoutPinnedPaneTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"layout-{Guid.NewGuid():N}.json");
    public void Dispose() { try { File.Delete(_path); } catch { /* best-effort */ } }

    private static EditorDockFactory NewFactory()
    {
        Loc.Configure(new StubStringProvider());
        return new EditorDockFactory(
            new GameBrowserViewModel(new StubDispatcher()),
            new ConversationViewModel(new StubDispatcher()),
            new NodeDetailViewModel(),
            new ConditionSearchViewModel("poe2", () => null, _ => { }, () => { }));
    }

    private static IDockable Find(IRootDock root, string id)
    {
        IDockable? Walk(IDockable d)
        {
            if (d.Id == id) return d;
            var children = (d as IDock)?.VisibleDockables ?? [];
            if (d is IRootDock r)
                children = children.Concat(r.LeftPinnedDockables ?? []).Concat(r.RightPinnedDockables ?? [])
                                   .Concat(r.HiddenDockables ?? []).ToList();
            foreach (var c in children) if (Walk(c) is { } hit) return hit;
            return null;
        }
        return Walk(root) ?? throw new InvalidOperationException($"no dockable '{id}'");
    }

    // The live layout as the app holds it: created, initialised, Conversations pinned with the
    // width Dock recorded when it was pinned.
    private static (EditorDockFactory factory, IRootDock layout, IDockable browser) PinnedLayout()
    {
        var factory = NewFactory();
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        var browser = Find(layout, EditorDockFactory.BrowserId);
        factory.PinDockable(browser);
        browser.SetPinnedBounds(0, 0, 342, 978);
        Assert.Contains(browser, layout.LeftPinnedDockables!);
        return (factory, layout, browser);
    }

    [Fact]
    public void Restore_KeepsAPinnedPanesPopOutWidth()
    {
        var (_, layout, _) = PinnedLayout();
        var store = new DockLayoutStore();
        store.Save(layout, _path);

        var restored = store.Load(_path)!;
        NewFactory().RestoreLayout(restored);

        var browser = Find(restored, EditorDockFactory.BrowserId);
        Assert.IsType<BrowserTool>(browser);            // the fresh, live wrapper…
        browser.GetPinnedBounds(out _, out _, out var width, out var height);
        Assert.Equal(342, width);                        // …still opens as wide as it was
        Assert.Equal(978, height);
    }

    [Fact]
    public void Save_WithAPopOutOpen_WritesALoadableLayout_AndLeavesTheOpenPopOutAlone()
    {
        var (factory, layout, browser) = PinnedLayout();
        factory.PreviewPinnedDockable(browser);          // the pop-out is open as the app closes
        var open = layout.PinnedDock;
        Assert.NotNull(open);

        new DockLayoutStore().Save(layout, _path);

        var restored = new DockLayoutStore().Load(_path);
        Assert.NotNull(restored);
        Assert.Contains(EditorDockFactory.BrowserId, restored!.LeftPinnedDockables!.Select(d => d.Id));
        Assert.Same(open, layout.PinnedDock);             // the on-screen layout is untouched
    }

    [Fact]
    public void Save_ThatFails_KeepsThePreviousLayoutFile()
    {
        // Before the fix a failed save (as with the pop-out open) had already truncated
        // layout.json, so the next launch silently fell back to the default layout.
        var (_, layout, _) = PinnedLayout();
        var store = new DockLayoutStore();
        store.Save(layout, _path);
        var before = File.ReadAllText(_path);

        new DockLayoutStore(new FailsHalfwaySerializer()).Save(layout, _path);   // logged, not thrown

        Assert.Equal(before, File.ReadAllText(_path));
        Assert.NotNull(store.Load(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    /// Writes the start of a file, then throws: a save that fails part-way.
    private sealed class FailsHalfwaySerializer : IDockSerializer
    {
        public void Save<T>(Stream stream, T value)
        {
            stream.Write("{ \"$type\": "u8);
            throw new InvalidOperationException("fails part-way");
        }
        public T? Load<T>(Stream stream) => throw new NotSupportedException();
        public string Serialize<T>(T value) => throw new NotSupportedException();
        public T? Deserialize<T>(string text) => throw new NotSupportedException();
    }
}
