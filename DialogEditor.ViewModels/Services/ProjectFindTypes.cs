namespace DialogEditor.ViewModels.Services;

/// A project-wide find query. Default + Female node text are always searched;
/// the three flags add optional coverage (link display setting, all-language
/// translation overlays, writer node comments).
///
/// InLinkDisplaySetting searches NodeLink.QuestionNodeTextDisplay, which is NOT
/// prose: it is an enum whose only legal values are ShowOnce, Always and Never.
/// Searching it is useful ("find every link set to Never"); the flag and its
/// labels are named for what it holds because the old name — "link choice text" —
/// read as writer-authored text and led to batch replace rewriting it by
/// substring, silently destroying the setting. See issue #24.
public sealed record ProjectFindQuery(
    string Text,
    bool CaseSensitive = false,
    bool InLinkDisplaySetting = false,
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
