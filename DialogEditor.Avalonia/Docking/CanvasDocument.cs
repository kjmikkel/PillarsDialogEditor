using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Docking;

/// The canvas document — the anchor. Single document in v1; cannot be closed.
/// Inner is [JsonIgnore] so the layout serializer never persists the live VM —
/// the factory re-attaches it by Id on load.
public sealed class CanvasDocument : Document
{
    [JsonIgnore] public ConversationViewModel Inner { get; }

    public CanvasDocument(ConversationViewModel inner)
    {
        Inner = inner;
        Id    = "Canvas";
        Title = Loc.Get("Dock_Doc_Canvas");
        CanClose = false;
    }
}
