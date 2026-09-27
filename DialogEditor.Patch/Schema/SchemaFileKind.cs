namespace DialogEditor.Patch.Schema;

/// The three JSON file formats that carry a SchemaVersion (PROJECT-FORMAT.md, "Versioning policy").
/// A ConversationPatch also appears nested inside every .dialogproject.
public enum SchemaFileKind { Project, ConversationPatch, PatchList }
