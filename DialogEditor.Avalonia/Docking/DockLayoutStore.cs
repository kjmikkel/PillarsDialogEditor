using System.Collections.Generic;
using System.IO;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Serializer.SystemTextJson;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Docking;

/// Persists the dock layout structure to JSON. Content (live VMs) is NOT serialized — each
/// tool/document wrapper's Inner property is [JsonIgnore]; the wrapper is re-attached to its
/// live VM by Id on load via EditorDockFactory.InitLayout's ContextLocator/DockableLocator.
/// A missing or corrupt file returns null so the caller falls back to the factory default;
/// this type never throws out of Save/Load/Delete — every failure is logged via AppLog.Warn.
public sealed class DockLayoutStore
{
    // Dock.Serializer.SystemTextJson.DockSerializer (confirmed via ilspycmd against the
    // installed 11.3.12.1 package — NOT "SystemTextJsonDockSerializer" as the early brief
    // draft guessed). Parameterless ctor uses ObservableCollection<> as the list type,
    // matching Dock.Model.Mvvm's collection type used by EditorDockFactory.
    private readonly IDockSerializer _serializer = new DockSerializer();

    public string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "layout.json");

    public void Save(IRootDock layout, string path)
    {
        // Once a layout has gone through EditorDockFactory.InitLayout (i.e. it's the live,
        // on-screen layout, not a freshly-CreateLayout'd template), FactoryBase.InitDockable
        // has set every dockable's Owner to its parent. Dock.Serializer.SystemTextJson's custom
        // IList<T> converter (JsonConverterList<T>.Write, confirmed via ilspycmd against the
        // installed 11.3.12.1 package) re-enters System.Text.Json via a nested
        // JsonSerializer.Serialize(writer, item, options) call per list item instead of writing
        // through the ambient writer state — and that nested call starts a BRAND NEW
        // ReferenceHandler.Preserve tracking scope. So a parent's VisibleDockables entry (list
        // item, tracked in the nested scope) whose Owner (plain, non-list property — tracked in
        // the OUTER scope) points back at that same parent is never recognised as "already
        // serialized": parent -> VisibleDockables[child] -> (new scope) -> child.Owner=parent ->
        // (new scope, parent never seen before) -> parent.VisibleDockables[child] -> ... forever,
        // tripping System.Text.Json's MaxDepth=64 guard (the reported
        // "$.MdiBounds.X"/"possible object cycle" is just WHERE the guard happened to fire, not
        // the cycle's origin). IDockable.Owner is the ONLY back-reference in play here — Context/
        // Factory (DockableBase) and Owner/Factory/Host (DockWindow) are all [IgnoreDataMember];
        // only DockableBase.Owner carries a plain [DataMember]. And Owner carries no information
        // the saved file needs: EditorDockFactory.InitLayout/RestoreLayout ALREADY reconstructs
        // Owner from scratch on every load (that's what FactoryBase.InitDockable does). So it's
        // safe, and correct, to omit Owner from what's written to disk: null every dockable's
        // Owner out of the tree for just the duration of Save, then restore the LIVE objects'
        // Owner afterwards (finally) so the running, on-screen layout is completely unaffected.
        var savedOwners = new List<(IDockable Dockable, IDockable? Owner)>();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            CollectAndClearOwners(layout, savedOwners, new HashSet<IDockable>(ReferenceEqualityComparer.Instance));

            using var s = File.Create(path);
            _serializer.Save(s, layout);
        }
        catch (Exception ex) { AppLog.Warn($"Dock layout save failed: {ex.Message}"); }
        finally
        {
            foreach (var (dockable, owner) in savedOwners) dockable.Owner = owner;
        }
    }

    private static void CollectAndClearOwners(
        IDockable node, List<(IDockable, IDockable?)> saved, HashSet<IDockable> visited)
    {
        if (!visited.Add(node)) return;

        saved.Add((node, node.Owner));
        node.Owner = null;

        if (node is IDock dock && dock.VisibleDockables is not null)
            foreach (var child in dock.VisibleDockables) CollectAndClearOwners(child, saved, visited);

        if (node is IRootDock root)
        {
            foreach (var l in new[]
                     {
                         root.HiddenDockables, root.LeftPinnedDockables, root.RightPinnedDockables,
                         root.TopPinnedDockables, root.BottomPinnedDockables,
                     })
                if (l is not null)
                    foreach (var d in l) CollectAndClearOwners(d, saved, visited);
            if (root.PinnedDock is { } pinned) CollectAndClearOwners(pinned, saved, visited);
            if (root.Windows is not null)
                foreach (var window in root.Windows)
                    if (window.Layout is not null) CollectAndClearOwners(window.Layout, saved, visited);
        }
    }

    public IRootDock? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var s = File.OpenRead(path);
            return _serializer.Load<IRootDock>(s);
        }
        catch (Exception ex) { AppLog.Warn($"Dock layout load failed, using default: {ex.Message}"); return null; }
    }

    public void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { AppLog.Warn($"Dock layout delete failed: {ex.Message}"); }
    }
}
