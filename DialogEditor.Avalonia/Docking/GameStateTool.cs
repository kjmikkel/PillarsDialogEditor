using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Docking;

/// Dock tool hosting the Game State panel (issue 305). Inner is [JsonIgnore] so the layout
/// serializer never persists the live VM — the factory re-attaches it by Id on load.
public sealed class GameStateTool : Tool
{
    [JsonIgnore] public GameStateViewModel Inner { get; }

    public GameStateTool(GameStateViewModel inner)
    {
        Inner = inner;
        Id    = "GameState";
        Title = Loc.Get("Dock_Tool_GameState");
        CanClose = true;
    }
}
