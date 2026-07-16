using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Docking;

/// Dock tool hosting the node-detail editor. Inner is [JsonIgnore] so the layout
/// serializer never persists the live VM — the factory re-attaches it by Id on load.
public sealed class DetailsTool : Tool
{
    [JsonIgnore] public NodeDetailViewModel Inner { get; }

    public DetailsTool(NodeDetailViewModel inner)
    {
        Inner = inner;
        Id    = "Details";
        Title = Loc.Get("Dock_Tool_Details");
        CanClose = true;
    }
}
