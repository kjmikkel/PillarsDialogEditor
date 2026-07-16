using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Docking;

/// Dock tool hosting the per-conversation condition/script search panel. Inner is
/// [JsonIgnore] so the layout serializer never persists the live VM — the factory
/// re-attaches it by Id on load.
public sealed class ConditionSearchTool : Tool
{
    [JsonIgnore] public ConditionSearchViewModel Inner { get; }

    public ConditionSearchTool(ConditionSearchViewModel inner)
    {
        Inner = inner;
        Id    = "ConditionSearch";
        Title = Loc.Get("Dock_Tool_ConditionSearch");
        CanClose = true;
    }
}
