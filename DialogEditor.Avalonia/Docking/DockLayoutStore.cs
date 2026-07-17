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
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var s = File.Create(path);
            _serializer.Save(s, layout);
        }
        catch (Exception ex) { AppLog.Warn($"Dock layout save failed: {ex.Message}"); }
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
