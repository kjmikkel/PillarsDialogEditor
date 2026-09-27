using DialogEditor.Core.Localisation;

namespace DialogEditor.Patch.Schema;

/// A file was written by a newer editor than this build (issue #62). Loading is refused
/// before anything is deserialised, so nothing can be dropped on save or half-applied.
/// A plain Exception (InvalidDataException is sealed): every existing load handler catches
/// Exception, so it is still treated as a load failure; callers that can say "update the
/// editor" catch it first.
/// Message is English (logs and the English-only dialog-patcher); the GUI builds its own
/// localised text from Kind / Found / Supported via SchemaVersionMessages.
[NotLocalised("Diagnostic and dialog-patcher text; the GUI renders Kind / Found / Supported via SchemaVersionMessages")]
public sealed class UnsupportedSchemaVersionException(
    SchemaFileKind kind, int found, int supported, string? conversationName = null)
    : Exception(BuildMessage(kind, found, supported, conversationName))
{
    public SchemaFileKind Kind             { get; } = kind;
    public int            Found            { get; } = found;
    public int            Supported        { get; } = supported;
    /// Set when the offender is a ConversationPatch nested inside a project.
    public string?        ConversationName { get; } = conversationName;

    private static string BuildMessage(SchemaFileKind kind, int found, int supported, string? conversation)
    {
        var what  = SchemaFormats.EnglishName(kind);
        var where = conversation is null ? "" : $" (conversation '{conversation}')";
        return $"This file uses {what} {found}{where}, but this version reads up to {what} {supported}. " +
               "It was saved by a newer Pillars Dialog Editor; update the editor / dialog-patcher to read it.";
    }
}
