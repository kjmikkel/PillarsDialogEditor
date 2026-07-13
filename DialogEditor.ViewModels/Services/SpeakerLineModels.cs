namespace DialogEditor.ViewModels.Services;

/// Which text of a node a row represents. A node with female text yields a
/// second, Female row in addition to its Default row.
public enum LineVariant { Default, Female }

/// A line's provenance relative to the project: unchanged game text, an edit
/// to an existing node, or a brand-new node the project adds.
public enum LineOrigin { Vanilla, Edited, New }

/// One spoken line by one speaker, located in a conversation, with provenance.
public record SpeakerLineRow(
    string      SpeakerGuid,
    string      ConversationName,
    int         NodeId,
    LineVariant Variant,
    string      LineText,
    LineOrigin  Origin);
