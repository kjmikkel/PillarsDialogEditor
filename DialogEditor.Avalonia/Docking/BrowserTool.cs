using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Docking;

/// Dock tool hosting the conversations browser. Inner is [JsonIgnore] so the layout
/// serializer never persists the live VM — the factory re-attaches it by Id on load.
public sealed class BrowserTool : Tool
{
    [JsonIgnore] public GameBrowserViewModel Inner { get; }

    public BrowserTool(GameBrowserViewModel inner)
    {
        Inner = inner;
        Id    = "Browser";
        Title = Loc.Get("Dock_Tool_Browser");
        CanClose = true;
    }
}
