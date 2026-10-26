using System.Text.Json.Serialization;

namespace DialogEditor.ViewModels.Services;

/// The two ways the editor presents a conversation: Reading (follow the text, no editing
/// chrome) and Editing (the full graph editor). The string converter keeps settings.json
/// hand-readable ("Reading" rather than 0).
[JsonConverter(typeof(JsonStringEnumConverter<EditorMode>))]
public enum EditorMode
{
    Reading,
    Editing,
}
