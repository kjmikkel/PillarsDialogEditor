namespace DialogEditor.ViewModels.Services;

/// A project-wide find query. Default + Female node text are always searched;
/// the three flags add optional coverage (link/choice text, all-language
/// translation overlays, writer node comments).
public sealed record ProjectFindQuery(
    string Text,
    bool CaseSensitive = false,
    bool InLinkChoice = false,
    bool InTranslations = false,
    bool InNodeComments = false);

/// One located match. Language is "" for the primary language (shown as the
/// primary label in the view); FieldLabel is a localized field-kind string.
public sealed record FindMatchRow(
    string ConversationName,
    int NodeId,
    string FieldLabel,
    string Language,
    string Snippet);
