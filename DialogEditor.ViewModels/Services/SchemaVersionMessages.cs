using DialogEditor.Patch.Schema;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// Turns UnsupportedSchemaVersionException into the localised "update to open it" text
/// (GitHub issue 62). Shared by the editor and the Patch Manager so both word it identically;
/// DialogEditor.Patch cannot reach Loc, which is why this lives here rather than on the exception.
public static class SchemaVersionMessages
{
    public static string Title => Loc.Get("Schema_TooNew_Title");

    public static string TooNew(UnsupportedSchemaVersionException ex, string fileName)
    {
        var format = FormatName(ex.Kind);
        return ex.ConversationName is null
            ? Loc.Format("Schema_TooNew", fileName, format, ex.Found, ex.Supported)
            : Loc.Format("Schema_TooNewInConversation", fileName, format, ex.Found, ex.Supported, ex.ConversationName);
    }

    /// The standalone Patch Manager's variant (GitHub issue 79): a player there updates the
    /// Pillars Dialog Patcher, not the editor, and nothing was written to the game.
    public static string TooNewForPatcher(UnsupportedSchemaVersionException ex, string fileName)
    {
        var format = FormatName(ex.Kind);
        return ex.ConversationName is null
            ? Loc.Format("Schema_TooNewForPatcher", fileName, format, ex.Found, ex.Supported)
            : Loc.Format("Schema_TooNewForPatcherInConversation", fileName, format, ex.Found, ex.Supported, ex.ConversationName);
    }

    // Literal keys (not $"Schema_Format_{kind}") so the resource guards can see every one.
    public static string FormatName(SchemaFileKind kind) => kind switch
    {
        SchemaFileKind.Project           => Loc.Get("Schema_Format_Project"),
        SchemaFileKind.ConversationPatch => Loc.Get("Schema_Format_ConversationPatch"),
        _                                => Loc.Get("Schema_Format_PatchList"),
    };
}
